using System.Security.Cryptography;
using System.Text;

namespace CryptoBet30.Infrastructure.Security;

/// <summary>
/// Device fingerprinting and fraud detection service
/// </summary>
public interface IFraudDetectionService
{
    Task<FraudCheckResult> CheckRegistration(string ipAddress, string? deviceFingerprint, string? userAgent);
    Task<FraudCheckResult> CheckCheckIn(Guid userId, string ipAddress, string? deviceFingerprint);
    Task RecordDeviceForUser(Guid userId, string ipAddress, string? deviceFingerprint, string? userAgent);
    Task<bool> IsDeviceBanned(string deviceFingerprint);
    Task<bool> IsIpBanned(string ipAddress);
    Task BanDevice(string deviceFingerprint, string reason);
    Task BanIp(string ipAddress, string reason);
}

public class FraudDetectionService : IFraudDetectionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FraudDetectionService> _logger;
    private readonly IConfiguration _configuration;

    public FraudDetectionService(
        ApplicationDbContext context,
        ILogger<FraudDetectionService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<FraudCheckResult> CheckRegistration(
        string ipAddress,
        string? deviceFingerprint,
        string? userAgent)
    {
        var issues = new List<string>();
        var riskScore = 0;

        // Check if IP is banned
        if (await IsIpBanned(ipAddress))
        {
            return FraudCheckResult.Blocked("IP address is banned");
        }

        // Check if device is banned
        if (!string.IsNullOrEmpty(deviceFingerprint) && await IsDeviceBanned(deviceFingerprint))
        {
            return FraudCheckResult.Blocked("Device is banned");
        }

        // Check max accounts per IP
        var maxAccountsPerIp = int.Parse(_configuration["Security:Fraud:MaxAccountsPerIP"] ?? "3");
        var accountsFromIp = await _context.Set<UserDevice>()
            .Where(d => d.IpAddress == ipAddress)
            .Select(d => d.UserId)
            .Distinct()
            .CountAsync();

        if (accountsFromIp >= maxAccountsPerIp)
        {
            issues.Add($"Too many accounts from IP ({accountsFromIp}/{maxAccountsPerIp})");
            riskScore += 50;
        }

        // Check max accounts per device fingerprint
        if (!string.IsNullOrEmpty(deviceFingerprint))
        {
            var maxAccountsPerDevice = int.Parse(_configuration["Security:Fraud:MaxAccountsPerDevice"] ?? "2");
            var accountsFromDevice = await _context.Set<UserDevice>()
                .Where(d => d.DeviceFingerprint == deviceFingerprint)
                .Select(d => d.UserId)
                .Distinct()
                .CountAsync();

            if (accountsFromDevice >= maxAccountsPerDevice)
            {
                issues.Add($"Too many accounts from device ({accountsFromDevice}/{maxAccountsPerDevice})");
                riskScore += 50;
            }
        }

        // Check for VPN/Proxy (basic check - can integrate with IPHub/IPQualityScore APIs)
        if (IsLikelyVpn(ipAddress, userAgent))
        {
            issues.Add("Possible VPN/Proxy detected");
            riskScore += 30;
        }

        // Check registration velocity (max N registrations per hour from IP)
        var registrationWindow = DateTime.UtcNow.AddHours(-1);
        var recentRegistrations = await _context.Set<UserDevice>()
            .Where(d => d.IpAddress == ipAddress && d.FirstSeenAt >= registrationWindow)
            .CountAsync();

        var maxRegistrationsPerHour = int.Parse(_configuration["Security:Fraud:MaxRegistrationsPerHour"] ?? "5");
        if (recentRegistrations >= maxRegistrationsPerHour)
        {
            issues.Add($"Too many registrations from IP in last hour ({recentRegistrations})");
            riskScore += 40;
        }

        if (riskScore >= 100)
        {
            return FraudCheckResult.Blocked(string.Join("; ", issues));
        }
        else if (riskScore >= 50)
        {
            return FraudCheckResult.Flagged(string.Join("; ", issues), riskScore);
        }

        return FraudCheckResult.Safe();
    }

    public async Task<FraudCheckResult> CheckCheckIn(
        Guid userId,
        string ipAddress,
        string? deviceFingerprint)
    {
        var issues = new List<string>();

        // Check if IP is banned
        if (await IsIpBanned(ipAddress))
        {
            return FraudCheckResult.Blocked("IP address is banned");
        }

        // Check if device is banned
        if (!string.IsNullOrEmpty(deviceFingerprint) && await IsDeviceBanned(deviceFingerprint))
        {
            return FraudCheckResult.Blocked("Device is banned");
        }

        // Check if this device belongs to the user
        var userDevice = await _context.Set<UserDevice>()
            .FirstOrDefaultAsync(d => 
                d.UserId == userId && 
                (d.IpAddress == ipAddress || d.DeviceFingerprint == deviceFingerprint));

        if (userDevice == null)
        {
            // New device/IP for this user - flag for review
            issues.Add("Check-in from unrecognized device/IP");
            return FraudCheckResult.Flagged(string.Join("; ", issues), 40);
        }

        return FraudCheckResult.Safe();
    }

    public async Task RecordDeviceForUser(
        Guid userId,
        string ipAddress,
        string? deviceFingerprint,
        string? userAgent)
    {
        var existing = await _context.Set<UserDevice>()
            .FirstOrDefaultAsync(d => 
                d.UserId == userId && 
                d.IpAddress == ipAddress && 
                d.DeviceFingerprint == deviceFingerprint);

        if (existing != null)
        {
            existing.UpdateLastSeen();
            existing.IncrementAccessCount();
        }
        else
        {
            var device = UserDevice.Create(userId, ipAddress, deviceFingerprint, userAgent);
            await _context.Set<UserDevice>().AddAsync(device);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsDeviceBanned(string deviceFingerprint)
    {
        return await _context.Set<BannedDevice>()
            .AnyAsync(b => b.DeviceFingerprint == deviceFingerprint && b.IsActive);
    }

    public async Task<bool> IsIpBanned(string ipAddress)
    {
        return await _context.Set<BannedDevice>()
            .AnyAsync(b => b.IpAddress == ipAddress && b.IsActive);
    }

    public async Task BanDevice(string deviceFingerprint, string reason)
    {
        var ban = BannedDevice.CreateDeviceBan(deviceFingerprint, reason);
        await _context.Set<BannedDevice>().AddAsync(ban);
        await _context.SaveChangesAsync();

        _logger.LogWarning("Device banned: {Fingerprint} - Reason: {Reason}", deviceFingerprint, reason);
    }

    public async Task BanIp(string ipAddress, string reason)
    {
        var ban = BannedDevice.CreateIpBan(ipAddress, reason);
        await _context.Set<BannedDevice>().AddAsync(ban);
        await _context.SaveChangesAsync();

        _logger.LogWarning("IP banned: {IP} - Reason: {Reason}", ipAddress, reason);
    }

    private bool IsLikelyVpn(string ipAddress, string? userAgent)
    {
        // Basic heuristics - can be enhanced with paid APIs
        
        // Check for common VPN user agents
        if (!string.IsNullOrEmpty(userAgent))
        {
            var vpnKeywords = new[] { "vpn", "proxy", "tor", "anonymizer" };
            if (vpnKeywords.Any(k => userAgent.ToLower().Contains(k)))
            {
                return true;
            }
        }

        // Check for data center IP ranges (basic check)
        // In production, use IPHub, IPQualityScore, or MaxMind GeoIP2
        var parts = ipAddress.Split('.');
        if (parts.Length == 4)
        {
            // Example: AWS EC2 ranges, DigitalOcean, etc.
            // This is a simplified check - use a proper database
            if (parts[0] == "54" || parts[0] == "52") return true; // AWS
            if (parts[0] == "104" && parts[1] == "131") return true; // DigitalOcean
        }

        return false;
    }
}

public record FraudCheckResult
{
    public bool IsAllowed { get; init; }
    public bool IsFlagged { get; init; }
    public string? Reason { get; init; }
    public int RiskScore { get; init; }

    public static FraudCheckResult Safe() => new() { IsAllowed = true, IsFlagged = false, RiskScore = 0 };
    public static FraudCheckResult Flagged(string reason, int riskScore) => new() 
    { 
        IsAllowed = true, 
        IsFlagged = true, 
        Reason = reason, 
        RiskScore = riskScore 
    };
    public static FraudCheckResult Blocked(string reason) => new() 
    { 
        IsAllowed = false, 
        IsFlagged = true, 
        Reason = reason, 
        RiskScore = 100 
    };
}
