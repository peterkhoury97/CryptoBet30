namespace CryptoBet30.Domain.Entities;

public class Referral
{
    public Guid Id { get; private set; }
    public Guid ReferrerId { get; private set; }
    public User Referrer { get; private set; }
    
    public Guid ReferredUserId { get; private set; }
    public User ReferredUser { get; private set; }
    
    public decimal CommissionRate { get; private set; } = 5.0m; // 5%
    public decimal TotalEarned { get; private set; }
    
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    
    private Referral() { } // EF Core

    public static Referral Create(Guid referrerId, Guid referredUserId, decimal commissionRate = 5.0m)
    {
        return new Referral
        {
            Id = Guid.NewGuid(),
            ReferrerId = referrerId,
            ReferredUserId = referredUserId,
            CommissionRate = commissionRate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddEarnings(decimal amount)
    {
        if (!IsActive)
            throw new InvalidOperationException("Referral is not active");
            
        TotalEarned += amount;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
