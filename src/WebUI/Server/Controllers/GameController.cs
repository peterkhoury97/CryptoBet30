using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CryptoBet30.Application.Commands.Game;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IGameStateService _gameStateService;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GameController> _logger;

    public GameController(
        IMediator mediator,
        IGameStateService gameStateService,
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<GameController> logger)
    {
        _mediator = mediator;
        _gameStateService = gameStateService;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get current active round + multipliers
    /// </summary>
    [HttpGet("current")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCurrentRound()
    {
        var round = await _gameStateService.GetCurrentRound();
        
        if (round == null)
        {
            return NotFound(new { error = "No active round" });
        }

        var pool = await _gameStateService.GetBettingPool(round.Id);
        var multipliers = new
        {
            higher = decimal.Parse(_configuration["Game:Multipliers:HigherWin"] ?? "1.95"),
            lower = decimal.Parse(_configuration["Game:Multipliers:LowerWin"] ?? "1.95")
        };

        return Ok(new
        {
            round,
            pool,
            multipliers
        });
    }

    /// <summary>
    /// Get round history
    /// </summary>
    [HttpGet("history")]
    [AllowAnonymous]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 10)
    {
        var rounds = await _context.GameRounds
            .Where(r => r.Phase == GamePhase.Settled)
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .Select(r => new
            {
                r.Id,
                r.Asset,
                r.OpenPrice,
                r.ClosePrice,
                r.OpenPriceDigitSum,
                r.ClosePriceDigitSum,
                r.Result,
                r.StartTime,
                r.EndTime,
                TotalBets = r.Bets.Count,
                TotalWagered = r.Bets.Sum(b => b.Amount),
                Winners = r.Bets.Count(b => b.IsWin)
            })
            .ToListAsync();

        return Ok(new { rounds });
    }

    /// <summary>
    /// Place a bet
    /// </summary>
    [HttpPost("bet")]
    public async Task<IActionResult> PlaceBet([FromBody] PlaceBetRequest request)
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var prediction = request.Prediction.ToLowerInvariant() switch
        {
            "higher" => BetOutcome.Higher,
            "lower" => BetOutcome.Lower,
            _ => throw new ArgumentException("Invalid prediction")
        };

        var command = new PlaceBetCommand(userId.Value, request.Amount, prediction);
        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new
        {
            betId = result.BetId,
            amount = request.Amount,
            prediction = request.Prediction,
            potentialPayout = result.PotentialPayout,
            potentialProfit = result.PotentialPayout - request.Amount,
            message = "Bet placed successfully"
        });
    }

    /// <summary>
    /// Get user's active bets
    /// </summary>
    [HttpGet("my-bets")]
    public async Task<IActionResult> GetMyBets()
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var round = await _gameStateService.GetCurrentRound();
        
        if (round == null)
        {
            return Ok(new { bets = new List<object>() });
        }

        var bets = await _context.Bets
            .Where(b => b.UserId == userId.Value && b.GameRoundId == round.Id)
            .Select(b => new
            {
                b.Id,
                b.Amount,
                Prediction = b.Prediction.ToString(),
                b.Multiplier,
                PotentialPayout = b.Amount * b.Multiplier,
                b.PlacedAt
            })
            .ToListAsync();

        return Ok(new { bets });
    }

    /// <summary>
    /// Get user's betting statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var allBets = await _context.Bets
            .Where(b => b.UserId == userId.Value && b.SettledAt.HasValue)
            .ToListAsync();

        var totalBets = allBets.Count;
        var totalWagered = allBets.Sum(b => b.Amount);
        var totalPayout = allBets.Sum(b => b.Payout ?? 0);
        var totalProfit = totalPayout - totalWagered;
        var wins = allBets.Count(b => b.IsWin);
        var losses = totalBets - wins;
        var winRate = totalBets > 0 ? (decimal)wins / totalBets * 100 : 0;

        return Ok(new
        {
            totalBets,
            totalWagered,
            totalPayout,
            totalProfit,
            wins,
            losses,
            winRate,
            averageBet = totalBets > 0 ? totalWagered / totalBets : 0,
            roi = totalWagered > 0 ? (totalProfit / totalWagered * 100) : 0
        });
    }

    /// <summary>
    /// Get multipliers for current game
    /// </summary>
    [HttpGet("multipliers")]
    [AllowAnonymous]
    public IActionResult GetMultipliers()
    {
        var multipliers = new
        {
            higher = decimal.Parse(_configuration["Game:Multipliers:HigherWin"] ?? "1.95"),
            lower = decimal.Parse(_configuration["Game:Multipliers:LowerWin"] ?? "1.95"),
            houseEdge = decimal.Parse(_configuration["Game:HouseEdgePercentage"] ?? "2.0")
        };

        return Ok(multipliers);
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userIdClaim))
        {
            return null;
        }

        return Guid.Parse(userIdClaim);
    }
}

public record PlaceBetRequest(
    decimal Amount,
    string Prediction // "Higher" or "Lower"
);
