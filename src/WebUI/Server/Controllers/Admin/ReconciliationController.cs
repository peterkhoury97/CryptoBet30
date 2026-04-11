using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Infrastructure.Blockchain;

namespace CryptoBet30.WebUI.Server.Controllers.Admin;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class ReconciliationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IBlockchainService _blockchainService;
    private readonly ILogger<ReconciliationController> _logger;

    public ReconciliationController(
        ApplicationDbContext context,
        IBlockchainService blockchainService,
        ILogger<ReconciliationController> logger)
    {
        _context = context;
        _blockchainService = blockchainService;
        _logger = logger;
    }

    /// <summary>
    /// Full reconciliation report - critical for financial audit
    /// Compares database balances vs blockchain balances
    /// </summary>
    [HttpGet("report")]
    public async Task<IActionResult> GetReconciliationReport()
    {
        try
        {
            // 1. Total user balances (what users see on screen)
            var totalUserBalances = await _context.Users.SumAsync(u => u.Balance);

            // 2. Total pending withdrawals (committed but not sent)
            var pendingWithdrawals = await _context.Transactions
                .Where(t => t.Type == "Withdrawal" && t.Status == "Pending")
                .SumAsync(t => t.Amount);

            // 3. Total deposits (all time)
            var totalDeposited = await _context.Users.SumAsync(u => u.TotalDeposited);

            // 4. Total withdrawn (all time)
            var totalWithdrawn = await _context.Transactions
                .Where(t => t.Type == "Withdrawal" && t.Status == "Completed")
                .SumAsync(t => t.Amount);

            // 5. Total wagered
            var totalWagered = await _context.Bets.SumAsync(b => b.Amount);

            // 6. Total won
            var totalWon = await _context.Bets
                .Where(b => b.Result == "Win")
                .SumAsync(b => b.Payout ?? 0);

            // 7. Total lost (house earnings from bets)
            var totalLost = await _context.Bets
                .Where(b => b.Result == "Lose")
                .SumAsync(b => b.Amount);

            // 8. Platform fees collected
            var totalFeesCollected = await _context.Transactions
                .Where(t => t.Fee.HasValue)
                .SumAsync(t => t.Fee ?? 0);

            // 9. Referral commissions paid
            var totalReferralsPaid = await _context.Set<ReferralEarning>()
                .SumAsync(r => r.Amount);

            // 10. Check-in rewards paid
            var totalCheckInsPaid = await _context.Set<DailyCheckIn>()
                .SumAsync(c => c.RewardAmount);

            // 11. Hot wallet balances per network (from blockchain)
            var polygonBalance = await _blockchainService.GetHotWalletBalance("POLYGON");
            var arbitrumBalance = await _blockchainService.GetHotWalletBalance("ARBITRUM");
            var tronBalance = await _blockchainService.GetHotWalletBalance("TRON");
            var bscBalance = await _blockchainService.GetHotWalletBalance("BINANCE");
            
            var totalHotWalletBalance = polygonBalance + arbitrumBalance + tronBalance + bscBalance;

            // 12. Expected hot wallet balance
            var expectedHotWallet = totalDeposited - totalWithdrawn - totalFeesCollected;

            // 13. Discrepancy check
            var discrepancy = totalHotWalletBalance - totalUserBalances;
            var isHealthy = Math.Abs(discrepancy) < 1.0m; // Allow $1 rounding error

            // 14. Profit calculation
            var houseEdgeProfit = totalLost - totalWon;
            var totalProfit = houseEdgeProfit + totalFeesCollected - totalReferralsPaid - totalCheckInsPaid;
            var withdrawableProfit = totalHotWalletBalance - totalUserBalances - pendingWithdrawals;

            // 15. Risk metrics
            var utilizationRate = totalUserBalances > 0 
                ? (totalHotWalletBalance / totalUserBalances) * 100 
                : 0;
            
            var canCoverWithdrawals = totalHotWalletBalance >= (totalUserBalances + pendingWithdrawals);

            return Ok(new
            {
                timestamp = DateTime.UtcNow,
                status = isHealthy ? "HEALTHY" : "DISCREPANCY_DETECTED",
                
                // User Liabilities (what you owe)
                userLiabilities = new
                {
                    totalUserBalances,
                    pendingWithdrawals,
                    totalLiability = totalUserBalances + pendingWithdrawals
                },
                
                // Hot Wallet Assets (what you have)
                hotWalletAssets = new
                {
                    polygon = polygonBalance,
                    arbitrum = arbitrumBalance,
                    tron = tronBalance,
                    bsc = bscBalance,
                    total = totalHotWalletBalance
                },
                
                // Reconciliation
                reconciliation = new
                {
                    expected = totalUserBalances,
                    actual = totalHotWalletBalance,
                    discrepancy,
                    discrepancyPercentage = totalUserBalances > 0 
                        ? (discrepancy / totalUserBalances) * 100 
                        : 0,
                    isHealthy,
                    canCoverWithdrawals
                },
                
                // Financial Summary
                financialSummary = new
                {
                    totalDeposited,
                    totalWithdrawn,
                    netCashFlow = totalDeposited - totalWithdrawn,
                    totalWagered,
                    totalWon,
                    totalLost,
                    houseEdgeProfit,
                    totalFeesCollected,
                    totalReferralsPaid,
                    totalCheckInsPaid,
                    totalProfit,
                    withdrawableProfit
                },
                
                // Risk Metrics
                riskMetrics = new
                {
                    utilizationRate,
                    recommendedMinimum = totalUserBalances * 0.3m, // 30% reserve
                    currentReserve = totalHotWalletBalance - totalUserBalances,
                    reservePercentage = totalUserBalances > 0 
                        ? ((totalHotWalletBalance - totalUserBalances) / totalUserBalances) * 100 
                        : 0,
                    status = utilizationRate >= 20 && utilizationRate <= 50 ? "HEALTHY" : "WARNING"
                },
                
                // Alerts
                alerts = GenerateAlerts(
                    totalHotWalletBalance,
                    totalUserBalances,
                    pendingWithdrawals,
                    discrepancy,
                    utilizationRate,
                    canCoverWithdrawals
                )
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating reconciliation report");
            return StatusCode(500, new { error = "Failed to generate reconciliation report" });
        }
    }

    /// <summary>
    /// Quick balance check - lightweight version
    /// </summary>
    [HttpGet("quick")]
    public async Task<IActionResult> QuickCheck()
    {
        var totalUserBalances = await _context.Users.SumAsync(u => u.Balance);
        var totalHotWallet = await _blockchainService.GetHotWalletBalance("POLYGON")
            + await _blockchainService.GetHotWalletBalance("ARBITRUM");
        
        var discrepancy = totalHotWallet - totalUserBalances;
        
        return Ok(new
        {
            userBalances = totalUserBalances,
            hotWallet = totalHotWallet,
            discrepancy,
            status = Math.Abs(discrepancy) < 1.0m ? "OK" : "CHECK_NEEDED"
        });
    }

    /// <summary>
    /// User balance breakdown - top 20 users
    /// </summary>
    [HttpGet("top-balances")]
    public async Task<IActionResult> GetTopBalances([FromQuery] int limit = 20)
    {
        var topUsers = await _context.Users
            .OrderByDescending(u => u.Balance)
            .Take(limit)
            .Select(u => new
            {
                userId = u.Id,
                username = u.Username ?? u.Email ?? u.WalletAddress,
                balance = u.Balance,
                totalDeposited = u.TotalDeposited,
                totalWagered = u.TotalWagered,
                lastActive = u.LastLoginAt
            })
            .ToListAsync();

        var totalTop = topUsers.Sum(u => u.balance);
        var totalAll = await _context.Users.SumAsync(u => u.Balance);
        var concentration = totalAll > 0 ? (totalTop / totalAll) * 100 : 0;

        return Ok(new
        {
            topUsers,
            summary = new
            {
                topUsersTotal = totalTop,
                allUsersTotal = totalAll,
                concentrationPercentage = concentration,
                warning = concentration > 50 ? "High concentration risk - top users hold >50% of funds" : null
            }
        });
    }

    /// <summary>
    /// Deposit/Withdrawal flow audit
    /// </summary>
    [HttpGet("flow")]
    public async Task<IActionResult> GetCashFlow([FromQuery] int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var deposits = await _context.Transactions
            .Where(t => t.Type == "Deposit" && t.CreatedAt >= since)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new
            {
                date = g.Key,
                count = g.Count(),
                total = g.Sum(t => t.Amount)
            })
            .ToListAsync();

        var withdrawals = await _context.Transactions
            .Where(t => t.Type == "Withdrawal" && t.CreatedAt >= since)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new
            {
                date = g.Key,
                count = g.Count(),
                total = g.Sum(t => t.Amount)
            })
            .ToListAsync();

        return Ok(new
        {
            period = $"Last {days} days",
            deposits,
            withdrawals,
            netFlow = deposits.Sum(d => d.total) - withdrawals.Sum(w => w.total)
        });
    }

    private List<string> GenerateAlerts(
        decimal hotWallet,
        decimal userBalances,
        decimal pendingWithdrawals,
        decimal discrepancy,
        decimal utilizationRate,
        bool canCoverWithdrawals)
    {
        var alerts = new List<string>();

        if (Math.Abs(discrepancy) > 100)
        {
            alerts.Add($"⚠️ CRITICAL: Discrepancy detected: ${Math.Abs(discrepancy):N2}");
        }

        if (!canCoverWithdrawals)
        {
            alerts.Add("🚨 EMERGENCY: Cannot cover all user balances + pending withdrawals!");
        }

        if (utilizationRate < 20)
        {
            alerts.Add($"⚠️ Low reserve: {utilizationRate:N1}% (recommend 20-50%)");
        }

        if (utilizationRate > 50)
        {
            alerts.Add($"💰 High reserve: {utilizationRate:N1}% - consider securing profits");
        }

        if (hotWallet < 5000)
        {
            alerts.Add($"⚠️ Hot wallet low: ${hotWallet:N2} (minimum: $5,000)");
        }

        if (pendingWithdrawals > hotWallet * 0.5m)
        {
            alerts.Add($"⚠️ High pending withdrawals: ${pendingWithdrawals:N2} ({(pendingWithdrawals / hotWallet) * 100:N1}% of hot wallet)");
        }

        if (alerts.Count == 0)
        {
            alerts.Add("✅ All systems healthy");
        }

        return alerts;
    }
}
