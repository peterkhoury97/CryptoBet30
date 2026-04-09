namespace CryptoBet30.Domain.Entities;

public class GameRound
{
    public Guid Id { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime LockTime { get; private set; }
    public DateTime EndTime { get; private set; }
    
    public string Asset { get; private set; } // "BTC" or "ETH"
    
    // Provably fair seeds
    public string ServerSeed { get; private set; } = string.Empty;
    public string ServerSeedHash { get; private set; } = string.Empty;
    public string? ClientSeed { get; private set; }
    public int Nonce { get; private set; }
    
    // Legacy price tracking (optional, for display only)
    public decimal OpenPrice { get; private set; }
    public decimal ClosePrice { get; private set; }
    
    public int OpenPriceDigitSum { get; private set; }
    public int ClosePriceDigitSum { get; private set; }
    
    public GamePhase Phase { get; private set; }
    public BetOutcome? Result { get; private set; }
    
    public decimal TotalBetsHigher { get; private set; }
    public decimal TotalBetsLower { get; private set; }
    
    private readonly List<Bet> _bets = new();
    public IReadOnlyCollection<Bet> Bets => _bets.AsReadOnly();

    private GameRound() { } // EF Core

    public static GameRound Create(string asset)
    {
        var now = DateTime.UtcNow;
        
        // Generate provably fair seed
        var serverSeed = ProvablyFairRng.GenerateSeed();
        var serverSeedHash = ProvablyFairRng.HashSeed(serverSeed);
        
        return new GameRound
        {
            Id = Guid.NewGuid(),
            Asset = asset,
            StartTime = now,
            LockTime = now.AddSeconds(15),
            EndTime = now.AddSeconds(30),
            Phase = GamePhase.Betting,
            ServerSeed = serverSeed,
            ServerSeedHash = serverSeedHash,
            Nonce = 0
        };
    }

    public void SetOpenPrice(decimal price)
    {
        if (Phase != GamePhase.Betting)
            throw new InvalidOperationException("Can only set open price during betting phase");
            
        OpenPrice = price;
        OpenPriceDigitSum = CalculateDigitSum(price);
    }

    public void LockBetting()
    {
        if (DateTime.UtcNow < LockTime)
            throw new InvalidOperationException("Lock time not reached");
            
        Phase = GamePhase.Locked;
    }

    public void Settle(decimal closePrice)
    {
        if (Phase != GamePhase.Locked)
            throw new InvalidOperationException("Round must be locked before settlement");
        
        // Still track price for display
        ClosePrice = closePrice;
        ClosePriceDigitSum = CalculateDigitSum(closePrice);
        
        // Calculate result using provably fair seed
        var combinedSeed = ProvablyFairRng.CombineSeeds(
            ServerSeed,
            ClientSeed ?? "default",
            Nonce
        );
        
        var isHigher = ProvablyFairRng.CalculateOutcome(combinedSeed);
        Result = isHigher ? BetOutcome.Higher : BetOutcome.Lower;
        
        Phase = GamePhase.Settled;
    }

    public void PlaceBet(Bet bet)
    {
        if (Phase != GamePhase.Betting)
            throw new BettingWindowClosedException("Betting window is closed");
            
        if (DateTime.UtcNow >= LockTime)
            throw new BettingWindowClosedException("Lock time reached");
            
        _bets.Add(bet);
        
        if (bet.Prediction == BetOutcome.Higher)
            TotalBetsHigher += bet.Amount;
        else
            TotalBetsLower += bet.Amount;
    }

    public void SetClientSeed(string clientSeed)
    {
        if (Phase != GamePhase.Betting)
            throw new InvalidOperationException("Can only set client seed during betting");
        
        ClientSeed = clientSeed;
    }

    private static int CalculateDigitSum(decimal price)
    {
        // Remove decimal point and sum all digits
        var priceString = price.ToString("F8").Replace(".", "");
        return priceString.Sum(c => c - '0');
    }
}

public enum GamePhase
{
    Betting,   // 0-15 seconds
    Locked,    // 15-30 seconds
    Settled    // After 30 seconds
}

public enum BetOutcome
{
    Higher,
    Lower
}
