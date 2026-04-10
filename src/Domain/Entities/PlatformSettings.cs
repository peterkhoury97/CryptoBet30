namespace CryptoBet30.Domain.Entities;

/// <summary>
/// Platform-wide settings and controls
/// </summary>
public class PlatformSettings
{
    public Guid Id { get; private set; }
    
    // Withdrawal Controls
    public bool WithdrawalsEnabled { get; private set; }
    public string? WithdrawalsDisabledReason { get; private set; }
    public DateTime? WithdrawalsDisabledAt { get; private set; }
    public Guid? WithdrawalsDisabledByUserId { get; private set; }
    
    // Deposit Controls
    public bool DepositsEnabled { get; private set; }
    public string? DepositsDisabledReason { get; private set; }
    public DateTime? DepositsDisabledAt { get; private set; }
    public Guid? DepositsDisabledByUserId { get; private set; }
    
    // Betting Controls
    public bool BettingEnabled { get; private set; }
    public string? BettingDisabledReason { get; private set; }
    
    // Hot Wallet Management
    public decimal HotWalletTargetBalance { get; private set; }
    public decimal HotWalletMinimumBalance { get; private set; }
    public decimal AutoSweepThreshold { get; private set; }
    public bool AutoSweepEnabled { get; private set; }
    
    // Platform Wallet (Fee Collection)
    public string PlatformFeeWallet { get; private set; }
    public decimal TotalFeesCollected { get; private set; }
    public decimal TotalFeesWithdrawn { get; private set; }
    
    // Maintenance Mode
    public bool MaintenanceMode { get; private set; }
    public string? MaintenanceMessage { get; private set; }
    
    public DateTime UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    private PlatformSettings() { } // EF Core

    public static PlatformSettings CreateDefault(string platformFeeWallet)
    {
        return new PlatformSettings
        {
            Id = Guid.NewGuid(),
            WithdrawalsEnabled = true,
            DepositsEnabled = true,
            BettingEnabled = true,
            HotWalletTargetBalance = 10000m,
            HotWalletMinimumBalance = 5000m,
            AutoSweepThreshold = 15000m,
            AutoSweepEnabled = false,
            PlatformFeeWallet = platformFeeWallet,
            TotalFeesCollected = 0m,
            TotalFeesWithdrawn = 0m,
            MaintenanceMode = false,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void EnableWithdrawals(Guid adminId)
    {
        WithdrawalsEnabled = true;
        WithdrawalsDisabledReason = null;
        WithdrawalsDisabledAt = null;
        WithdrawalsDisabledByUserId = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void DisableWithdrawals(string reason, Guid adminId)
    {
        WithdrawalsEnabled = false;
        WithdrawalsDisabledReason = reason;
        WithdrawalsDisabledAt = DateTime.UtcNow;
        WithdrawalsDisabledByUserId = adminId;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void EnableDeposits(Guid adminId)
    {
        DepositsEnabled = true;
        DepositsDisabledReason = null;
        DepositsDisabledAt = null;
        DepositsDisabledByUserId = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void DisableDeposits(string reason, Guid adminId)
    {
        DepositsEnabled = false;
        DepositsDisabledReason = reason;
        DepositsDisabledAt = DateTime.UtcNow;
        DepositsDisabledByUserId = adminId;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void EnableBetting(Guid adminId)
    {
        BettingEnabled = true;
        BettingDisabledReason = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void DisableBetting(string reason, Guid adminId)
    {
        BettingEnabled = false;
        BettingDisabledReason = reason;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void UpdateHotWalletSettings(
        decimal targetBalance,
        decimal minimumBalance,
        decimal autoSweepThreshold,
        bool autoSweepEnabled,
        Guid adminId)
    {
        HotWalletTargetBalance = targetBalance;
        HotWalletMinimumBalance = minimumBalance;
        AutoSweepThreshold = autoSweepThreshold;
        AutoSweepEnabled = autoSweepEnabled;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void RecordFeeCollection(decimal amount)
    {
        TotalFeesCollected += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordFeeWithdrawal(decimal amount)
    {
        TotalFeesWithdrawn += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnableMaintenanceMode(string message, Guid adminId)
    {
        MaintenanceMode = true;
        MaintenanceMessage = message;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }

    public void DisableMaintenanceMode(Guid adminId)
    {
        MaintenanceMode = false;
        MaintenanceMessage = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = adminId;
    }
}

/// <summary>
/// Hot wallet balance snapshots for monitoring
/// </summary>
public class HotWalletSnapshot
{
    public Guid Id { get; private set; }
    public string Network { get; private set; }
    public decimal Balance { get; private set; }
    public decimal TotalUserDeposits { get; private set; }
    public decimal TotalUserWithdrawals { get; private set; }
    public decimal NetFlow { get; private set; }
    public int PendingWithdrawals { get; private set; }
    public decimal PendingWithdrawalAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private HotWalletSnapshot() { } // EF Core

    public static HotWalletSnapshot Create(
        string network,
        decimal balance,
        decimal totalDeposits,
        decimal totalWithdrawals,
        int pendingCount,
        decimal pendingAmount)
    {
        return new HotWalletSnapshot
        {
            Id = Guid.NewGuid(),
            Network = network,
            Balance = balance,
            TotalUserDeposits = totalDeposits,
            TotalUserWithdrawals = totalWithdrawals,
            NetFlow = totalDeposits - totalWithdrawals,
            PendingWithdrawals = pendingCount,
            PendingWithdrawalAmount = pendingAmount,
            CreatedAt = DateTime.UtcNow
        };
    }
}
