using StackExchange.Redis;
using System.Text.Json;

namespace CryptoBet30.Infrastructure.Caching;

/// <summary>
/// Redis-based game state caching for high-speed access
/// Stores current round, betting pools, and active bets
/// </summary>
public class RedisGameStateService : IGameStateService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisGameStateService> _logger;
    private const string CurrentRoundKey = "game:current_round";
    private const string BettingPoolPrefix = "game:pool:";
    private const string UserBetsPrefix = "game:user_bets:";

    public RedisGameStateService(
        IConnectionMultiplexer redis,
        ILogger<RedisGameStateService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<GameRoundDto?> GetCurrentRound()
    {
        try
        {
            var json = await _db.StringGetAsync(CurrentRoundKey);
            
            if (json.IsNullOrEmpty)
                return null;
            
            return JsonSerializer.Deserialize<GameRoundDto>(json!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current round from Redis");
            return null;
        }
    }

    public async Task SetCurrentRound(GameRoundDto round)
    {
        try
        {
            var json = JsonSerializer.Serialize(round);
            await _db.StringSetAsync(CurrentRoundKey, json, TimeSpan.FromMinutes(1));
            
            _logger.LogDebug("Current round cached: {RoundId}", round.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching current round");
        }
    }

    public async Task<BettingPoolDto> GetBettingPool(Guid roundId)
    {
        try
        {
            var key = $"{BettingPoolPrefix}{roundId}";
            var json = await _db.StringGetAsync(key);
            
            if (json.IsNullOrEmpty)
            {
                return new BettingPoolDto
                {
                    RoundId = roundId,
                    TotalBetsHigher = 0,
                    TotalBetsLower = 0,
                    BetCount = 0
                };
            }
            
            return JsonSerializer.Deserialize<BettingPoolDto>(json!) 
                ?? new BettingPoolDto { RoundId = roundId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting betting pool for {RoundId}", roundId);
            return new BettingPoolDto { RoundId = roundId };
        }
    }

    public async Task UpdateBettingPool(Guid roundId, decimal amount, BetOutcome prediction)
    {
        try
        {
            var key = $"{BettingPoolPrefix}{roundId}";
            
            // Use Redis transaction for atomic update
            var tran = _db.CreateTransaction();
            
            #pragma warning disable CS4014
            if (prediction == BetOutcome.Higher)
            {
                tran.HashIncrementAsync(key, "TotalBetsHigher", (double)amount);
            }
            else
            {
                tran.HashIncrementAsync(key, "TotalBetsLower", (double)amount);
            }
            
            tran.HashIncrementAsync(key, "BetCount", 1);
            tran.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
            #pragma warning restore CS4014
            
            await tran.ExecuteAsync();
            
            _logger.LogDebug(
                "Updated betting pool {RoundId}: {Amount} on {Prediction}",
                roundId,
                amount,
                prediction
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating betting pool");
        }
    }

    public async Task<List<UserBetDto>> GetUserBets(Guid userId, Guid roundId)
    {
        try
        {
            var key = $"{UserBetsPrefix}{userId}:{roundId}";
            var json = await _db.StringGetAsync(key);
            
            if (json.IsNullOrEmpty)
                return new List<UserBetDto>();
            
            return JsonSerializer.Deserialize<List<UserBetDto>>(json!) 
                ?? new List<UserBetDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user bets");
            return new List<UserBetDto>();
        }
    }

    public async Task AddUserBet(Guid userId, Guid roundId, UserBetDto bet)
    {
        try
        {
            var key = $"{UserBetsPrefix}{userId}:{roundId}";
            var existingBets = await GetUserBets(userId, roundId);
            
            existingBets.Add(bet);
            
            var json = JsonSerializer.Serialize(existingBets);
            await _db.StringSetAsync(key, json, TimeSpan.FromMinutes(2));
            
            _logger.LogDebug("User bet cached: {UserId}, Round: {RoundId}", userId, roundId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching user bet");
        }
    }

    public async Task ClearRoundData(Guid roundId)
    {
        try
        {
            var poolKey = $"{BettingPoolPrefix}{roundId}";
            await _db.KeyDeleteAsync(poolKey);
            
            // Clear user bets for this round (requires pattern search)
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var pattern = $"{UserBetsPrefix}*:{roundId}";
            
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                await _db.KeyDeleteAsync(key);
            }
            
            _logger.LogInformation("Cleared Redis data for round {RoundId}", roundId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing round data");
        }
    }

    public async Task<long> GetActivePlayers()
    {
        try
        {
            // Count unique users who placed bets in last 5 minutes
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var pattern = $"{UserBetsPrefix}*";
            
            var count = 0L;
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                count++;
            }
            
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active players count");
            return 0;
        }
    }
}

public interface IGameStateService
{
    Task<GameRoundDto?> GetCurrentRound();
    Task SetCurrentRound(GameRoundDto round);
    Task<BettingPoolDto> GetBettingPool(Guid roundId);
    Task UpdateBettingPool(Guid roundId, decimal amount, BetOutcome prediction);
    Task<List<UserBetDto>> GetUserBets(Guid userId, Guid roundId);
    Task AddUserBet(Guid userId, Guid roundId, UserBetDto bet);
    Task ClearRoundData(Guid roundId);
    Task<long> GetActivePlayers();
}

// DTOs for Redis caching
public class GameRoundDto
{
    public Guid Id { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal OpenPrice { get; set; }
    public int OpenDigitSum { get; set; }
    public DateTime LockTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Phase { get; set; } = string.Empty;
}

public class BettingPoolDto
{
    public Guid RoundId { get; set; }
    public decimal TotalBetsHigher { get; set; }
    public decimal TotalBetsLower { get; set; }
    public int BetCount { get; set; }
}

public class UserBetDto
{
    public Guid BetId { get; set; }
    public decimal Amount { get; set; }
    public string Prediction { get; set; } = string.Empty;
    public DateTime PlacedAt { get; set; }
}
