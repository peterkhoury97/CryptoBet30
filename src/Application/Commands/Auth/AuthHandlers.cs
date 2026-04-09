using MediatR;
using Microsoft.Extensions.Logging;
using CryptoBet30.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

namespace CryptoBet30.Application.Commands.Auth;

public class AuthenticateWalletHandler : IRequestHandler<AuthenticateWalletCommand, AuthenticationResult>
{
    private readonly ApplicationDbContext _context;
    private readonly IWalletSignatureVerifier _signatureVerifier;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly ILogger<AuthenticateWalletHandler> _logger;

    public AuthenticateWalletHandler(
        ApplicationDbContext context,
        IWalletSignatureVerifier signatureVerifier,
        IJwtTokenGenerator tokenGenerator,
        ILogger<AuthenticateWalletHandler> logger)
    {
        _context = context;
        _signatureVerifier = signatureVerifier;
        _tokenGenerator = tokenGenerator;
        _logger = logger;
    }

    public async Task<AuthenticationResult> Handle(
        AuthenticateWalletCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Verify signature
            var isValidSignature = await _signatureVerifier.VerifySignature(
                request.WalletAddress,
                request.Message,
                request.Signature
            );

            if (!isValidSignature)
            {
                return new AuthenticationResult(false, null, null, "Invalid signature");
            }

            // Find or create user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.WalletAddress == request.WalletAddress.ToLowerInvariant(), cancellationToken);

            if (user == null)
            {
                // New user - register
                user = User.CreateWithWallet(request.WalletAddress, request.ReferralCode);
                await _context.Users.AddAsync(user, cancellationToken);
                
                _logger.LogInformation("New wallet user registered: {WalletAddress}", request.WalletAddress);
            }
            else
            {
                // Existing user - update last login
                user.UpdateLastLogin();
                _logger.LogInformation("Wallet user logged in: {WalletAddress}", request.WalletAddress);
            }

            await _context.SaveChangesAsync(cancellationToken);

            // Generate JWT token
            var token = _tokenGenerator.GenerateToken(user);

            return new AuthenticationResult(true, token, user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating wallet: {WalletAddress}", request.WalletAddress);
            return new AuthenticationResult(false, null, null, "Authentication failed");
        }
    }
}

public class RegisterEmailHandler : IRequestHandler<RegisterEmailCommand, AuthenticationResult>
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IEmailService _emailService;
    private readonly ILogger<RegisterEmailHandler> _logger;

    public RegisterEmailHandler(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IEmailService emailService,
        ILogger<RegisterEmailHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<AuthenticationResult> Handle(
        RegisterEmailCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if email already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email.ToLowerInvariant(), cancellationToken);

            if (existingUser != null)
            {
                return new AuthenticationResult(false, null, null, "Email already registered");
            }

            // Validate password strength
            if (request.Password.Length < 8)
            {
                return new AuthenticationResult(false, null, null, "Password must be at least 8 characters");
            }

            // Hash password
            var passwordHash = _passwordHasher.HashPassword(request.Password);

            // Create user
            var user = User.CreateWithEmail(
                request.Email,
                passwordHash,
                request.Username,
                request.ReferralCode
            );

            await _context.Users.AddAsync(user, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Send verification email
            await _emailService.SendVerificationEmail(user.Email!, user.Id);

            _logger.LogInformation("New email user registered: {Email}", request.Email);

            // Generate JWT token
            var token = _tokenGenerator.GenerateToken(user);

            return new AuthenticationResult(true, token, user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering email user: {Email}", request.Email);
            return new AuthenticationResult(false, null, null, "Registration failed");
        }
    }
}

public class LoginEmailHandler : IRequestHandler<LoginEmailCommand, AuthenticationResult>
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly ILogger<LoginEmailHandler> _logger;

    public LoginEmailHandler(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        ILogger<LoginEmailHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _logger = logger;
    }

    public async Task<AuthenticationResult> Handle(
        LoginEmailCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find user by email
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email.ToLowerInvariant(), cancellationToken);

            if (user == null)
            {
                return new AuthenticationResult(false, null, null, "Invalid email or password");
            }

            // Verify password
            var isValidPassword = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash!);

            if (!isValidPassword)
            {
                return new AuthenticationResult(false, null, null, "Invalid email or password");
            }

            // Update last login
            user.UpdateLastLogin();
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Email user logged in: {Email}", request.Email);

            // Generate JWT token
            var token = _tokenGenerator.GenerateToken(user);

            return new AuthenticationResult(true, token, user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging in email user: {Email}", request.Email);
            return new AuthenticationResult(false, null, null, "Login failed");
        }
    }
}

// Service interfaces
public interface IWalletSignatureVerifier
{
    Task<bool> VerifySignature(string walletAddress, string message, string signature);
}

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public interface IJwtTokenGenerator
{
    string GenerateToken(User user);
}

public interface IEmailService
{
    Task SendVerificationEmail(string email, Guid userId);
}
