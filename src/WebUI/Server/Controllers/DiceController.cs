using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/game/dice")]
[Authorize]
public class DiceController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DiceController> _logger;

    public DiceController(
        ApplicationDbContext context,
        ILogger<DiceController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Roll dice
    /// </summary>
    [HttpPost("roll")]
    public async Task<IActionResult> Roll([FromBody] RollDiceRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        // Validation
        if (request.Amount <= 0 || request.Amount > user.AvailableBalance)
        {
            return BadRequest(new { error = "Insufficient balance" });
        }

        if (request.TargetNumber < 1 || request.TargetNumber > 99)
        {
            return BadRequest(new { error = "Target must be between 1 and 99" });
        }

        // Generate provably fair result
        var serverSeed = ProvablyFairRng.GenerateSeed();
        var serverSeedHash = ProvablyFairRng.HashSeed(serverSeed);
        
        // Roll result: 0.00 - 100.00
        var hashBytes = Convert.FromBase64String(serverSeedHash);
        var number = BitConverter.ToUInt64(hashBytes, 0);
        var result = (number % 10000) / 100m; // 0.00 - 99.99

        // Calculate win
        var isWin = request.RollUnder 
            ? result < request.TargetNumber 
            : result > request.TargetNumber;

        // Calculate multiplier
        var winChance = request.RollUnder 
            ? request.TargetNumber 
            : (100m - request.TargetNumber);
        
        var houseEdge = 0.99m; // 1% house edge
        var multiplier = (100m / winChance) * houseEdge;
        
        var payout = isWin ? request.Amount * multiplier : 0m;

        // Deduct bet amount
        user.DeductBalance(request.Amount);

        // Credit winnings if won
        if (isWin && payout > 0)
        {
            user.CreditWinnings(payout);
        }

        // Record bet in database
        var diceRoll = new DiceRoll
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Amount = request.Amount,
            TargetNumber = request.TargetNumber,
            RollUnder = request.RollUnder,
            Result = result,
            Multiplier = multiplier,
            Payout = payout,
            IsWin = isWin,
            ServerSeed = serverSeed,
            ServerSeedHash = serverSeedHash,
            CreatedAt = DateTime.UtcNow
        };

        _context.DiceRolls.Add(diceRoll);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Dice roll: User {UserId} bet ${Amount}, target {Target} ({Direction}), result {Result} → {Outcome}",
            userId,
            request.Amount,
            request.TargetNumber,
            request.RollUnder ? "under" : "over",
            result,
            isWin ? "WIN" : "LOSS"
        );

        return Ok(new
        {
            id = diceRoll.Id,
            result,
            amount = request.Amount,
            multiplier,
            payout,
            isWin,
            serverSeed, // Revealed immediately after roll
            serverSeedHash,
            newBalance = user.AvailableBalance
        });
    }

    /// <summary>
    /// Get recent rolls for current user
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentRolls([FromQuery] int limit = 20)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var rolls = await _context.DiceRolls
            .Where(r => r.UserId == userId.Value)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new
            {
                r.Id,
                r.Result,
                r.Amount,
                r.Multiplier,
                r.Payout,
                r.IsWin,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(new { rolls });
    }

    /// <summary>
    /// Verify a past dice roll
    /// </summary>
    [HttpGet("verify/{rollId}")]
    public async Task<IActionResult> VerifyRoll(Guid rollId)
    {
        var roll = await _context.DiceRolls.FindAsync(rollId);
        
        if (roll == null)
        {
            return NotFound(new { error = "Roll not found" });
        }

        // Verify hash
        var calculatedHash = ProvablyFairRng.HashSeed(roll.ServerSeed);
        var hashMatches = calculatedHash == roll.ServerSeedHash;

        // Recalculate result
        var hashBytes = Convert.FromBase64String(roll.ServerSeedHash);
        var number = BitConverter.ToUInt64(hashBytes, 0);
        var calculatedResult = (number % 10000) / 100m;
        var resultMatches = Math.Abs(calculatedResult - roll.Result) < 0.01m;

        return Ok(new
        {
            rollId = roll.Id,
            serverSeed = roll.ServerSeed,
            serverSeedHash = roll.ServerSeedHash,
            result = roll.Result,
            verification = new
            {
                hashMatches,
                resultMatches,
                isFair = hashMatches && resultMatches
            }
        });
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}

public record RollDiceRequest(
    decimal Amount,
    decimal TargetNumber,
    bool RollUnder
);

// Add to Domain/Entities/DiceRoll.cs
public class DiceRoll
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public decimal Amount { get; set; }
    public decimal TargetNumber { get; set; }
    public bool RollUnder { get; set; }
    
    public decimal Result { get; set; } // 0.00 - 100.00
    public decimal Multiplier { get; set; }
    public decimal Payout { get; set; }
    public bool IsWin { get; set; }
    
    public string ServerSeed { get; set; } = string.Empty;
    public string ServerSeedHash { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; }
}
