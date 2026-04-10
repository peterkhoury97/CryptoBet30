using CryptoBet30.Infrastructure.Security;

namespace CryptoBet30.WebUI.Server.Middleware;

/// <summary>
/// Middleware to detect and block fraudulent requests
/// </summary>
public class FraudDetectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FraudDetectionMiddleware> _logger;

    public FraudDetectionMiddleware(
        RequestDelegate next,
        ILogger<FraudDetectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IFraudDetectionService fraudService)
    {
        // Only check registration and sensitive endpoints
        var path = context.Request.Path.Value?.ToLower() ?? "";
        var shouldCheck = path.Contains("/api/auth/register") || 
                         path.Contains("/api/auth/wallet") ||
                         path.Contains("/api/checkin/claim");

        if (shouldCheck && context.Request.Method == "POST")
        {
            var ipAddress = GetClientIp(context);
            var deviceFingerprint = context.Request.Headers["X-Device-Fingerprint"].ToString();
            var userAgent = context.Request.Headers["User-Agent"].ToString();

            // Check if IP or device is banned
            if (await fraudService.IsIpBanned(ipAddress))
            {
                _logger.LogWarning("Blocked request from banned IP: {IP}", ipAddress);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Access denied" });
                return;
            }

            if (!string.IsNullOrEmpty(deviceFingerprint) && await fraudService.IsDeviceBanned(deviceFingerprint))
            {
                _logger.LogWarning("Blocked request from banned device: {Fingerprint}", deviceFingerprint);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Access denied" });
                return;
            }

            // For registration endpoints, do fraud check
            if (path.Contains("/api/auth/register") || path.Contains("/api/auth/wallet"))
            {
                var fraudCheck = await fraudService.CheckRegistration(ipAddress, deviceFingerprint, userAgent);
                
                if (!fraudCheck.IsAllowed)
                {
                    _logger.LogWarning(
                        "Registration blocked from IP {IP}: {Reason}",
                        ipAddress,
                        fraudCheck.Reason
                    );
                    context.Response.StatusCode = 429; // Too Many Requests
                    await context.Response.WriteAsJsonAsync(new 
                    { 
                        error = "Registration limit reached. Please try again later or contact support.",
                        reason = fraudCheck.Reason
                    });
                    return;
                }

                if (fraudCheck.IsFlagged)
                {
                    _logger.LogWarning(
                        "Suspicious registration from IP {IP}: {Reason} (Risk: {Score})",
                        ipAddress,
                        fraudCheck.Reason,
                        fraudCheck.RiskScore
                    );
                    // Allow but log for manual review
                }
            }
        }

        await _next(context);
    }

    private string GetClientIp(HttpContext context)
    {
        // Try to get real IP from headers (CloudFlare, nginx, etc.)
        var cfConnectingIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(cfConnectingIp))
            return cfConnectingIp;

        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xForwardedFor))
            return xForwardedFor.Split(',')[0].Trim();

        var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xRealIp))
            return xRealIp;

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

public static class FraudDetectionMiddlewareExtensions
{
    public static IApplicationBuilder UseFraudDetection(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<FraudDetectionMiddleware>();
    }
}
