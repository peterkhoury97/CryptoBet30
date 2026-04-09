namespace CryptoBet30.Domain.Entities;

public class Bet
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    
    public Guid GameRoundId { get; private set; }
    public GameRound GameRound { get; private set; }
    
    public decimal Amount { get; private set; }
    public BetOutcome Prediction { get; private set; }
    
    public DateTime PlacedAt { get; private set; }
    public DateTime? SettledAt { get; private set; }
    
    public decimal? Payout { get; private set; }
    public bool IsWin { get; private set; }
    
    public decimal HouseEdgePercentage { get; private set; } = 2.0m; // 2% house edge

    private Bet() { } // EF Core

    public static Bet Create(Guid userId, Guid gameRoundId, decimal amount, BetOutcome prediction)
    {
        if (amount <= 0)
            throw new ArgumentException("Bet amount must be positive");
            
        if (amount < 0.01m)
            throw new ArgumentException("Minimum bet is 0.01");
            
        if (amount > 100m)
            throw new ArgumentException("Maximum bet is 100");
            
        return new Bet
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameRoundId = gameRoundId,
            Amount = amount,
            Prediction = prediction,
            PlacedAt = DateTime.UtcNow
        };
    }

    public void Settle(BetOutcome actualOutcome, decimal totalPool, decimal winningPool)
    {
        if (SettledAt.HasValue)
            throw new InvalidOperationException("Bet already settled");
            
        IsWin = Prediction == actualOutcome;
        
        if (IsWin)
        {
            // Calculate payout based on pool ratio
            // Winner gets: (Their bet / Winning pool) * Total pool * (1 - House Edge)
            var poolRatio = Amount / winningPool;
            var grossPayout = poolRatio * totalPool;
            Payout = grossPayout * (1 - HouseEdgePercentage / 100m);
        }
        else
        {
            Payout = 0;
        }
        
        SettledAt = DateTime.UtcNow;
    }

    public decimal GetNetProfit()
    {
        if (!SettledAt.HasValue)
            return 0;
            
        return (Payout ?? 0) - Amount;
    }
}
