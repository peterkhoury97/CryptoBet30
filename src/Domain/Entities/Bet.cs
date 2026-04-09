using System.ComponentModel.DataAnnotations;

namespace CryptoBet30.Domain.Entities;

public class Bet
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    
    public Guid GameRoundId { get; private set; }
    public GameRound GameRound { get; private set; } = null!;
    
    public decimal Amount { get; private set; }
    public BetOutcome Prediction { get; private set; }
    public decimal Multiplier { get; private set; } // Fixed multiplier (e.g., 1.95)
    
    public decimal? Payout { get; private set; }
    public bool IsWin { get; private set; }
    public decimal HouseEdgePercentage { get; private set; }
    
    public DateTime PlacedAt { get; private set; }
    public DateTime? SettledAt { get; private set; }
    
    private Bet() { } // EF Core

    public static Bet Create(
        Guid userId,
        Guid gameRoundId,
        decimal amount,
        BetOutcome prediction,
        decimal multiplier,
        decimal houseEdgePercentage = 2.0m)
    {
        if (amount <= 0)
            throw new ArgumentException("Bet amount must be positive", nameof(amount));
        
        if (multiplier <= 0)
            throw new ArgumentException("Multiplier must be positive", nameof(multiplier));

        return new Bet
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameRoundId = gameRoundId,
            Amount = amount,
            Prediction = prediction,
            Multiplier = multiplier,
            HouseEdgePercentage = houseEdgePercentage,
            PlacedAt = DateTime.UtcNow
        };
    }

    public void Settle(BetOutcome actualResult)
    {
        if (SettledAt.HasValue)
            throw new InvalidOperationException("Bet already settled");

        IsWin = Prediction == actualResult;
        
        if (IsWin)
        {
            // Fixed multiplier payout
            // Example: Bet 10 USDT at x1.95 → Win 19.5 USDT total (9.5 profit)
            Payout = Amount * Multiplier;
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

public enum BetOutcome
{
    Higher,
    Lower
}
