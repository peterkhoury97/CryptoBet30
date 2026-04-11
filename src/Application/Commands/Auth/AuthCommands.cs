using MediatR;

namespace CryptoBet30.Application.Commands.Auth;

// MetaMask wallet authentication
public record AuthenticateWalletCommand(
    string WalletAddress,
    string Signature,
    string Message,
    Guid? ReferralCode = null
) : IRequest<AuthenticationResult>;

// Email/password registration
public record RegisterEmailCommand(
    string Email,
    string Password,
    string Username,
    Guid? ReferralCode = null
) : IRequest<AuthenticationResult>;

// Email/password login
public record LoginEmailCommand(
    string Email,
    string Password
) : IRequest<AuthenticationResult>;

// Link wallet to existing account
public record LinkWalletCommand(
    Guid UserId,
    string WalletAddress,
    string Signature,
    string Message
) : IRequest<bool>;

// Verify email
public record VerifyEmailCommand(
    Guid UserId,
    string VerificationCode
) : IRequest<bool>;

// Authentication result
public record AuthenticationResult(
    bool Success,
    string? Token,
    Guid? UserId,
    string? Error = null,
    bool IsNewUser = false
);
