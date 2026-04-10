using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReferralController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReferralController> _logger;

    public ReferralController(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<ReferralController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get user's referral info
    /// </summary>
    [HttpGet("info")]
    public async Task<IActionResult> GetReferralInfo()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var user = await _context.Users
            .Include(u => u.Referrals)
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user == null) return NotFound();

        // Get direct referrals (Level 1)
        var directReferrals = await _context.Users
            .Where(u => u.ReferredByUserId == userId.Value)
            .Select(u => new
            {
                id = u.Id,
                username = u.Username ?? u.Email ?? u.WalletAddress!.Substring(0, 8) + "...",
                joinedAt = u.CreatedAt,
                totalWagered = u.TotalWagered,
                isActive = u.IsActive
            })
            .ToListAsync();

        // Get indirect referrals (Level 2)
        var indirectReferralIds = directReferrals.Select(r => r.id).ToList();
        var indirectReferrals = await _context.Users
            .Where(u => indirectReferralIds.Contains(u.ReferredByUserId!.Value))
            .Select(u => new
            {
                id = u.Id,
                username = u.Username ?? u.Email ?? u.WalletAddress!.Substring(0, 8) + "...",
                joinedAt = u.CreatedAt,
                referredBy = u.ReferredByUserId,
                totalWagered = u.TotalWagered
            })
            .ToListAsync();

        // Get earnings breakdown
        var earnings = await _context.Set<ReferralEarning>()
            .Where(e => e.ReferrerId == userId.Value)
            .GroupBy(e => e.Level)
            .Select(g => new
            {
                level = g.Key,
                total = g.Sum(e => e.Amount),
                count = g.Count()
            })
            .ToListAsync();

        // Commission rates from config
        var level1Rate = decimal.Parse(_configuration["Referral:Level1Commission"] ?? "5"); // 5%
        var level2Rate = decimal.Parse(_configuration["Referral:Level2Commission"] ?? "2"); // 2%

        return Ok(new
        {
            referralCode = user.ReferralCode,
            totalEarnings = user.TotalReferralEarnings,
            totalReferrals = user.TotalReferrals,
            directReferrals = directReferrals.Count,
            indirectReferrals = indirectReferrals.Count,
            commissionRates = new
            {
                level1 = level1Rate,
                level2 = level2Rate
            },
            earningsBreakdown = earnings,
            referrals = new
            {
                direct = directReferrals,
                indirect = indirectReferrals
            }
        });
    }

    /// <summary>
    /// Get referral tree (hierarchical)
    /// </summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetReferralTree()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var directReferrals = await _context.Users
            .Where(u => u.ReferredByUserId == userId.Value)
            .Select(u => new
            {
                id = u.Id,
                username = u.Username ?? u.Email ?? u.WalletAddress!.Substring(0, 8) + "...",
                referralCode = u.ReferralCode,
                joinedAt = u.CreatedAt,
                totalWagered = u.TotalWagered,
                totalEarnings = u.TotalReferralEarnings,
                isActive = u.IsActive
            })
            .ToListAsync();

        // For each direct referral, get their referrals (level 2)
        var tree = new List<object>();
        foreach (var direct in directReferrals)
        {
            var subReferrals = await _context.Users
                .Where(u => u.ReferredByUserId == direct.id)
                .Select(u => new
                {
                    id = u.Id,
                    username = u.Username ?? u.Email ?? u.WalletAddress!.Substring(0, 8) + "...",
                    joinedAt = u.CreatedAt,
                    totalWagered = u.TotalWagered
                })
                .ToListAsync();

            tree.Add(new
            {
                direct.id,
                direct.username,
                direct.referralCode,
                direct.joinedAt,
                direct.totalWagered,
                direct.totalEarnings,
                direct.isActive,
                subReferrals = subReferrals.Count,
                children = subReferrals
            });
        }

        return Ok(new { tree });
    }

    /// <summary>
    /// Get recent referral earnings
    /// </summary>
    [HttpGet("earnings")]
    public async Task<IActionResult> GetReferralEarnings([FromQuery] int limit = 50)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var earnings = await _context.Set<ReferralEarning>()
            .Include(e => e.ReferredUser)
            .Where(e => e.ReferrerId == userId.Value)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new
            {
                id = e.Id,
                amount = e.Amount,
                level = e.Level,
                source = e.Source,
                referredUser = e.ReferredUser.Username ?? e.ReferredUser.Email ?? e.ReferredUser.WalletAddress!.Substring(0, 8) + "...",
                createdAt = e.CreatedAt
            })
            .ToListAsync();

        return Ok(new { earnings });
    }

    /// <summary>
    /// Validate referral code
    /// </summary>
    [HttpGet("validate/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateReferralCode(string code)
    {
        var referrer = await _context.Users
            .Where(u => u.ReferralCode == code.ToUpper())
            .Select(u => new
            {
                id = u.Id,
                username = u.Username ?? "User",
                isValid = true
            })
            .FirstOrDefaultAsync();

        if (referrer == null)
        {
            return Ok(new { isValid = false });
        }

        return Ok(referrer);
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : null;
    }
}
