using System.ComponentModel.DataAnnotations;

namespace CryptoBet30.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    
    // Wallet-based authentication
    public string? WalletAddress { get; private set; }
    
    // Email/password authentication
    public string? Email { get; private set; }
    public string? PasswordHash { get; private set; }
    public string? Username { get; private set; }
    
    // Account type
    public AuthenticationType AuthType { get; private set; }
    
    // Admin role
    public bool IsAdmin { get; private set; }
    
    // Balances (off-chain)
    public decimal AvailableBalance { get; private set; }
    public decimal LockedBetBalance { get; private set; }
    public decimal BonusBalance { get; private set; }
    
    // Referral
    public Guid? ReferredByUserId { get; private set; }
    public User? ReferredBy { get; private set; }
    public decimal TotalReferralEarnings { get; private set; }
    
    // Collections
    public ICollection<Bet> Bets { get; private set; } = new List<Bet>();
    public ICollection<Transaction> Transactions { get; private set; } = new List<Transaction>();
    
    // Metadata
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsEmailVerified { get; private set; }
    
    private User() { } // EF Core

    // Factory: MetaMask wallet login
    public static User CreateWithWallet(string walletAddress, Guid? referredBy = null)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address is required", nameof(walletAddress));
        
        return new User
        {
            Id = Guid.NewGuid(),
            WalletAddress = walletAddress.ToLowerInvariant(),
            AuthType = AuthenticationType.Wallet,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ReferredByUserId = referredBy
        };
    }

    // Factory: Email/password registration
    public static User CreateWithEmail(string email, string passwordHash, string username, Guid? referredBy = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required", nameof(username));
        
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            Username = username,
            AuthType = AuthenticationType.EmailPassword,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ReferredByUserId = referredBy
        };
    }

    // Link wallet to existing email account
    public void LinkWallet(string walletAddress)
    {
        if (AuthType != AuthenticationType.EmailPassword)
            throw new InvalidOperationException("Can only link wallet to email accounts");
        
        WalletAddress = walletAddress.ToLowerInvariant();
    }

    public void UpdateLastLogin()
    {
        LastLoginAt = DateTime.UtcNow;
    }

    public void VerifyEmail()
    {
        IsEmailVerified = true;
    }

    public void Deposit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Deposit amount must be positive", nameof(amount));
        
        AvailableBalance += amount;
    }

    public void LockBalanceForBet(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        if (AvailableBalance < amount)
            throw new InvalidOperationException("Insufficient available balance");
        
        AvailableBalance -= amount;
        LockedBetBalance += amount;
    }

    public void UnlockBalanceAfterBet(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        LockedBetBalance -= amount;
    }

    public void CreditWinnings(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        AvailableBalance += amount;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Withdrawal amount must be positive", nameof(amount));
        
        if (AvailableBalance < amount)
            throw new InvalidOperationException("Insufficient available balance");
        
        AvailableBalance -= amount;
    }

    public void AddReferralEarnings(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        TotalReferralEarnings += amount;
        AvailableBalance += amount;
    }

    public void MakeAdmin()
    {
        IsAdmin = true;
    }

    public void RevokeAdmin()
    {
        IsAdmin = false;
    }
}

public enum AuthenticationType
{
    Wallet,          // MetaMask/WalletConnect
    EmailPassword    // Traditional email/password
}
