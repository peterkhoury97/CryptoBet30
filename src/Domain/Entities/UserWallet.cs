namespace CryptoBet30.Domain.Entities;

/// <summary>
/// User's unique deposit wallet per network
/// </summary>
public class UserWallet
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    
    public string Network { get; private set; } // POLYGON, TRON, BINANCE
    public string Address { get; private set; }
    public string EncryptedPrivateKey { get; private set; } // Encrypted for sweeping
    public int DerivationIndex { get; private set; } // HD wallet index
    
    public decimal TotalDeposited { get; private set; }
    public decimal LastDepositAmount { get; private set; }
    public DateTime? LastDepositAt { get; private set; }
    public string? LastDepositTxHash { get; private set; }
    
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }

    private UserWallet() { } // EF Core

    public static UserWallet Create(
        Guid userId,
        string network,
        string address,
        string encryptedPrivateKey,
        int derivationIndex)
    {
        return new UserWallet
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Network = network.ToUpper(),
            Address = address,
            EncryptedPrivateKey = encryptedPrivateKey,
            DerivationIndex = derivationIndex,
            TotalDeposited = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void RecordDeposit(decimal amount, string txHash)
    {
        TotalDeposited += amount;
        LastDepositAmount = amount;
        LastDepositAt = DateTime.UtcNow;
        LastDepositTxHash = txHash;
    }

    public void UpdateLastChecked()
    {
        LastCheckedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Deposit transaction detected on blockchain (pending credit)
/// </summary>
public class PendingDeposit
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    
    public string Network { get; private set; }
    public string FromAddress { get; private set; }
    public string ToAddress { get; private set; }
    public string TxHash { get; private set; }
    
    public decimal Amount { get; private set; }
    public int Confirmations { get; private set; }
    public int RequiredConfirmations { get; private set; }
    
    public bool IsConfirmed { get; private set; }
    public bool IsCredited { get; private set; }
    public bool IsSwept { get; private set; }
    public string? SweepTxHash { get; private set; }
    
    public DateTime DetectedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? CreditedAt { get; private set; }
    public DateTime? SweptAt { get; private set; }

    private PendingDeposit() { } // EF Core

    public static PendingDeposit Create(
        Guid userId,
        string network,
        string fromAddress,
        string toAddress,
        string txHash,
        decimal amount,
        int requiredConfirmations)
    {
        return new PendingDeposit
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Network = network.ToUpper(),
            FromAddress = fromAddress,
            ToAddress = toAddress,
            TxHash = txHash,
            Amount = amount,
            Confirmations = 0,
            RequiredConfirmations = requiredConfirmations,
            IsConfirmed = false,
            IsCredited = false,
            IsSwept = false,
            DetectedAt = DateTime.UtcNow
        };
    }

    public void UpdateConfirmations(int confirmations)
    {
        Confirmations = confirmations;
        
        if (confirmations >= RequiredConfirmations && !IsConfirmed)
        {
            IsConfirmed = true;
            ConfirmedAt = DateTime.UtcNow;
        }
    }

    public void MarkAsCredited()
    {
        IsCredited = true;
        CreditedAt = DateTime.UtcNow;
    }

    public void MarkAsSwept(string sweepTxHash)
    {
        IsSwept = true;
        SweepTxHash = sweepTxHash;
        SweptAt = DateTime.UtcNow;
    }
}
