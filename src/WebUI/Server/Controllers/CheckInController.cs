using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CheckInController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckInController> _logger;

    public CheckInController(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<CheckInController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get check-in status
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetCheckInStatus()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        var canCheckIn = user.CanCheckInToday();
        var nextReward = CalculateReward(user.CurrentCheckInStreak + 1);
        
        var minimumWager = decimal.Parse(_configuration["CheckIn:MinimumWagerRequired"] ?? "500");
        var wagerRequirement = new
        {
            required = minimumWager,
            current = user.TotalWagered,
            remaining = Math.Max(0, minimumWager - user.TotalWagered),
            isMet = user.TotalWagered >= minimumWager
        };

        return Ok(new
        {
            canCheckIn = canCheckIn && wagerRequirement.isMet,
            currentStreak = user.CurrentCheckInStreak,
            longestStreak = user.LongestCheckInStreak,
            totalCheckIns = user.TotalCheckIns,
            lastCheckIn = user.LastCheckInDate,
            nextReward,
            streakBonus = GetStreakBonus(user.CurrentCheckInStreak + 1),
            wagerRequirement
        });
    }

    /// <summary>
    /// Perform daily check-in
    /// </summary>
    [HttpPost("claim")]
    public async Task<IActionResult> ClaimCheckIn()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        if (!user.CanCheckInToday())
        {
            return BadRequest(new { error = "Already checked in today. Come back tomorrow!" });
        }

        // ANTI-FRAUD: Require $500 total wagered to claim check-in
        var minimumWager = decimal.Parse(_configuration["CheckIn:MinimumWagerRequired"] ?? "500");
        if (user.TotalWagered < minimumWager)
        {
            return BadRequest(new
            {
                error = $"You must wager at least ${minimumWager} to claim daily rewards.",
                currentWager = user.TotalWagered,
                remaining = minimumWager - user.TotalWagered
            });
        }

        // Get device info from headers for fraud detection
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var deviceFingerprint = Request.Headers["X-Device-Fingerprint"].ToString();
        var userAgent = Request.Headers["User-Agent"].ToString();

        // Fraud check
        var fraudService = HttpContext.RequestServices.GetRequiredService<IFraudDetectionService>();
        var fraudCheck = await fraudService.CheckCheckIn(userId.Value, ipAddress, deviceFingerprint);

        if (!fraudCheck.IsAllowed)
        {
            _logger.LogWarning(
                "Check-in blocked for user {UserId}: {Reason}",
                userId,
                fraudCheck.Reason
            );
            return BadRequest(new { error = "Check-in not allowed. Please contact support." });
        }

        if (fraudCheck.IsFlagged)
        {
            _logger.LogWarning(
                "Suspicious check-in flagged for user {UserId}: {Reason} (RiskScore: {Score})",
                userId,
                fraudCheck.Reason,
                fraudCheck.RiskScore
            );
        }

        // Record device
        await fraudService.RecordDeviceForUser(userId.Value, ipAddress, deviceFingerprint, userAgent);

        var newStreak = user.LastCheckInDate?.Date == DateTime.UtcNow.Date.AddDays(-1)
            ? user.CurrentCheckInStreak + 1
            : 1;

        var rewardAmount = CalculateReward(newStreak);
        
        // Perform check-in
        user.CheckIn(rewardAmount);

        // Create check-in record
        var checkIn = DailyCheckIn.Create(userId.Value, newStreak, rewardAmount);
        await _context.Set<DailyCheckIn>().AddAsync(checkIn);

        // Award referral commission if user was referred
        if (user.ReferredByUserId.HasValue)
        {
            var referrer = await _context.Users.FindAsync(user.ReferredByUserId.Value);
            if (referrer != null)
            {
                var commission = rewardAmount * 0.10m; // 10% of check-in reward
                referrer.AddReferralEarnings(commission);

                var earning = ReferralEarning.Create(
                    referrer.Id,
                    user.Id,
                    1,
                    commission,
                    "CheckIn"
                );
                await _context.Set<ReferralEarning>().AddAsync(earning);
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} checked in. Streak: {Streak}, Reward: {Reward}, IP: {IP}",
            userId,
            newStreak,
            rewardAmount,
            ipAddress
        );

        return Ok(new
        {
            success = true,
            reward = rewardAmount,
            newStreak,
            longestStreak = user.LongestCheckInStreak,
            totalCheckIns = user.TotalCheckIns,
            message = GetStreakMessage(newStreak)
        });
    }

    /// <summary>
    /// Get check-in history
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetCheckInHistory([FromQuery] int limit = 30)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var history = await _context.Set<DailyCheckIn>()
            .Where(c => c.UserId == userId.Value)
            .OrderByDescending(c => c.CheckInDate)
            .Take(limit)
            .Select(c => new
            {
                id = c.Id,
                date = c.CheckInDate,
                streak = c.CurrentStreak,
                reward = c.RewardAmount,
                createdAt = c.CreatedAt
            })
            .ToListAsync();

        return Ok(new { history });
    }

    /// <summary>
    /// Get check-in leaderboard
    /// </summary>
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetCheckInLeaderboard([FromQuery] int limit = 10)
    {
        var leaderboard = await _context.Users
            .Where(u => u.IsActive)
            .OrderByDescending(u => u.CurrentCheckInStreak)
            .ThenByDescending(u => u.LongestCheckInStreak)
            .Take(limit)
            .Select(u => new
            {
                username = u.Username ?? u.Email ?? u.WalletAddress!.Substring(0, 8) + "...",
                currentStreak = u.CurrentCheckInStreak,
                longestStreak = u.LongestCheckInStreak,
                totalCheckIns = u.TotalCheckIns
            })
            .ToListAsync();

        return Ok(new { leaderboard });
    }

    private decimal CalculateReward(int streak)
    {
        // Base reward from config (e.g., 0.10 USDT)
        var baseReward = decimal.Parse(_configuration["CheckIn:BaseReward"] ?? "0.10");
        var streakBonus = GetStreakBonus(streak);
        
        return baseReward + streakBonus;
    }

    private decimal GetStreakBonus(int streak)
    {
        // Milestone bonuses
        return streak switch
        {
            >= 365 => 10.0m,   // 1 year: $10 bonus!
            >= 180 => 5.0m,    // 6 months: $5
            >= 90 => 2.0m,     // 3 months: $2
            >= 30 => 1.0m,     // 1 month: $1
            >= 14 => 0.50m,    // 2 weeks: $0.50
            >= 7 => 0.25m,     // 1 week: $0.25
            >= 3 => 0.10m,     // 3 days: $0.10
            _ => 0m
        };
    }

    private string GetStreakMessage(int streak)
    {
        return streak switch
        {
            >= 365 => "🎉 1 YEAR STREAK! You're a legend! $10 bonus!",
            >= 180 => "🔥 6 months! Incredible dedication! $5 bonus!",
            >= 90 => "💪 90 days straight! Amazing! $2 bonus!",
            >= 30 => "⭐ 30-day streak! Keep it up! $1 bonus!",
            >= 14 => "🚀 2 weeks! You're on fire! $0.50 bonus!",
            >= 7 => "💎 7-day streak! $0.25 bonus!",
            >= 3 => "✨ 3 days in a row! $0.10 bonus!",
            _ => $"Day {streak}! Come back tomorrow to continue your streak!"
        };
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : null;
    }
}
