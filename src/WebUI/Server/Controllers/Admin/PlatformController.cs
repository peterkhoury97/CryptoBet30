using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;
using CryptoBet30.Application.Services;

namespace CryptoBet30.WebUI.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class PlatformController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IBlockchainService _blockchainService;
    private readonly ILogger<PlatformController> _logger;

    public PlatformController(
        ApplicationDbContext context,
        IBlockchainService blockchainService,
        ILogger<PlatformController> logger)
    {
        _context = context;
        _blockchainService = blockchainService;
        _logger = logger;
    }

    /// <summary>
    /// Get platform settings and status
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await GetOrCreateSettings();

        return Ok(new
        {
            withdrawalsEnabled = settings.WithdrawalsEnabled,
            withdrawalsDisabledReason = settings.WithdrawalsDisabledReason,
            withdrawalsDisabledAt = settings.WithdrawalsDisabledAt,
            
            depositsEnabled = settings.DepositsEnabled,
            depositsDisabledReason = settings.DepositsDisabledReason,
            depositsDisabledAt = settings.DepositsDisabledAt,
            
            bettingEnabled = settings.BettingEnabled,
            bettingDisabledReason = settings.BettingDisabledReason,
            
            hotWalletTargetBalance = settings.HotWalletTargetBalance,
            hotWalletMinimumBalance = settings.HotWalletMinimumBalance,
            autoSweepThreshold = settings.AutoSweepThreshold,
            autoSweepEnabled = settings.AutoSweepEnabled,
            
            platformFeeWallet = settings.PlatformFeeWallet,
            totalFeesCollected = settings.TotalFeesCollected,
            totalFeesWithdrawn = settings.TotalFeesWithdrawn,
            availableFees = settings.TotalFeesCollected - settings.TotalFeesWithdrawn,
            
            maintenanceMode = settings.MaintenanceMode,
            maintenanceMessage = settings.MaintenanceMessage,
            
            updatedAt = settings.UpdatedAt
        });
    }

    /// <summary>
    /// Get hot wallet status across all networks
    /// </summary>
    [HttpGet("wallet/status")]
    public async Task<IActionResult> GetHotWalletStatus()
    {
        var networks = new[] { "POLYGON", "TRON", "BINANCE" };
        var walletStatus = new List<object>();

        foreach (var network in networks)
        {
            try
            {
                var balance = await _blockchainService.GetHotWalletBalance(network);
                
                // Get total user deposits on this network
                var totalDeposits = await _context.Transactions
                    .Where(t => t.Type == TransactionType.Deposit && 
                               t.Network == network &&
                               t.Status == TransactionStatus.Completed)
                    .SumAsync(t => t.Amount);

                // Get total user withdrawals on this network
                var totalWithdrawals = await _context.Transactions
                    .Where(t => t.Type == TransactionType.Withdrawal && 
                               t.Network == network &&
                               t.Status == TransactionStatus.Completed)
                    .SumAsync(t => t.Amount);

                // Get pending withdrawals
                var pendingWithdrawals = await _context.Transactions
                    .Where(t => t.Type == TransactionType.Withdrawal && 
                               t.Network == network &&
                               (t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Processing))
                    .ToListAsync();

                var pendingAmount = pendingWithdrawals.Sum(t => t.Amount);
                var pendingCount = pendingWithdrawals.Count;

                // Calculate metrics
                var netFlow = totalDeposits - totalWithdrawals;
                var availableBalance = balance - pendingAmount;
                var utilizationRate = totalDeposits > 0 ? (balance / totalDeposits) * 100 : 0;

                walletStatus.Add(new
                {
                    network,
                    balance,
                    availableBalance,
                    totalDeposits,
                    totalWithdrawals,
                    netFlow,
                    pendingWithdrawals = new
                    {
                        count = pendingCount,
                        amount = pendingAmount
                    },
                    utilizationRate = Math.Round(utilizationRate, 2),
                    isHealthy = availableBalance > 0 && utilizationRate >= 20
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting wallet status for {Network}", network);
                walletStatus.Add(new
                {
                    network,
                    error = "Unable to fetch balance"
                });
            }
        }

        return Ok(new { wallets = walletStatus });
    }

    /// <summary>
    /// Get hot wallet history (balance over time)
    /// </summary>
    [HttpGet("wallet/history")]
    public async Task<IActionResult> GetHotWalletHistory([FromQuery] int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        
        var snapshots = await _context.Set<HotWalletSnapshot>()
            .Where(s => s.CreatedAt >= since)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        var grouped = snapshots
            .GroupBy(s => s.Network)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => new
                {
                    timestamp = s.CreatedAt,
                    balance = s.Balance,
                    netFlow = s.NetFlow,
                    pendingWithdrawals = s.PendingWithdrawalAmount
                }).ToList()
            );

        return Ok(new { history = grouped });
    }

    /// <summary>
    /// Take wallet snapshot manually
    /// </summary>
    [HttpPost("wallet/snapshot")]
    public async Task<IActionResult> TakeWalletSnapshot()
    {
        var networks = new[] { "POLYGON", "TRON", "BINANCE" };

        foreach (var network in networks)
        {
            try
            {
                var balance = await _blockchainService.GetHotWalletBalance(network);
                
                var totalDeposits = await _context.Transactions
                    .Where(t => t.Type == TransactionType.Deposit && t.Network == network)
                    .SumAsync(t => t.Amount);

                var totalWithdrawals = await _context.Transactions
                    .Where(t => t.Type == TransactionType.Withdrawal && t.Network == network)
                    .SumAsync(t => t.Amount);

                var pending = await _context.Transactions
                    .Where(t => t.Type == TransactionType.Withdrawal && 
                               t.Network == network &&
                               t.Status == TransactionStatus.Pending)
                    .ToListAsync();

                var snapshot = HotWalletSnapshot.Create(
                    network,
                    balance,
                    totalDeposits,
                    totalWithdrawals,
                    pending.Count,
                    pending.Sum(t => t.Amount)
                );

                await _context.Set<HotWalletSnapshot>().AddAsync(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error taking snapshot for {Network}", network);
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Snapshot taken for all networks" });
    }

    /// <summary>
    /// Enable/disable withdrawals
    /// </summary>
    [HttpPost("withdrawals/toggle")]
    public async Task<IActionResult> ToggleWithdrawals([FromBody] ToggleRequest request)
    {
        var adminId = GetAdminId();
        var settings = await GetOrCreateSettings();

        if (request.Enabled)
        {
            settings.EnableWithdrawals(adminId);
            _logger.LogWarning("Admin {AdminId} enabled withdrawals", adminId);
        }
        else
        {
            settings.DisableWithdrawals(request.Reason ?? "Admin action", adminId);
            _logger.LogWarning("Admin {AdminId} disabled withdrawals: {Reason}", adminId, request.Reason);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            withdrawalsEnabled = settings.WithdrawalsEnabled,
            reason = settings.WithdrawalsDisabledReason
        });
    }

    /// <summary>
    /// Enable/disable deposits
    /// </summary>
    [HttpPost("deposits/toggle")]
    public async Task<IActionResult> ToggleDeposits([FromBody] ToggleRequest request)
    {
        var adminId = GetAdminId();
        var settings = await GetOrCreateSettings();

        if (request.Enabled)
        {
            settings.EnableDeposits(adminId);
            _logger.LogWarning("Admin {AdminId} enabled deposits", adminId);
        }
        else
        {
            settings.DisableDeposits(request.Reason ?? "Admin action", adminId);
            _logger.LogWarning("Admin {AdminId} disabled deposits: {Reason}", adminId, request.Reason);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            depositsEnabled = settings.DepositsEnabled,
            reason = settings.DepositsDisabledReason
        });
    }

    /// <summary>
    /// Enable/disable betting
    /// </summary>
    [HttpPost("betting/toggle")]
    public async Task<IActionResult> ToggleBetting([FromBody] ToggleRequest request)
    {
        var adminId = GetAdminId();
        var settings = await GetOrCreateSettings();

        if (request.Enabled)
        {
            settings.EnableBetting(adminId);
            _logger.LogWarning("Admin {AdminId} enabled betting", adminId);
        }
        else
        {
            settings.DisableBetting(request.Reason ?? "Admin action", adminId);
            _logger.LogWarning("Admin {AdminId} disabled betting: {Reason}", adminId, request.Reason);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            bettingEnabled = settings.BettingEnabled,
            reason = settings.BettingDisabledReason
        });
    }

    /// <summary>
    /// Update hot wallet settings
    /// </summary>
    [HttpPost("wallet/settings")]
    public async Task<IActionResult> UpdateHotWalletSettings([FromBody] HotWalletSettingsRequest request)
    {
        var adminId = GetAdminId();
        var settings = await GetOrCreateSettings();

        settings.UpdateHotWalletSettings(
            request.TargetBalance,
            request.MinimumBalance,
            request.AutoSweepThreshold,
            request.AutoSweepEnabled,
            adminId
        );

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {AdminId} updated hot wallet settings: Target={Target}, Min={Min}, Sweep={Sweep}",
            adminId,
            request.TargetBalance,
            request.MinimumBalance,
            request.AutoSweepThreshold
        );

        return Ok(new { success = true });
    }

    /// <summary>
    /// Withdraw collected fees to platform wallet
    /// </summary>
    [HttpPost("fees/withdraw")]
    public async Task<IActionResult> WithdrawFees([FromBody] WithdrawFeesRequest request)
    {
        var adminId = GetAdminId();
        var settings = await GetOrCreateSettings();

        var availableFees = settings.TotalFeesCollected - settings.TotalFeesWithdrawn;

        if (request.Amount > availableFees)
        {
            return BadRequest(new { error = "Amount exceeds available fees" });
        }

        // TODO: Implement actual blockchain transfer to platform fee wallet
        // For now, just record it
        settings.RecordFeeWithdrawal(request.Amount);
        await _context.SaveChangesAsync();

        _logger.LogWarning(
            "Admin {AdminId} withdrew {Amount} USDT in fees to platform wallet",
            adminId,
            request.Amount
        );

        return Ok(new
        {
            success = true,
            amount = request.Amount,
            remainingFees = settings.TotalFeesCollected - settings.TotalFeesWithdrawn
        });
    }

    /// <summary>
    /// Enable/disable maintenance mode
    /// </summary>
    [HttpPost("maintenance/toggle")]
    public async Task<IActionResult> ToggleMaintenanceMode([FromBody] MaintenanceModeRequest request)
    {
        var adminId = GetAdminId();
        var settings = await GetOrCreateSettings();

        if (request.Enabled)
        {
            settings.EnableMaintenanceMode(request.Message ?? "Platform is under maintenance", adminId);
            _logger.LogWarning("Admin {AdminId} enabled maintenance mode", adminId);
        }
        else
        {
            settings.DisableMaintenanceMode(adminId);
            _logger.LogWarning("Admin {AdminId} disabled maintenance mode", adminId);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            maintenanceMode = settings.MaintenanceMode,
            message = settings.MaintenanceMessage
        });
    }

    private async Task<PlatformSettings> GetOrCreateSettings()
    {
        var settings = await _context.Set<PlatformSettings>().FirstOrDefaultAsync();
        
        if (settings == null)
        {
            settings = PlatformSettings.CreateDefault("0x0000000000000000000000000000000000000000");
            await _context.Set<PlatformSettings>().AddAsync(settings);
            await _context.SaveChangesAsync();
        }

        return settings;
    }

    private Guid GetAdminId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : Guid.Empty;
    }
}

public record ToggleRequest(bool Enabled, string? Reason);
public record HotWalletSettingsRequest(
    decimal TargetBalance,
    decimal MinimumBalance,
    decimal AutoSweepThreshold,
    bool AutoSweepEnabled
);
public record WithdrawFeesRequest(decimal Amount);
public record MaintenanceModeRequest(bool Enabled, string? Message);
