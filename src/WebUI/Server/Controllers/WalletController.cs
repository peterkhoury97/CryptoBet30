using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IBlockchainService _blockchainService;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WalletController> _logger;

    public WalletController(
        IMediator mediator,
        IBlockchainService blockchainService,
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<WalletController> logger)
    {
        _mediator = mediator;
        _blockchainService = blockchainService;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get user balance
    /// </summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance()
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId.Value);
        
        if (user == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            availableBalance = user.AvailableBalance,
            lockedBalance = user.LockedBetBalance,
            bonusBalance = user.BonusBalance,
            totalBalance = user.AvailableBalance + user.LockedBetBalance + user.BonusBalance
        });
    }

    /// <summary>
    /// Get wallet info with wager requirements
    /// </summary>
    [HttpGet("info")]
    public async Task<IActionResult> GetWalletInfo()
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null)
        {
            return NotFound();
        }

        // Calculate wager requirement
        var requiredWagerTotal = user.TotalDeposited;
        var requiredWager = Math.Max(0, requiredWagerTotal - user.TotalWagered);
        var wagerProgress = requiredWagerTotal > 0 ? (user.TotalWagered / requiredWagerTotal * 100m) : 100m;

        return Ok(new
        {
            availableBalance = user.AvailableBalance,
            lockedBalance = user.LockedBetBalance,
            totalWagered = user.TotalWagered,
            requiredWager,
            requiredWagerTotal,
            wagerProgress = Math.Min(wagerProgress, 100m),
            canWithdraw = requiredWager <= 0
        });
    }

    /// <summary>
    /// Get deposit address for specific network
    /// </summary>
    [HttpGet("deposit-address")]
    public async Task<IActionResult> GetDepositAddress([FromQuery] string network = "POLYGON")
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        // CHECK: Are deposits enabled?
        var platformSettings = await _context.Set<PlatformSettings>().FirstOrDefaultAsync();
        if (platformSettings != null && !platformSettings.DepositsEnabled)
        {
            return BadRequest(new
            {
                error = "Deposits are temporarily disabled",
                reason = platformSettings.DepositsDisabledReason
            });
        }

        var walletService = HttpContext.RequestServices.GetRequiredService<IWalletGenerationService>();
        var address = await walletService.GetUserDepositAddress(userId.Value, network);

        var networkInfo = network.ToUpper() switch
        {
            "POLYGON" => new
            {
                name = "Polygon",
                symbol = "MATIC",
                currency = "USDT",
                confirmations = 12,
                estimatedTime = "~2 minutes",
                fee = "~$0.01",
                explorerUrl = $"https://polygonscan.com/address/{address}"
            },
            "TRON" => new
            {
                name = "Tron",
                symbol = "TRX",
                currency = "USDT (TRC-20)",
                confirmations = 19,
                estimatedTime = "~1 minute",
                fee = "~$0.001",
                explorerUrl = $"https://tronscan.org/#/address/{address}"
            },
            "BINANCE" => new
            {
                name = "Binance Smart Chain",
                symbol = "BNB",
                currency = "USDT (BEP-20)",
                confirmations = 15,
                estimatedTime = "~1 minute",
                fee = "~$0.05",
                explorerUrl = $"https://bscscan.com/address/{address}"
            },
            "ARBITRUM" => new
            {
                name = "Arbitrum One",
                symbol = "ETH",
                currency = "USDT (Arbitrum)",
                confirmations = 10,
                estimatedTime = "~30 seconds",
                fee = "~$0.001",
                explorerUrl = $"https://arbiscan.io/address/{address}"
            },
            _ => new
            {
                name = network,
                symbol = "?",
                currency = "USDT",
                confirmations = 12,
                estimatedTime = "~2 minutes",
                fee = "~$0.01",
                explorerUrl = $"https://polygonscan.com/address/{address}"
            }
        };

        return Ok(new
        {
            address,
            network = networkInfo,
            important = new[]
            {
                "Only send USDT to this address",
                $"Make sure you select {networkInfo.name} network",
                "Deposits are automatically credited after confirmations",
                "Minimum deposit: $10 USDT"
            }
        });
    }

    /// <summary>
    /// Calculate withdrawal fee
    /// </summary>
    [HttpGet("withdrawal-fee")]
    public async Task<IActionResult> GetWithdrawalFee([FromQuery] decimal amount)
    {
        var feePercentage = decimal.Parse(_configuration["Blockchain:Fees:WithdrawalFeePercentage"] ?? "0.5");
        var minFee = decimal.Parse(_configuration["Blockchain:Fees:MinimumWithdrawalFee"] ?? "0.001");
        
        var fee = Math.Max(amount * (feePercentage / 100m), minFee);
        var gasEstimate = await _blockchainService.EstimateWithdrawalFee();
        var totalFee = fee + gasEstimate;
        
        return Ok(new
        {
            amount,
            platformFee = fee,
            platformFeePercentage = feePercentage,
            gasFee = gasEstimate,
            totalFee,
            youReceive = amount - totalFee
        });
    }

    /// <summary>
    /// Request withdrawal
    /// </summary>
    [HttpPost("withdraw")]
    public async Task<IActionResult> Withdraw([FromBody] WithdrawRequest request)
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        // CHECK: Are withdrawals enabled?
        var platformSettings = await _context.Set<PlatformSettings>().FirstOrDefaultAsync();
        if (platformSettings != null && !platformSettings.WithdrawalsEnabled)
        {
            return BadRequest(new
            {
                error = "Withdrawals are temporarily disabled",
                reason = platformSettings.WithdrawalsDisabledReason
            });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        
        if (user == null)
        {
            return NotFound();
        }

        // Check wager requirement (AML compliance)
        var requiredWager = Math.Max(0, user.TotalDeposited - user.TotalWagered);
        if (requiredWager > 0)
        {
            return BadRequest(new
            {
                error = "Wager requirement not met",
                message = $"You must wager ${requiredWager:F2} more before you can withdraw.",
                requiredWager,
                totalWagered = user.TotalWagered,
                totalDeposited = user.TotalDeposited,
                progress = user.TotalDeposited > 0 ? (user.TotalWagered / user.TotalDeposited * 100m) : 0
            });
        }

        // Validate amount
        if (request.Amount < 0.001m)
        {
            return BadRequest(new { error = "Minimum withdrawal is 0.001 USDT" });
        }

        // Validate network
        if (!new[] { "POLYGON", "TRON", "BINANCE" }.Contains(request.Network?.ToUpper()))
        {
            return BadRequest(new { error = "Invalid network. Choose: POLYGON, TRON, or BINANCE" });
        }

        // Calculate fees based on network
        var feePercentage = decimal.Parse(_configuration["Blockchain:Fees:WithdrawalFeePercentage"] ?? "0.5");
        var minPlatformFee = 0.10m; // $0.10 minimum
        var platformFee = Math.Max(request.Amount * (feePercentage / 100m), minPlatformFee);
        
        // Network fees (approximate)
        var networkFee = request.Network?.ToUpper() switch
        {
            "POLYGON" => 0.01m,
            "TRON" => 0.001m,
            "BINANCE" => 0.05m,
            _ => 0.01m
        };
        
        var totalFee = platformFee + networkFee;
        var amountToSend = request.Amount - totalFee;

        if (user.AvailableBalance < request.Amount)
        {
            return BadRequest(new { error = "Insufficient balance" });
        }

        if (amountToSend <= 0)
        {
            return BadRequest(new { error = $"Amount too small to cover fees ({totalFee:F4} USDT)" });
        }

        // Create withdrawal transaction with network and fee info
        var transaction = Transaction.CreateWithdrawal(
            userId.Value,
            request.Amount,
            totalFee,
            request.DestinationAddress,
            request.Network?.ToUpper() ?? "POLYGON"
        );

        user.Withdraw(request.Amount);
        
        await _context.Transactions.AddAsync(transaction);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Withdrawal requested: {Amount} USDT (fee: {Fee}) to {Address} by user {UserId}",
            request.Amount,
            totalFee,
            request.DestinationAddress,
            userId
        );

        // Process withdrawal asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                // Send to user (amount - fees)
                var (userSuccess, userTxHash) = await _blockchainService.SendWithdrawal(
                    request.DestinationAddress,
                    amountToSend,
                    request.Network?.ToUpper() ?? "POLYGON"
                );

                if (!userSuccess)
                {
                    transaction.MarkAsFailed("Blockchain transaction failed");
                    user.Deposit(request.Amount); // Refund
                    await _context.SaveChangesAsync();
                    return;
                }

                // Send platform fee to your wallet
                var platformWallet = _configuration["Blockchain:Fees:PlatformFeeWallet"];
                if (!string.IsNullOrEmpty(platformWallet) && platformFee > 0)
                {
                    var (feeSuccess, feeTxHash) = await _blockchainService.SendWithdrawal(
                        platformWallet,
                        platformFee
                    );
                    
                    if (feeSuccess)
                    {
                        _logger.LogInformation(
                            "Platform fee collected: {Fee} USDT, TxHash: {TxHash}",
                            platformFee,
                            feeTxHash
                        );
                    }
                }

                transaction.MarkAsCompleted(userTxHash);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Withdrawal processing failed for transaction {TxId}", transaction.Id);
                transaction.MarkAsFailed(ex.Message);
                user.Deposit(request.Amount); // Refund
                await _context.SaveChangesAsync();
            }
        });

        return Ok(new
        {
            transactionId = transaction.Id,
            amount = request.Amount,
            platformFee,
            gasFee,
            totalFee,
            youReceive = amountToSend,
            status = "Processing"
        });
    }

    /// <summary>
    /// Get transaction statistics
    /// </summary>
    [HttpGet("transaction-stats")]
    public async Task<IActionResult> GetTransactionStats()
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var deposits = await _context.Transactions
            .Where(t => t.UserId == userId.Value && t.Type == TransactionType.Deposit && t.Status == TransactionStatus.Completed)
            .ToListAsync();

        var withdrawals = await _context.Transactions
            .Where(t => t.UserId == userId.Value && t.Type == TransactionType.Withdrawal && t.Status == TransactionStatus.Completed)
            .ToListAsync();

        var allTransactions = await _context.Transactions
            .Where(t => t.UserId == userId.Value && t.Status == TransactionStatus.Completed)
            .ToListAsync();

        var totalDeposits = deposits.Sum(t => t.Amount);
        var totalWithdrawals = withdrawals.Sum(t => t.Amount);
        var totalFees = allTransactions.Sum(t => t.Fee ?? 0);

        return Ok(new
        {
            totalDeposits,
            depositCount = deposits.Count,
            totalWithdrawals,
            withdrawalCount = withdrawals.Count,
            totalFees,
            feeCount = allTransactions.Count(t => (t.Fee ?? 0) > 0),
            netFlow = totalDeposits - totalWithdrawals
        });
    }

    /// <summary>
    /// Get transaction history
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] string? network = null)
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var query = _context.Transactions.Where(t => t.UserId == userId.Value);
        
        // Filter by type
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<TransactionType>(type, out var txType))
        {
            query = query.Where(t => t.Type == txType);
        }
        
        // Filter by status
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TransactionStatus>(status, out var txStatus))
        {
            query = query.Where(t => t.Status == txStatus);
        }
        
        // Filter by network
        if (!string.IsNullOrEmpty(network))
        {
            query = query.Where(t => t.Network == network);
        }

        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .Select(t => new
            {
                id = t.Id,
                type = t.Type.ToString(),
                network = t.Network,
                amount = t.Amount,
                fee = t.Fee ?? 0,
                status = t.Status.ToString(),
                blockchainTxHash = t.BlockchainTxHash,
                createdAt = t.CreatedAt
            })
            .ToListAsync();
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
    /// Verify deposit (manual)
    /// </summary>
    [HttpPost("verify-deposit")]
    public async Task<IActionResult> VerifyDeposit([FromBody] VerifyDepositRequest request)
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        // CHECK: Are deposits enabled?
        var platformSettings = await _context.Set<PlatformSettings>().FirstOrDefaultAsync();
        if (platformSettings != null && !platformSettings.DepositsEnabled)
        {
            return BadRequest(new
            {
                error = "Deposits are temporarily disabled",
                reason = platformSettings.DepositsDisabledReason
            });
        }

        var isValid = await _blockchainService.VerifyDeposit(request.TxHash, request.Amount);
        
        if (!isValid)
        {
            return BadRequest(new { error = "Invalid deposit transaction" });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        
        if (user == null)
        {
            return NotFound();
        }

        // Check if already processed
        var existingTx = await _context.Transactions
            .FirstOrDefaultAsync(t => t.BlockchainTxHash == request.TxHash);
        
        if (existingTx != null)
        {
            return BadRequest(new { error = "Transaction already processed" });
        }

        // Create deposit transaction
        var transaction = Transaction.CreateDeposit(userId.Value, request.Amount);
        transaction.SetBlockchainTxHash(request.TxHash);
        transaction.MarkAsCompleted(request.TxHash);
        
        user.Deposit(request.Amount);
        
        await _context.Transactions.AddAsync(transaction);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Deposit credited: {Amount} USDT for user {UserId}, TxHash: {TxHash}",
            request.Amount,
            userId,
            request.TxHash
        );

        return Ok(new
        {
            message = "Deposit verified and credited",
            amount = request.Amount,
            newBalance = user.AvailableBalance
        });
    }

    /// <summary>
    /// ADMIN: Fund test account (testnet only)
    /// </summary>
    [HttpPost("admin/fund-test-account")]
    public async Task<IActionResult> FundTestAccount([FromBody] FundTestAccountRequest request)
    {
        var useTestnet = bool.Parse(_configuration["Testing:UseTestnet"] ?? "false");
        
        if (!useTestnet)
        {
            return BadRequest(new { error = "Only available on testnet" });
        }

        var user = await _context.Users.FindAsync(request.UserId);
        
        if (user == null)
        {
            return NotFound();
        }

        var amount = request.Amount > 0 ? request.Amount : 100m;
        user.Deposit(amount);
        
        var transaction = Transaction.CreateDeposit(request.UserId, amount);
        transaction.MarkAsCompleted("TESTNET_AUTO_FUND");
        
        await _context.Transactions.AddAsync(transaction);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Test account funded: {Amount} USDT for user {UserId}", amount, request.UserId);

        return Ok(new
        {
            message = "Test account funded",
            amount,
            newBalance = user.AvailableBalance
        });
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userIdClaim))
        {
            return null;
        }

        return Guid.Parse(userIdClaim);
    }
}

public record WithdrawRequest(
    decimal Amount,
    string DestinationAddress,
    string? Network
);

public record VerifyDepositRequest(
    string TxHash,
    decimal Amount
);

public record FundTestAccountRequest(
    Guid UserId,
    decimal Amount = 100m
);
