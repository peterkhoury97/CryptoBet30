using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CryptoBet30.Infrastructure.Security;

/// <summary>
/// AES-256 encryption for API requests/responses
/// Prevents users from inspecting network traffic in browser DevTools
/// </summary>
public class ApiEncryptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiEncryptionMiddleware> _logger;
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public ApiEncryptionMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiEncryptionMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;

        // Get encryption key from config (32 bytes for AES-256)
        var keyString = configuration["Security:EncryptionKey"] 
            ?? "CHANGE_THIS_TO_32_BYTE_KEY_IN_PRODUCTION_NOW!";
        _key = Encoding.UTF8.GetBytes(keyString.PadRight(32).Substring(0, 32));
        
        // Get IV from config (16 bytes)
        var ivString = configuration["Security:EncryptionIV"] 
            ?? "CHANGE_THIS_16B!";
        _iv = Encoding.UTF8.GetBytes(ivString.PadRight(16).Substring(0, 16));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only encrypt sensitive endpoints
        if (ShouldEncrypt(context.Request.Path))
        {
            // Decrypt incoming request
            if (context.Request.Method == "POST" || context.Request.Method == "PUT")
            {
                await DecryptRequest(context);
            }

            // Capture and encrypt outgoing response
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            await EncryptResponse(context, originalBodyStream);
        }
        else
        {
            await _next(context);
        }
    }

    private bool ShouldEncrypt(PathString path)
    {
        // Encrypt these sensitive endpoints
        var encryptPaths = new[]
        {
            "/api/game/bet",
            "/api/wallet/withdraw",
            "/api/wallet/balance",
            "/api/game/multipliers",
            "/api/admin/"
        };

        return encryptPaths.Any(p => path.StartsWithSegments(p));
    }

    private async Task DecryptRequest(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering();
            
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var encryptedBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (string.IsNullOrEmpty(encryptedBody))
                return;

            // Check if request has encryption header
            if (!context.Request.Headers.ContainsKey("X-Encrypted"))
                return;

            var decryptedJson = Decrypt(encryptedBody);
            
            var decryptedBytes = Encoding.UTF8.GetBytes(decryptedJson);
            context.Request.Body = new MemoryStream(decryptedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt request");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid encrypted request");
        }
    }

    private async Task EncryptResponse(HttpContext context, Stream originalBodyStream)
    {
        try
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            if (context.Response.StatusCode == 200 && !string.IsNullOrEmpty(responseBody))
            {
                var encryptedResponse = Encrypt(responseBody);
                
                // Add encryption header
                context.Response.Headers.Add("X-Encrypted", "true");
                context.Response.ContentType = "text/plain";
                
                var encryptedBytes = Encoding.UTF8.GetBytes(encryptedResponse);
                await originalBodyStream.WriteAsync(encryptedBytes);
            }
            else
            {
                // Don't encrypt error responses
                await context.Response.Body.CopyToAsync(originalBodyStream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt response");
            await originalBodyStream.WriteAsync(Encoding.UTF8.GetBytes("Encryption error"));
        }
    }

    private string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        
        return Convert.ToBase64String(encryptedBytes);
    }

    private string Decrypt(string cipherText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(cipherText);
        var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        
        return Encoding.UTF8.GetString(decryptedBytes);
    }
}

/// <summary>
/// Extension method to add encryption middleware
/// </summary>
public static class ApiEncryptionMiddlewareExtensions
{
    public static IApplicationBuilder UseApiEncryption(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiEncryptionMiddleware>();
    }
}
