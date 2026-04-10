namespace CryptoBet30.Domain.Entities;

/// <summary>
/// Daily check-in tracking for user engagement
/// </summary>
public class DailyCheckIn
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    
    public DateTime CheckInDate { get; private set; }
    public int CurrentStreak { get; private set; }
    public int LongestStreak { get; private set; }
    public int TotalCheckIns { get; private set; }
    
    public decimal RewardAmount { get; private set; }
    public bool BonusClaimed { get; private set; }
    
    public DateTime CreatedAt { get; private set; }

    private DailyCheckIn() { } // EF Core

    public static DailyCheckIn Create(Guid userId, int currentStreak, decimal rewardAmount)
    {
        return new DailyCheckIn
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CheckInDate = DateTime.UtcNow.Date,
            CurrentStreak = currentStreak,
            LongestStreak = currentStreak,
            TotalCheckIns = 1,
            RewardAmount = rewardAmount,
            BonusClaimed = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void ClaimBonus()
    {
        BonusClaimed = true;
    }
}

/// <summary>
/// Referral earnings tracking
/// </summary>
public class ReferralEarning
{
    public Guid Id { get; private set; }
    public Guid ReferrerId { get; private set; }
    public User Referrer { get; private set; }
    
    public Guid ReferredUserId { get; private set; }
    public User ReferredUser { get; private set; }
    
    public int Level { get; private set; } // 1 = direct, 2 = indirect
    public decimal Amount { get; private set; }
    public string Source { get; private set; } // "Bet", "Deposit", "CheckIn"
    
    public DateTime CreatedAt { get; private set; }

    private ReferralEarning() { } // EF Core

    public static ReferralEarning Create(
        Guid referrerId,
        Guid referredUserId,
        int level,
        decimal amount,
        string source)
    {
        return new ReferralEarning
        {
            Id = Guid.NewGuid(),
            ReferrerId = referrerId,
            ReferredUserId = referredUserId,
            Level = level,
            Amount = amount,
            Source = source,
            CreatedAt = DateTime.UtcNow
        };
    }
}
