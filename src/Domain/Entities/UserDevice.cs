namespace CryptoBet30.Domain.Entities;

/// <summary>
/// Tracks devices and IPs used by each user for fraud detection
/// </summary>
public class UserDevice
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    
    public string IpAddress { get; private set; }
    public string? DeviceFingerprint { get; private set; }
    public string? UserAgent { get; private set; }
    
    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public int AccessCount { get; private set; }
    
    public bool IsFlagged { get; private set; }
    public string? FlagReason { get; private set; }

    private UserDevice() { } // EF Core

    public static UserDevice Create(
        Guid userId,
        string ipAddress,
        string? deviceFingerprint,
        string? userAgent)
    {
        return new UserDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IpAddress = ipAddress,
            DeviceFingerprint = deviceFingerprint,
            UserAgent = userAgent,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            AccessCount = 1,
            IsFlagged = false
        };
    }

    public void UpdateLastSeen()
    {
        LastSeenAt = DateTime.UtcNow;
    }

    public void IncrementAccessCount()
    {
        AccessCount++;
    }

    public void Flag(string reason)
    {
        IsFlagged = true;
        FlagReason = reason;
    }

    public void Unflag()
    {
        IsFlagged = false;
        FlagReason = null;
    }
}

/// <summary>
/// Banned devices and IPs
/// </summary>
public class BannedDevice
{
    public Guid Id { get; private set; }
    public string? IpAddress { get; private set; }
    public string? DeviceFingerprint { get; private set; }
    public string Reason { get; private set; }
    public DateTime BannedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? BannedByUserId { get; private set; }

    private BannedDevice() { } // EF Core

    public static BannedDevice CreateIpBan(
        string ipAddress,
        string reason,
        DateTime? expiresAt = null,
        Guid? bannedByUserId = null)
    {
        return new BannedDevice
        {
            Id = Guid.NewGuid(),
            IpAddress = ipAddress,
            Reason = reason,
            BannedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true,
            BannedByUserId = bannedByUserId
        };
    }

    public static BannedDevice CreateDeviceBan(
        string deviceFingerprint,
        string reason,
        DateTime? expiresAt = null,
        Guid? bannedByUserId = null)
    {
        return new BannedDevice
        {
            Id = Guid.NewGuid(),
            DeviceFingerprint = deviceFingerprint,
            Reason = reason,
            BannedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true,
            BannedByUserId = bannedByUserId
        };
    }

    public void Revoke()
    {
        IsActive = false;
    }
}
