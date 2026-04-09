namespace CryptoBet30.Domain.Entities;

public class DiceRoll
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    
    public decimal Amount { get; private set; }
    public decimal TargetNumber { get; private set; }
    public bool RollUnder { get; private set; }
    
    public decimal Result { get; private set; } // 0.00 - 100.00
    public decimal Multiplier { get; private set; }
    public decimal Payout { get; private set; }
    public bool IsWin { get; private set; }
    
    public string ServerSeed { get; private set; } = string.Empty;
    public string ServerSeedHash { get; private set; } = string.Empty;
    
    public DateTime CreatedAt { get; private set; }
    
    // Navigation
    public User User { get; private set; } = null!;

    private DiceRoll() { } // EF Core

    public static DiceRoll Create(
        Guid userId,
        decimal amount,
        decimal targetNumber,
        bool rollUnder,
        decimal result,
        decimal multiplier,
        bool isWin,
        string serverSeed,
        string serverSeedHash)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        if (targetNumber < 1 || targetNumber > 99)
            throw new ArgumentException("Target must be between 1 and 99", nameof(targetNumber));
        
        if (result < 0 || result > 100)
            throw new ArgumentException("Result must be between 0 and 100", nameof(result));

        var payout = isWin ? amount * multiplier : 0m;

        return new DiceRoll
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
            TargetNumber = targetNumber,
            RollUnder = rollUnder,
            Result = result,
            Multiplier = multiplier,
            Payout = payout,
            IsWin = isWin,
            ServerSeed = serverSeed,
            ServerSeedHash = serverSeedHash,
            CreatedAt = DateTime.UtcNow
        };
    }

    public decimal GetNetProfit() => Payout - Amount;
}
