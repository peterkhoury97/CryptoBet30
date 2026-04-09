using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FairnessController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FairnessController> _logger;

    public FairnessController(
        ApplicationDbContext context,
        ILogger<FairnessController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Verify a past round's fairness
    /// </summary>
    [HttpGet("verify/{roundId}")]
    public async Task<IActionResult> VerifyRound(Guid roundId)
    {
        var round = await _context.GameRounds.FindAsync(roundId);
        
        if (round == null)
        {
            return NotFound(new { error = "Round not found" });
        }

        if (round.Phase != GamePhase.Settled)
        {
            return BadRequest(new { error = "Round not settled yet. Server seed still hidden." });
        }

        // Verify hash matches seed
        var calculatedHash = ProvablyFairRng.HashSeed(round.ServerSeed);
        var hashMatches = calculatedHash == round.ServerSeedHash;

        // Recalculate outcome from seed
        var combinedSeed = ProvablyFairRng.CombineSeeds(
            round.ServerSeed,
            round.ClientSeed ?? "default",
            round.Nonce
        );
        
        var calculatedOutcome = ProvablyFairRng.CalculateOutcome(combinedSeed);
        var expectedResult = calculatedOutcome ? BetOutcome.Higher : BetOutcome.Lower;
        var outcomeMatches = round.Result == expectedResult;

        return Ok(new
        {
            roundId = round.Id,
            serverSeed = round.ServerSeed,
            serverSeedHash = round.ServerSeedHash,
            clientSeed = round.ClientSeed,
            nonce = round.Nonce,
            result = round.Result.ToString(),
            verification = new
            {
                hashMatches,
                outcomeMatches,
                isFair = hashMatches && outcomeMatches
            },
            howToVerify = new
            {
                step1 = "Hash the server seed with SHA-256",
                step2 = $"Verify it matches: {round.ServerSeedHash}",
                step3 = "Combine server seed + client seed + nonce",
                step4 = "Calculate outcome (even = Higher, odd = Lower)",
                step5 = $"Verify it matches result: {round.Result}"
            }
        });
    }

    /// <summary>
    /// Get current round hash (before settlement)
    /// Players can see hash but not seed until round ends
    /// </summary>
    [HttpGet("current-hash")]
    public async Task<IActionResult> GetCurrentHash()
    {
        var currentRound = await _context.GameRounds
            .Where(r => r.Phase != GamePhase.Settled)
            .OrderByDescending(r => r.StartTime)
            .FirstOrDefaultAsync();

        if (currentRound == null)
        {
            return NotFound(new { error = "No active round" });
        }

        return Ok(new
        {
            roundId = currentRound.Id,
            serverSeedHash = currentRound.ServerSeedHash, // Public hash (seed hidden)
            message = "Server seed will be revealed after round settlement"
        });
    }

    /// <summary>
    /// Get fairness stats
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetFairnessStats()
    {
        var settledRounds = await _context.GameRounds
            .Where(r => r.Phase == GamePhase.Settled)
            .OrderByDescending(r => r.StartTime)
            .Take(1000)
            .ToListAsync();

        if (settledRounds.Count == 0)
        {
            return Ok(new { message = "No settled rounds yet" });
        }

        var higherCount = settledRounds.Count(r => r.Result == BetOutcome.Higher);
        var lowerCount = settledRounds.Count(r => r.Result == BetOutcome.Lower);
        var higherPercent = (decimal)higherCount / settledRounds.Count * 100;
        var lowerPercent = (decimal)lowerCount / settledRounds.Count * 100;

        // Chi-square test for randomness (expected: 50/50)
        var expected = settledRounds.Count / 2.0;
        var chiSquare = Math.Pow(higherCount - expected, 2) / expected +
                        Math.Pow(lowerCount - expected, 2) / expected;
        
        // Critical value at 95% confidence = 3.841
        var isRandom = chiSquare < 3.841;

        return Ok(new
        {
            totalRounds = settledRounds.Count,
            distribution = new
            {
                higher = new { count = higherCount, percent = higherPercent.ToString("F2") + "%" },
                lower = new { count = lowerCount, percent = lowerPercent.ToString("F2") + "%" }
            },
            randomnessTest = new
            {
                chiSquare = chiSquare.ToString("F2"),
                isRandom,
                confidence = "95%",
                explanation = isRandom 
                    ? "Distribution is statistically random (p < 0.05)"
                    : "Distribution shows slight bias (may need more samples)"
            },
            recentRounds = settledRounds.Take(10).Select(r => new
            {
                r.Id,
                r.Result,
                r.StartTime,
                serverSeedHash = r.ServerSeedHash.Substring(0, 16) + "..."
            })
        });
    }

    /// <summary>
    /// Set client seed for current round (user-provided randomness)
    /// </summary>
    [HttpPost("set-client-seed")]
    public async Task<IActionResult> SetClientSeed([FromBody] SetClientSeedRequest request)
    {
        var currentRound = await _context.GameRounds
            .Where(r => r.Phase == GamePhase.Betting)
            .OrderByDescending(r => r.StartTime)
            .FirstOrDefaultAsync();

        if (currentRound == null)
        {
            return BadRequest(new { error = "No active betting round" });
        }

        if (string.IsNullOrWhiteSpace(request.ClientSeed))
        {
            return BadRequest(new { error = "Client seed cannot be empty" });
        }

        currentRound.SetClientSeed(request.ClientSeed);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Client seed set successfully",
            roundId = currentRound.Id,
            clientSeed = request.ClientSeed
        });
    }
}

public record SetClientSeedRequest(string ClientSeed);
