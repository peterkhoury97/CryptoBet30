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
    private readonly ILogger<WalletController> _logger;

    public WalletController(
        IMediator mediator,
        IBlockchainService blockchainService,
        ApplicationDbContext context,
        ILogger<WalletController> logger)
    {
        _mediator = mediator;
        _blockchainService = blockchainService;
        _context = context;
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
            confirmations = 12
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

        if (user.AvailableBalance < request.Amount)
        {
            return BadRequest(new { error = "Insufficient balance" });
        }

        // Estimate fee
        var estimatedFee = await _blockchainService.EstimateWithdrawalFee();
        
        if (user.AvailableBalance < request.Amount + estimatedFee)
        {
            return BadRequest(new { error = $"Insufficient balance to cover fee ({estimatedFee} MATIC)" });
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
            "Withdrawal requested: {Amount} USDT to {Address} by user {UserId}",
            request.Amount,
            request.DestinationAddress,
            userId
        );

        // Process withdrawal asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                var (success, txHash) = await _blockchainService.SendWithdrawal(
                    request.DestinationAddress,
                    request.Amount
                );

                if (success)
                {
                    transaction.MarkAsCompleted(txHash);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    transaction.MarkAsFailed("Blockchain transaction failed");
                    user.Deposit(request.Amount); // Refund
                    await _context.SaveChangesAsync();
                }
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
            estimatedFee = estimatedFee,
            status = "Processing"
        });
    }

    /// <summary>
    /// Get transaction history
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions([FromQuery] int limit = 20)
    {
        var userId = GetUserId();
        
        if (userId == null)
        {
            return Unauthorized();
        }

        var transactions = await _context.Transactions
            .Where(t => t.UserId == userId.Value)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .Select(t => new
            {
                t.Id,
                t.Type,
                t.Amount,
                t.Status,
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
