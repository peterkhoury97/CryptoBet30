using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Application.Commands.Auth;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.WebUI.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IMediator mediator, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// MetaMask wallet authentication
    /// </summary>
    [HttpPost("wallet")]
    [AllowAnonymous]
    public async Task<IActionResult> AuthenticateWallet([FromBody] WalletAuthRequest request)
    {
        var command = new AuthenticateWalletCommand(
            request.WalletAddress,
            request.Signature,
            request.Message,
            request.ReferralCode
        );

        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        // Get user's deposit addresses (if new user, they were just created)
        var depositAddresses = await GetUserDepositAddresses(result.UserId!.Value);

        return Ok(new
        {
            token = result.Token,
            userId = result.UserId,
            depositAddresses,
            isNewUser = result.IsNewUser
        });
    }

    /// <summary>
    /// Get nonce for wallet signature
    /// </summary>
    [HttpGet("wallet/nonce")]
    [AllowAnonymous]
    public IActionResult GetNonce([FromQuery] string walletAddress)
    {
        // Generate random nonce for MetaMask signing
        var nonce = Guid.NewGuid().ToString();
        var message = $"Sign this message to authenticate with CryptoBet30.\n\nNonce: {nonce}";
        
        return Ok(new { message, nonce });
    }

    /// <summary>
    /// Email/password registration
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var command = new RegisterEmailCommand(
            request.Email,
            request.Password,
            request.Username,
            request.ReferralCode
        );

        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        // Get user's deposit addresses (just created)
        var depositAddresses = await GetUserDepositAddresses(result.UserId!.Value);

        return Ok(new
        {
            token = result.Token,
            userId = result.UserId,
            depositAddresses,
            message = "Registration successful! Your deposit addresses are ready."
        });
    }

    /// <summary>
    /// Email/password login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var command = new LoginEmailCommand(request.Email, request.Password);
        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            return Unauthorized(new { error = result.Error });
        }

        return Ok(new
        {
            token = result.Token,
            userId = result.UserId
        });
    }

    /// <summary>
    /// Get current user info
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // TODO: Implement user query handler
        return Ok(new { userId });
    }

    private async Task<Dictionary<string, string>> GetUserDepositAddresses(Guid userId)
    {
        using var scope = HttpContext.RequestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var wallets = await context.Set<UserWallet>()
            .Where(w => w.UserId == userId)
            .ToListAsync();

        return wallets.ToDictionary(
            w => w.Network.ToLower(),
            w => w.Address
        );
    }
}

// Request DTOs
public record WalletAuthRequest(
    string WalletAddress,
    string Signature,
    string Message,
    Guid? ReferralCode = null
);

public record RegisterRequest(
    string Email,
    string Password,
    string Username,
    Guid? ReferralCode = null
);

public record LoginRequest(
    string Email,
    string Password
);
