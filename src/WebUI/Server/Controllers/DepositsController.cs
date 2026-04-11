using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DepositsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DepositsController> _logger;

    public DepositsController(
        ApplicationDbContext context,
        ILogger<DepositsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get user's pending deposits (waiting for confirmations)
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingDeposits()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var pending = await _context.Set<PendingDeposit>()
            .Where(d => d.UserId == userId.Value && !d.IsCredited)
            .OrderByDescending(d => d.DetectedAt)
            .Select(d => new
            {
                id = d.Id,
                amount = d.Amount,
                network = d.Network,
                txHash = d.TxHash,
                confirmations = d.Confirmations,
                requiredConfirmations = d.RequiredConfirmations,
                isConfirmed = d.IsConfirmed,
                isCredited = d.IsCredited,
                detectedAt = d.DetectedAt,
                estimatedCreditTime = d.DetectedAt.AddMinutes(GetEstimatedMinutes(d.Network, d.RequiredConfirmations)),
                explorerUrl = GetExplorerUrl(d.Network, d.TxHash)
            })
            .ToListAsync();

        return Ok(new
        {
            pendingDeposits = pending,
            total = pending.Count,
            totalAmount = pending.Sum(d => d.amount)
        });
    }

    /// <summary>
    /// Get deposit history
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetDepositHistory([FromQuery] int limit = 20)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var history = await _context.Set<PendingDeposit>()
            .Where(d => d.UserId == userId.Value && d.IsCredited)
            .OrderByDescending(d => d.CreditedAt)
            .Take(limit)
            .Select(d => new
            {
                id = d.Id,
                amount = d.Amount,
                network = d.Network,
                txHash = d.TxHash,
                detectedAt = d.DetectedAt,
                creditedAt = d.CreditedAt,
                timeToCredit = d.CreditedAt.HasValue 
                    ? (d.CreditedAt.Value - d.DetectedAt).TotalMinutes 
                    : 0,
                explorerUrl = GetExplorerUrl(d.Network, d.TxHash)
            })
            .ToListAsync();

        return Ok(new { deposits = history });
    }

    /// <summary>
    /// Get user's unique wallet addresses
    /// </summary>
    [HttpGet("wallets")]
    public async Task<IActionResult> GetUserWallets()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var wallets = await _context.Set<UserWallet>()
            .Where(w => w.UserId == userId.Value)
            .Select(w => new
            {
                network = w.Network,
                address = w.Address,
                totalDeposited = w.TotalDeposited,
                lastDepositAmount = w.LastDepositAmount,
                lastDepositAt = w.LastDepositAt,
                createdAt = w.CreatedAt,
                explorerUrl = GetExplorerUrl(w.Network, w.Address, isAddress: true)
            })
            .ToListAsync();

        return Ok(new { wallets });
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : null;
    }

    private string GetExplorerUrl(string network, string txHashOrAddress, bool isAddress = false)
    {
        var path = isAddress ? "address" : "tx";
        
        return network.ToUpper() switch
        {
            "POLYGON" => $"https://polygonscan.com/{path}/{txHashOrAddress}",
            "TRON" => isAddress 
                ? $"https://tronscan.org/#/address/{txHashOrAddress}"
                : $"https://tronscan.org/#/transaction/{txHashOrAddress}",
            "BINANCE" => $"https://bscscan.com/{path}/{txHashOrAddress}",
            _ => $"https://polygonscan.com/{path}/{txHashOrAddress}"
        };
    }

    private int GetEstimatedMinutes(string network, int confirmations)
    {
        var timePerBlock = network.ToUpper() switch
        {
            "POLYGON" => 2.1, // ~2.1 seconds per block
            "TRON" => 3.0,    // ~3 seconds per block
            "BINANCE" => 3.0, // ~3 seconds per block
            _ => 2.1
        };

        return (int)Math.Ceiling(confirmations * timePerBlock / 60.0);
    }
}
