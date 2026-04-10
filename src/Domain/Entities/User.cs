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
    
    // Wager tracking (AML compliance)
    public decimal TotalDeposited { get; private set; }
    public decimal TotalWagered { get; private set; }
    
    // Referral
    public Guid? ReferredByUserId { get; private set; }
    public User? ReferredBy { get; private set; }
    public decimal TotalReferralEarnings { get; private set; }
    public string? ReferralCode { get; private set; }
    public int TotalReferrals { get; private set; }
    
    // Daily Check-In
    public int CurrentCheckInStreak { get; private set; }
    public int LongestCheckInStreak { get; private set; }
    public DateTime? LastCheckInDate { get; private set; }
    public int TotalCheckIns { get; private set; }
    
    // Collections
    public ICollection<Bet> Bets { get; private set; } = new List<Bet>();
    public ICollection<Transaction> Transactions { get; private set; } = new List<Transaction>();
    public ICollection<User> Referrals { get; private set; } = new List<User>();
    public ICollection<ReferralEarning> ReferralEarningsGiven { get; private set; } = new List<ReferralEarning>();
    public ICollection<ReferralEarning> ReferralEarningsReceived { get; private set; } = new List<ReferralEarning>();
    public ICollection<DailyCheckIn> CheckIns { get; private set; } = new List<DailyCheckIn>();
    
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
            ReferralCode = GenerateReferralCode(),
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
            ReferralCode = GenerateReferralCode(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ReferredByUserId = referredBy
        };
    }

    private static string GenerateReferralCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No confusing chars
        var random = new Random();
        return new string(Enumerable.Range(0, 8)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
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
        TotalDeposited += amount;
    }

    public void LockBalanceForBet(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));
        
        if (AvailableBalance < amount)
            throw new InvalidOperationException("Insufficient available balance");
        
        AvailableBalance -= amount;
        LockedBetBalance += amount;
        TotalWagered += amount; // Track wager for AML
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
        TotalReferrals++; // Track referral activity
    }

    public bool CanCheckInToday()
    {
        return LastCheckInDate == null || LastCheckInDate.Value.Date < DateTime.UtcNow.Date;
    }

    public void CheckIn(decimal rewardAmount)
    {
        if (!CanCheckInToday())
            throw new InvalidOperationException("Already checked in today");

        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        // Update streak
        if (LastCheckInDate?.Date == yesterday)
        {
            CurrentCheckInStreak++;
        }
        else if (LastCheckInDate?.Date < yesterday || LastCheckInDate == null)
        {
            CurrentCheckInStreak = 1; // Reset streak
        }

        if (CurrentCheckInStreak > LongestCheckInStreak)
        {
            LongestCheckInStreak = CurrentCheckInStreak;
        }

        LastCheckInDate = today;
        TotalCheckIns++;
        
        // Award bonus
        if (rewardAmount > 0)
        {
            BonusBalance += rewardAmount;
        }
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
