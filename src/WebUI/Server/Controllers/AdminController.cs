using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[AdminOnly]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IBlockchainService _blockchainService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ApplicationDbContext context,
        IBlockchainService blockchainService,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _context = context;
        _blockchainService = blockchainService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get platform statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] string period = "today")
    {
        var now = DateTime.UtcNow;
        var startDate = period switch
        {
            "today" => now.Date,
            "week" => now.Date.AddDays(-7),
            "month" => now.Date.AddMonths(-1),
            "year" => now.Date.AddYears(-1),
            _ => now.Date
        };

        // User stats
        var totalUsers = await _context.Users.CountAsync();
        var newUsers = await _context.Users
            .CountAsync(u => u.CreatedAt >= startDate);
        var activeUsers = await _context.Bets
            .Where(b => b.PlacedAt >= startDate)
            .Select(b => b.UserId)
            .Distinct()
            .CountAsync();

        // Betting stats
        var allBets = await _context.Bets
            .Where(b => b.PlacedAt >= startDate && b.SettledAt.HasValue)
            .ToListAsync();

        var totalBets = allBets.Count;
        var totalWagered = allBets.Sum(b => b.Amount);
        var totalPayouts = allBets.Sum(b => b.Payout ?? 0);
        var houseProfit = totalWagered - totalPayouts;
        var avgBetSize = totalBets > 0 ? totalWagered / totalBets : 0;

        // Transaction stats
        var deposits = await _context.Transactions
            .Where(t => t.Type == TransactionType.Deposit && t.CreatedAt >= startDate)
            .SumAsync(t => t.Amount);
        
        var withdrawals = await _context.Transactions
            .Where(t => t.Type == TransactionType.Withdrawal && t.CreatedAt >= startDate)
            .ToListAsync();
        
        var withdrawalAmount = withdrawals.Sum(t => t.Amount);
        
        // Calculate withdrawal fees collected
        var feePercentage = decimal.Parse(_configuration["Blockchain:Fees:WithdrawalFeePercentage"] ?? "0.5");
        var minFee = decimal.Parse(_configuration["Blockchain:Fees:MinimumWithdrawalFee"] ?? "0.001");
        var feesCollected = withdrawals.Sum(t => Math.Max(t.Amount * (feePercentage / 100m), minFee));

        // Game stats
        var totalRounds = await _context.GameRounds
            .CountAsync(r => r.StartTime >= startDate);
        
        var settledRounds = await _context.GameRounds
            .Where(r => r.StartTime >= startDate && r.Phase == GamePhase.Settled)
            .ToListAsync();

        var higherWins = settledRounds.Count(r => r.Result == BetOutcome.Higher);
        var lowerWins = settledRounds.Count(r => r.Result == BetOutcome.Lower);

        // Revenue breakdown
        var totalRevenue = houseProfit + feesCollected;

        return Ok(new
        {
            period,
            users = new
            {
                total = totalUsers,
                newUsers,
                activeUsers
            },
            betting = new
            {
                totalBets,
                totalWagered,
                totalPayouts,
                houseProfit,
                avgBetSize,
                winRate = totalBets > 0 ? (decimal)allBets.Count(b => b.IsWin) / totalBets * 100 : 0
            },
            transactions = new
            {
                deposits,
                withdrawals = withdrawalAmount,
                feesCollected,
                netFlow = deposits - withdrawalAmount
            },
            games = new
            {
                totalRounds,
                settledRounds = settledRounds.Count,
                higherWins,
                lowerWins,
                higherWinRate = settledRounds.Count > 0 ? (decimal)higherWins / settledRounds.Count * 100 : 0
            },
            revenue = new
            {
                total = totalRevenue,
                fromHouseEdge = houseProfit,
                fromWithdrawalFees = feesCollected,
                houseEdgePercentage = totalWagered > 0 ? houseProfit / totalWagered * 100 : 0
            }
        });
    }

    /// <summary>
    /// Get top players by profit
    /// </summary>
    [HttpGet("top-players")]
    public async Task<IActionResult> GetTopPlayers([FromQuery] int limit = 10)
    {
        var userBets = await _context.Bets
            .Where(b => b.SettledAt.HasValue)
            .GroupBy(b => b.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalWagered = g.Sum(b => b.Amount),
                TotalPayout = g.Sum(b => b.Payout ?? 0),
                TotalBets = g.Count(),
                Wins = g.Count(b => b.IsWin)
            })
            .ToListAsync();

        var topPlayers = userBets
            .Select(u => new
            {
                u.UserId,
                u.TotalWagered,
                u.TotalPayout,
                Profit = u.TotalPayout - u.TotalWagered,
                u.TotalBets,
                u.Wins,
                WinRate = u.TotalBets > 0 ? (decimal)u.Wins / u.TotalBets * 100 : 0
            })
            .OrderByDescending(u => u.Profit)
            .Take(limit)
            .ToList();

        // Get user details
        var userIds = topPlayers.Select(p => p.UserId).ToList();
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.WalletAddress,
                u.Email,
                u.Username,
                u.CreatedAt
            })
            .ToListAsync();

        var result = topPlayers.Select(p =>
        {
            var user = users.FirstOrDefault(u => u.Id == p.UserId);
            return new
            {
                userId = p.UserId,
                walletAddress = user?.WalletAddress?.Substring(0, 10) + "...",
                username = user?.Username ?? "Anonymous",
                totalWagered = p.TotalWagered,
                totalPayout = p.TotalPayout,
                profit = p.Profit,
                totalBets = p.TotalBets,
                wins = p.Wins,
                winRate = p.WinRate,
                memberSince = user?.CreatedAt
            };
        });

        return Ok(new { players = result });
    }

    /// <summary>
    /// Get revenue chart data
    /// </summary>
    [HttpGet("revenue-chart")]
    public async Task<IActionResult> GetRevenueChart([FromQuery] int days = 30)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-days);
        
        var dailyStats = new List<object>();

        for (int i = 0; i < days; i++)
        {
            var date = startDate.AddDays(i);
            var nextDate = date.AddDays(1);

            var bets = await _context.Bets
                .Where(b => b.PlacedAt >= date && b.PlacedAt < nextDate && b.SettledAt.HasValue)
                .ToListAsync();

            var wagered = bets.Sum(b => b.Amount);
            var payouts = bets.Sum(b => b.Payout ?? 0);
            var houseProfit = wagered - payouts;

            var withdrawals = await _context.Transactions
                .Where(t => t.Type == TransactionType.Withdrawal && t.CreatedAt >= date && t.CreatedAt < nextDate)
                .ToListAsync();

            var feePercentage = decimal.Parse(_configuration["Blockchain:Fees:WithdrawalFeePercentage"] ?? "0.5");
            var minFee = decimal.Parse(_configuration["Blockchain:Fees:MinimumWithdrawalFee"] ?? "0.001");
            var fees = withdrawals.Sum(t => Math.Max(t.Amount * (feePercentage / 100m), minFee));

            dailyStats.Add(new
            {
                date = date.ToString("yyyy-MM-dd"),
                wagered,
                houseProfit,
                withdrawalFees = fees,
                totalRevenue = houseProfit + fees,
                bets = bets.Count
            });
        }

        return Ok(new { data = dailyStats });
    }

    /// <summary>
    /// Get pending withdrawals
    /// </summary>
    [HttpGet("pending-withdrawals")]
    public async Task<IActionResult> GetPendingWithdrawals()
    {
        var pending = await _context.Transactions
            .Where(t => t.Type == TransactionType.Withdrawal && 
                       (t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Processing))
            .Include(t => t.User)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                userId = t.UserId,
                walletAddress = t.User.WalletAddress,
                t.Amount,
                t.DestinationAddress,
                t.Status,
                t.CreatedAt
            })
            .ToListAsync();

        return Ok(new { withdrawals = pending });
    }

    /// <summary>
    /// Update game settings
    /// </summary>
    [HttpPatch("settings")]
    public IActionResult UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        // TODO: Implement settings update (save to database, not just appsettings)
        
        _logger.LogInformation("Admin updated settings: {@Settings}", request);

        return Ok(new { message = "Settings updated (restart required)" });
    }

    /// <summary>
    /// Get hot wallet balance
    /// </summary>
    [HttpGet("wallet-balance")]
    public async Task<IActionResult> GetWalletBalance()
    {
        var address = _blockchainService.GetDepositAddress();
        var balance = await _blockchainService.GetBalance(address);

        return Ok(new
        {
            address,
            balance,
            chain = "Polygon"
        });
    }

    /// <summary>
    /// Get user profile (admin only)
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserProfile(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        
        if (user == null)
        {
            return NotFound();
        }

        var bets = await _context.Bets
            .Where(b => b.UserId == userId && b.SettledAt.HasValue)
            .ToListAsync();

        var stats = new
        {
            totalBets = bets.Count,
            totalWagered = bets.Sum(b => b.Amount),
            netProfit = bets.Sum(b => (b.Payout ?? 0) - b.Amount),
            winRate = bets.Count > 0 ? (decimal)bets.Count(b => b.IsWin) / bets.Count * 100 : 0
        };

        return Ok(new
        {
            id = user.Id,
            walletAddress = user.WalletAddress,
            email = user.Email,
            username = user.Username,
            authType = user.AuthType.ToString(),
            availableBalance = user.AvailableBalance,
            lockedBalance = user.LockedBetBalance,
            bonusBalance = user.BonusBalance,
            totalBalance = user.AvailableBalance + user.LockedBetBalance + user.BonusBalance,
            totalReferralEarnings = user.TotalReferralEarnings,
            createdAt = user.CreatedAt,
            lastLoginAt = user.LastLoginAt,
            isActive = user.IsActive,
            stats
        });
    }

    /// <summary>
    /// Get user transactions (admin only)
    /// </summary>
    [HttpGet("user/{userId}/transactions")]
    public async Task<IActionResult> GetUserTransactions(Guid userId, [FromQuery] int limit = 50)
    {
        var transactions = await _context.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .Select(t => new
            {
                t.Id,
                Type = t.Type.ToString(),
                t.Amount,
                Status = t.Status.ToString(),
                t.BlockchainTxHash,
                t.CreatedAt
            })
            .ToListAsync();

        return Ok(new { transactions });
    }

    /// <summary>
    /// Get user bets (admin only)
    /// </summary>
    [HttpGet("user/{userId}/bets")]
    public async Task<IActionResult> GetUserBets(Guid userId, [FromQuery] int limit = 100)
    {
        var bets = await _context.Bets
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.PlacedAt)
            .Take(limit)
            .Select(b => new
            {
                b.Id,
                b.Amount,
                Prediction = b.Prediction.ToString(),
                b.Multiplier,
                b.IsWin,
                b.Payout,
                b.PlacedAt,
                b.SettledAt
            })
            .ToListAsync();

        return Ok(new { bets });
    }

    /// <summary>
    /// Search users (admin only)
    /// </summary>
    [HttpGet("users/search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string query, [FromQuery] int limit = 20)
    {
        var users = await _context.Users
            .Where(u => 
                (u.WalletAddress != null && u.WalletAddress.Contains(query)) ||
                (u.Email != null && u.Email.Contains(query)) ||
                (u.Username != null && u.Username.Contains(query)))
            .Take(limit)
            .Select(u => new
            {
                u.Id,
                u.WalletAddress,
                u.Email,
                u.Username,
                u.CreatedAt,
                TotalBalance = u.AvailableBalance + u.LockedBetBalance + u.BonusBalance
            })
            .ToListAsync();

        return Ok(new { users });
    }
}

public record UpdateSettingsRequest(
    decimal? HouseEdgePercentage,
    decimal? WithdrawalFeePercentage,
    decimal? HigherMultiplier,
    decimal? LowerMultiplier,
    decimal? MinBet,
    decimal? MaxBet
);
