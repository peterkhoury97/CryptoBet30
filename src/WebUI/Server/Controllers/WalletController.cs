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
    /// Get deposit address
    /// </summary>
    [HttpGet("deposit-address")]
    public IActionResult GetDepositAddress()
    {
        var address = _blockchainService.GetDepositAddress();
        
        return Ok(new
        {
            address,
            chain = "Polygon",
            minDeposit = 0.01m,
            confirmations = 12,
            note = "Send USDT (Polygon network) to this address"
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

        var user = await _context.Users.FindAsync(userId.Value);
        
        if (user == null)
        {
            return NotFound();
        }

        // Validate amount
        if (request.Amount < 0.001m)
        {
            return BadRequest(new { error = "Minimum withdrawal is 0.001 USDT" });
        }

        // Calculate fees
        var feePercentage = decimal.Parse(_configuration["Blockchain:Fees:WithdrawalFeePercentage"] ?? "0.5");
        var minFee = decimal.Parse(_configuration["Blockchain:Fees:MinimumWithdrawalFee"] ?? "0.001");
        var platformFee = Math.Max(request.Amount * (feePercentage / 100m), minFee);
        var gasFee = await _blockchainService.EstimateWithdrawalFee();
        var totalFee = platformFee + gasFee;
        var amountToSend = request.Amount - totalFee;

        if (user.AvailableBalance < request.Amount)
        {
            return BadRequest(new { error = "Insufficient balance" });
        }

        if (amountToSend <= 0)
        {
            return BadRequest(new { error = $"Amount too small to cover fees ({totalFee:F4} USDT)" });
        }

        // Create withdrawal transaction
        var transaction = Transaction.CreateWithdrawal(
            userId.Value,
            request.Amount,
            request.DestinationAddress
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
                    amountToSend
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
    /// Get transaction history
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null)
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

        var transactions = await query
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
    string DestinationAddress
);

public record VerifyDepositRequest(
    string TxHash,
    decimal Amount
);

public record FundTestAccountRequest(
    Guid UserId,
    decimal Amount = 100m
);
