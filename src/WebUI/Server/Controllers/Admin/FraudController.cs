using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Infrastructure.Security;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class FraudController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IFraudDetectionService _fraudService;
    private readonly ILogger<FraudController> _logger;

    public FraudController(
        ApplicationDbContext context,
        IFraudDetectionService fraudService,
        ILogger<FraudController> logger)
    {
        _context = context;
        _fraudService = fraudService;
        _logger = logger;
    }

    /// <summary>
    /// Get flagged users and devices
    /// </summary>
    [HttpGet("flagged")]
    public async Task<IActionResult> GetFlaggedDevices([FromQuery] int limit = 50)
    {
        var flaggedDevices = await _context.Set<UserDevice>()
            .Where(d => d.IsFlagged)
            .Include(d => d.User)
            .OrderByDescending(d => d.LastSeenAt)
            .Take(limit)
            .Select(d => new
            {
                id = d.Id,
                userId = d.UserId,
                username = d.User.Username ?? d.User.Email ?? d.User.WalletAddress!.Substring(0, 8) + "...",
                ipAddress = d.IpAddress,
                deviceFingerprint = d.DeviceFingerprint,
                flagReason = d.FlagReason,
                accessCount = d.AccessCount,
                lastSeen = d.LastSeenAt
            })
            .ToListAsync();

        return Ok(new { flaggedDevices });
    }

    /// <summary>
    /// Get suspicious patterns (multiple accounts same IP/device)
    /// </summary>
    [HttpGet("suspicious")]
    public async Task<IActionResult> GetSuspiciousPatterns()
    {
        // Group by IP - show IPs with 3+ accounts
        var suspiciousIps = await _context.Set<UserDevice>()
            .GroupBy(d => d.IpAddress)
            .Where(g => g.Select(d => d.UserId).Distinct().Count() >= 3)
            .Select(g => new
            {
                ipAddress = g.Key,
                accountCount = g.Select(d => d.UserId).Distinct().Count(),
                users = g.Select(d => new
                {
                    userId = d.UserId,
                    username = d.User.Username ?? d.User.Email ?? d.User.WalletAddress!.Substring(0, 8) + "...",
                    totalWagered = d.User.TotalWagered,
                    totalDeposited = d.User.TotalDeposited,
                    createdAt = d.User.CreatedAt
                }).Distinct().Take(10)
            })
            .ToListAsync();

        // Group by device fingerprint - show devices with 2+ accounts
        var suspiciousDevices = await _context.Set<UserDevice>()
            .Where(d => d.DeviceFingerprint != null)
            .GroupBy(d => d.DeviceFingerprint)
            .Where(g => g.Select(d => d.UserId).Distinct().Count() >= 2)
            .Select(g => new
            {
                deviceFingerprint = g.Key,
                accountCount = g.Select(d => d.UserId).Distinct().Count(),
                users = g.Select(d => new
                {
                    userId = d.UserId,
                    username = d.User.Username ?? d.User.Email ?? d.User.WalletAddress!.Substring(0, 8) + "...",
                    totalWagered = d.User.TotalWagered,
                    createdAt = d.User.CreatedAt
                }).Distinct().Take(10)
            })
            .ToListAsync();

        return Ok(new
        {
            suspiciousIps = suspiciousIps.Take(20),
            suspiciousDevices = suspiciousDevices.Take(20)
        });
    }

    /// <summary>
    /// Ban an IP address
    /// </summary>
    [HttpPost("ban/ip")]
    public async Task<IActionResult> BanIp([FromBody] BanRequest request)
    {
        var adminId = GetAdminId();
        await _fraudService.BanIp(request.IpAddress!, request.Reason);
        
        _logger.LogWarning(
            "Admin {AdminId} banned IP {IP}: {Reason}",
            adminId,
            request.IpAddress,
            request.Reason
        );

        return Ok(new { success = true });
    }

    /// <summary>
    /// Ban a device fingerprint
    /// </summary>
    [HttpPost("ban/device")]
    public async Task<IActionResult> BanDevice([FromBody] BanRequest request)
    {
        var adminId = GetAdminId();
        await _fraudService.BanDevice(request.DeviceFingerprint!, request.Reason);
        
        _logger.LogWarning(
            "Admin {AdminId} banned device {Device}: {Reason}",
            adminId,
            request.DeviceFingerprint,
            request.Reason
        );

        return Ok(new { success = true });
    }

    /// <summary>
    /// Get banned IPs and devices
    /// </summary>
    [HttpGet("bans")]
    public async Task<IActionResult> GetBans()
    {
        var bans = await _context.Set<BannedDevice>()
            .Where(b => b.IsActive)
            .OrderByDescending(b => b.BannedAt)
            .Select(b => new
            {
                id = b.Id,
                ipAddress = b.IpAddress,
                deviceFingerprint = b.DeviceFingerprint,
                reason = b.Reason,
                bannedAt = b.BannedAt,
                expiresAt = b.ExpiresAt
            })
            .ToListAsync();

        return Ok(new { bans });
    }

    /// <summary>
    /// Revoke a ban
    /// </summary>
    [HttpPost("unban/{banId}")]
    public async Task<IActionResult> Unban(Guid banId)
    {
        var ban = await _context.Set<BannedDevice>().FindAsync(banId);
        if (ban == null) return NotFound();

        ban.Revoke();
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    private Guid GetAdminId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : Guid.Empty;
    }
}

public record BanRequest(
    string? IpAddress,
    string? DeviceFingerprint,
    string Reason
);
