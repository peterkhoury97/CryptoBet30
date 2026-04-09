namespace CryptoBet30.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string WalletAddress { get; private set; }
    public string Email { get; private set; }
    
    // Off-chain balances (fast, no gas fees)
    public decimal AvailableBalance { get; private set; }
    public decimal LockedBetBalance { get; private set; }
    public decimal BonusBalance { get; private set; }
    
    public Guid? ReferredByUserId { get; private set; }
    public User? ReferredBy { get; private set; }
    
    public decimal TotalReferralEarnings { get; private set; }
    public int ActiveReferrals { get; private set; }
    
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    
    private readonly List<Bet> _bets = new();
    public IReadOnlyCollection<Bet> Bets => _bets.AsReadOnly();
    
    private readonly List<Transaction> _transactions = new();
    public IReadOnlyCollection<Transaction> Transactions => _transactions.AsReadOnly();

    private User() { } // EF Core

    public static User Create(string walletAddress, string email, Guid? referredByUserId = null)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            WalletAddress = walletAddress.ToLowerInvariant(),
            Email = email,
            ReferredByUserId = referredByUserId,
            AvailableBalance = 0,
            LockedBetBalance = 0,
            BonusBalance = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Deposit(decimal amount, string txHash)
    {
        if (amount <= 0)
            throw new ArgumentException("Deposit amount must be positive");
            
        AvailableBalance += amount;
        
        var transaction = Transaction.CreateDeposit(Id, amount, txHash);
        _transactions.Add(transaction);
    }

    public void LockBalanceForBet(decimal amount)
    {
        if (amount > AvailableBalance)
            throw new InsufficientBalanceException("Insufficient available balance");
            
        AvailableBalance -= amount;
        LockedBetBalance += amount;
    }

    public void UnlockBalanceAfterBet(decimal amount)
    {
        if (amount > LockedBetBalance)
            throw new InvalidOperationException("Cannot unlock more than locked balance");
            
        LockedBetBalance -= amount;
        AvailableBalance += amount;
    }

    public void CreditWinnings(decimal amount)
    {
        AvailableBalance += amount;
    }

    public void Withdraw(decimal amount, string destinationAddress)
    {
        if (amount > AvailableBalance)
            throw new InsufficientBalanceException("Insufficient balance for withdrawal");
            
        if (amount < 0.001m) // Minimum withdrawal
            throw new ArgumentException("Withdrawal amount too small");
            
        AvailableBalance -= amount;
        
        var transaction = Transaction.CreateWithdrawal(Id, amount, destinationAddress);
        _transactions.Add(transaction);
    }

    public void AddReferralEarnings(decimal amount)
    {
        BonusBalance += amount;
        TotalReferralEarnings += amount;
    }

    public void UpdateLastLogin()
    {
        LastLoginAt = DateTime.UtcNow;
    }
}

public class InsufficientBalanceException : Exception
{
    public InsufficientBalanceException(string message) : base(message) { }
}

public class BettingWindowClosedException : Exception
{
    public BettingWindowClosedException(string message) : base(message) { }
}
