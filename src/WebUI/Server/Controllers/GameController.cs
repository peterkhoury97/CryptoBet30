using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IGameStateService _gameStateService;
    private readonly ILogger<GameController> _logger;

    public GameController(
        IMediator mediator,
        IGameStateService gameStateService,
        ILogger<GameController> logger)
    {
        _mediator = mediator;
        _gameStateService = gameStateService;
        _logger = logger;
    }

    /// <summary>
    /// Get current active round
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

        return Ok(new
        {
            round,
            pool
        });
    }

    /// <summary>
    /// Get round history
    /// </summary>
    [HttpGet("history")]
    [AllowAnonymous]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 10)
    {
        // TODO: Implement history query
        return Ok(new { rounds = new List<object>() });
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

        // TODO: Implement PlaceBetCommand
        // var command = new PlaceBetCommand(userId.Value, request.Amount, request.Prediction);
        // var result = await _mediator.Send(command);

        return Ok(new
        {
            betId = Guid.NewGuid(),
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

        var bets = await _gameStateService.GetUserBets(userId.Value, round.Id);

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

        // TODO: Implement stats query
        return Ok(new
        {
            totalBets = 0,
            totalWagered = 0m,
            totalWon = 0m,
            winRate = 0.0
        });
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
