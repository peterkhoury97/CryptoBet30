using System.Net.Http.Json;

namespace CryptoBet30.Infrastructure.Services;

/// <summary>
/// Real-time cryptocurrency price service
/// Fetches BTC/ETH prices from CoinGecko API
/// </summary>
public class CoinGeckoPriceService : IPriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CoinGeckoPriceService> _logger;
    private const string BaseUrl = "https://api.coingecko.com/api/v3";

    public CoinGeckoPriceService(
        HttpClient httpClient,
        ILogger<CoinGeckoPriceService> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _logger = logger;
    }

    public async Task<decimal> GetCurrentPrice(string asset)
    {
        try
        {
            var coinId = asset.ToLowerInvariant() switch
            {
                "btc" or "bitcoin" => "bitcoin",
                "eth" or "ethereum" => "ethereum",
                _ => throw new ArgumentException($"Unsupported asset: {asset}")
            };

            var response = await _httpClient.GetFromJsonAsync<CoinGeckoResponse>(
                $"/simple/price?ids={coinId}&vs_currencies=usd&precision=8"
            );

            if (response == null)
            {
                _logger.LogError("Empty response from CoinGecko for {Asset}", asset);
                throw new Exception("Failed to fetch price");
            }

            var price = coinId switch
            {
                "bitcoin" => response.Bitcoin?.Usd ?? 0,
                "ethereum" => response.Ethereum?.Usd ?? 0,
                _ => 0
            };

            if (price == 0)
            {
                _logger.LogError("Zero price returned for {Asset}", asset);
                throw new Exception("Invalid price data");
            }

            _logger.LogDebug("{Asset} price: ${Price}", asset, price);
            return price;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching price for {Asset}", asset);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching price for {Asset}", asset);
            throw;
        }
    }

    public async Task<Dictionary<string, decimal>> GetMultiplePrices(params string[] assets)
    {
        var prices = new Dictionary<string, decimal>();

        foreach (var asset in assets)
        {
            try
            {
                prices[asset] = await GetCurrentPrice(asset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get price for {Asset}", asset);
                prices[asset] = 0;
            }
        }

        return prices;
    }
}

public interface IPriceService
{
    Task<decimal> GetCurrentPrice(string asset);
    Task<Dictionary<string, decimal>> GetMultiplePrices(params string[] assets);
}

// CoinGecko API response models
public class CoinGeckoResponse
{
    public PriceData? Bitcoin { get; set; }
    public PriceData? Ethereum { get; set; }
}

public class PriceData
{
    public decimal Usd { get; set; }
}
