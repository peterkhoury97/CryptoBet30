using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.Infrastructure.Services;

/// <summary>
/// JWT token generation for authenticated sessions
/// </summary>
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtTokenGenerator> _logger;

    public JwtTokenGenerator(IConfiguration configuration, ILogger<JwtTokenGenerator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string GenerateToken(User user)
    {
        var secretKey = _configuration["Security:Jwt:SecretKey"] 
            ?? throw new InvalidOperationException("JWT secret key not configured");
        
        var issuer = _configuration["Security:Jwt:Issuer"] ?? "CryptoBet30";
        var audience = _configuration["Security:Jwt:Audience"] ?? "CryptoBet30.Client";
        var expiryMinutes = int.Parse(_configuration["Security:Jwt:ExpiryMinutes"] ?? "60");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("auth_type", user.AuthType.ToString()),
            new Claim("is_admin", user.IsAdmin.ToString().ToLower())
        };

        if (!string.IsNullOrEmpty(user.WalletAddress))
        {
            claims.Add(new Claim("wallet_address", user.WalletAddress));
        }

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        if (!string.IsNullOrEmpty(user.Username))
        {
            claims.Add(new Claim("username", user.Username));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        
        _logger.LogDebug("JWT token generated for user {UserId}", user.Id);
        
        return tokenString;
    }
}
