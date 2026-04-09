namespace CryptoBet30.Domain.Entities;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? Fee { get; private set; } // Platform + network fees
    
    public string? Network { get; private set; } // "POLYGON", "TRON", "BINANCE"
    public string? BlockchainTxHash { get; private set; }
    public string? DestinationAddress { get; private set; }
    
    public TransactionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    
    public string? FailureReason { get; private set; }

    private Transaction() { } // EF Core

    public static Transaction CreateDeposit(Guid userId, decimal amount, string txHash, string network = "POLYGON")
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = TransactionType.Deposit,
            Amount = amount,
            Fee = 0, // No fee on deposits
            Network = network,
            BlockchainTxHash = txHash,
            Status = TransactionStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
    }

    public static Transaction CreateWithdrawal(Guid userId, decimal amount, decimal fee, string destinationAddress, string network = "POLYGON")
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = TransactionType.Withdrawal,
            Amount = amount,
            Fee = fee,
            Network = network,
            DestinationAddress = destinationAddress,
            Status = TransactionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkAsProcessing(string txHash)
    {
        if (Status != TransactionStatus.Pending)
            throw new InvalidOperationException("Can only process pending transactions");
            
        Status = TransactionStatus.Processing;
        BlockchainTxHash = txHash;
    }

    public void MarkAsCompleted()
    {
        if (Status != TransactionStatus.Processing)
            throw new InvalidOperationException("Transaction must be processing");
            
        Status = TransactionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string reason)
    {
        Status = TransactionStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;
    }
}

public enum TransactionType
{
    Deposit,
    Withdrawal,
    Referral,
    Bonus
}

public enum TransactionStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
