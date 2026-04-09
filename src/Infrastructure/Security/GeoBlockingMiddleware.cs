using MaxMind.GeoIP2;
using System.Net;

namespace CryptoBet30.Infrastructure.Security;

/// <summary>
/// Middleware to block restricted countries
/// </summary>
public class GeoBlockingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GeoBlockingMiddleware> _logger;
    private readonly IConfiguration _configuration;
    
    // Countries where online gambling is heavily restricted or requires license
    private static readonly HashSet<string> BlockedCountries = new()
    {
        // North America
        "US", // United States - Federal + state laws
        "CA", // Canada - Provincial restrictions
        
        // Europe (strict licensing)
        "GB", // United Kingdom - UK Gambling Commission required
        "FR", // France - ARJEL license required
        "DE", // Germany - Strict state gambling laws
        "IT", // Italy - ADM license required
        "ES", // Spain - DGOJ license required
        "NL", // Netherlands - KSA license required
        "BE", // Belgium - Gaming Commission license
        "DK", // Denmark - Spillemyndigheden license
        "SE", // Sweden - Spelinspektionen license
        "NO", // Norway - State monopoly
        "FI", // Finland - Veikkaus monopoly
        "PL", // Poland - Ministry of Finance license
        "GR", // Greece - Hellenic Gaming Commission
        "PT", // Portugal - SRIJ license
        "CZ", // Czech Republic - Ministry of Finance
        "RO", // Romania - ONJN license
        "BG", // Bulgaria - State Commission on Gambling
        "HR", // Croatia - Ministry of Finance
        "HU", // Hungary - State Tax Authority
        
        // Oceania
        "AU", // Australia - Strict Interactive Gambling Act
        "NZ", // New Zealand - Gambling Act restrictions
        
        // Asia (strict/banned)
        "CN", // China - All gambling banned
        "KP", // North Korea - Banned
        "SG", // Singapore - Remote Gambling Act
        "KR", // South Korea - Banned except lottery
        "MY", // Malaysia - Sharia law restrictions
        "TH", // Thailand - Gambling Act restrictions
        "VN", // Vietnam - Criminal Code restrictions
        "PH", // Philippines - PAGCOR license required
        "ID", // Indonesia - Islamic law ban
        
        // Middle East (religious restrictions)
        "SA", // Saudi Arabia - Islamic law ban
        "AE", // UAE - Islamic law restrictions
        "QA", // Qatar - Islamic law ban
        "KW", // Kuwait - Islamic law ban
        "BH", // Bahrain - Islamic law restrictions
        "OM", // Oman - Islamic law ban
        "IQ", // Iraq - Islamic law ban
        "IR", // Iran - Islamic law ban
        "AF", // Afghanistan - Islamic law ban
        "PK", // Pakistan - Islamic law restrictions
        
        // Africa
        "ZA", // South Africa - National Gambling Act
        
        // Others
        "CU", // Cuba - US sanctions
        "SY", // Syria - US sanctions
        "KH"  // Cambodia - Gambling ban
    };

    public GeoBlockingMiddleware(
        RequestDelegate next,
        ILogger<GeoBlockingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip blocking for certain paths
        var path = context.Request.Path.Value?.ToLower();
        if (path == "/blocked" || 
            path == "/legal" || 
            path == "/terms" ||
            path?.StartsWith("/api/fairness/") == true ||
            path?.StartsWith("/_framework") == true ||
            path?.StartsWith("/css") == true ||
            path?.StartsWith("/js") == true)
        {
            await _next(context);
            return;
        }

        // Get user's country from IP
        var country = GetCountryCode(context);
        
        if (!string.IsNullOrEmpty(country) && BlockedCountries.Contains(country))
        {
            _logger.LogWarning(
                "Blocked access from restricted country: {Country}, IP: {IP}",
                country,
                GetClientIp(context)
            );
            
            context.Response.StatusCode = 451; // Unavailable For Legal Reasons
            context.Response.Redirect("/blocked");
            return;
        }

        await _next(context);
    }

    private string? GetCountryCode(HttpContext context)
    {
        try
        {
            // Try to use MaxMind GeoIP2 database
            var geoIpPath = _configuration["GeoIP:DatabasePath"];
            
            if (string.IsNullOrEmpty(geoIpPath) || !File.Exists(geoIpPath))
            {
                // Fallback: Use Cloudflare header if behind Cloudflare
                if (context.Request.Headers.TryGetValue("CF-IPCountry", out var cfCountry))
                {
                    return cfCountry.ToString().ToUpper();
                }
                
                // Development mode: Allow all
                return null;
            }

            using var reader = new DatabaseReader(geoIpPath);
            var ip = GetClientIp(context);
            
            if (IPAddress.TryParse(ip, out var ipAddress))
            {
                var response = reader.Country(ipAddress);
                return response.Country.IsoCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get country code for IP");
        }

        return null;
    }

    private string GetClientIp(HttpContext context)
    {
        // Check for Cloudflare header
        if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
        {
            return cfIp.ToString();
        }
        
        // Check for X-Forwarded-For (proxy/load balancer)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
        {
            var ip = xff.ToString().Split(',')[0].Trim();
            return ip;
        }
        
        // Direct connection
        return context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
    }
}
