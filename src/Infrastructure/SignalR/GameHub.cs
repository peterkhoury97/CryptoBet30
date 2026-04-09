using Microsoft.AspNetCore.SignalR;

namespace CryptoBet30.Infrastructure.SignalR;

/// <summary>
/// SignalR Hub for real-time game updates
/// Broadcasts price updates, round state, and betting pool changes
/// </summary>
public class GameHub : Hub
{
    private readonly ILogger<GameHub> _logger;
    private readonly IGameStateService _gameStateService;

    public GameHub(
        ILogger<GameHub> logger,
        IGameStateService gameStateService)
    {
        _logger = logger;
        _gameStateService = gameStateService;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        
        // Send current game state to new connection
        var currentRound = await _gameStateService.GetCurrentRound();
        
        if (currentRound != null)
        {
            await Clients.Caller.SendAsync("CurrentRound", currentRound);
            
            var pool = await _gameStateService.GetBettingPool(currentRound.Id);
            await Clients.Caller.SendAsync("BettingPool", pool);
        }
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}, Reason: {Exception}",
            Context.ConnectionId,
            exception?.Message
        );
        
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client subscribes to a specific game round
    /// </summary>
    public async Task JoinRound(Guid roundId)
    {
        var groupName = $"round_{roundId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogDebug(
            "Client {ConnectionId} joined round {RoundId}",
            Context.ConnectionId,
            roundId
        );
    }

    /// <summary>
    /// Client unsubscribes from a game round
    /// </summary>
    public async Task LeaveRound(Guid roundId)
    {
        var groupName = $"round_{roundId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogDebug(
            "Client {ConnectionId} left round {RoundId}",
            Context.ConnectionId,
            roundId
        );
    }

    /// <summary>
    /// Get current betting pool stats
    /// </summary>
    public async Task<BettingPoolDto> GetBettingPool(Guid roundId)
    {
        return await _gameStateService.GetBettingPool(roundId);
    }

    /// <summary>
    /// Ping for connection health check
    /// </summary>
    public Task Ping()
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Strongly-typed SignalR client interface
/// </summary>
public interface IGameClient
{
    // Round events
    Task RoundStarted(GameRoundStartedDto round);
    Task BettingLocked(BettingLockedDto data);
    Task RoundSettled(RoundSettledDto result);
    
    // Price updates
    Task PriceUpdate(PriceUpdateDto update);
    
    // Betting pool updates
    Task BettingPool(BettingPoolDto pool);
    
    // User-specific events
    Task BetPlaced(BetPlacedDto bet);
    Task BetSettled(BetSettledDto result);
    
    // System events
    Task Countdown(int secondsRemaining);
    Task Error(string message);
}

// SignalR DTOs
public class GameRoundStartedDto
{
    public Guid RoundId { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal OpenPrice { get; set; }
    public int OpenDigitSum { get; set; }
    public DateTime LockTime { get; set; }
    public DateTime EndTime { get; set; }
}

public class BettingLockedDto
{
    public Guid RoundId { get; set; }
    public decimal TotalBetsHigher { get; set; }
    public decimal TotalBetsLower { get; set; }
    public int TotalBets { get; set; }
}

public class RoundSettledDto
{
    public Guid RoundId { get; set; }
    public decimal ClosePrice { get; set; }
    public int CloseDigitSum { get; set; }
    public string Result { get; set; } = string.Empty;
    public int TotalBets { get; set; }
    public int TotalWinners { get; set; }
    public decimal TotalPool { get; set; }
}

public class PriceUpdateDto
{
    public string Asset { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int DigitSum { get; set; }
    public DateTime Timestamp { get; set; }
}

public class BetPlacedDto
{
    public Guid BetId { get; set; }
    public decimal Amount { get; set; }
    public string Prediction { get; set; } = string.Empty;
    public DateTime PlacedAt { get; set; }
}

public class BetSettledDto
{
    public Guid BetId { get; set; }
    public bool IsWin { get; set; }
    public decimal Payout { get; set; }
    public decimal NetProfit { get; set; }
}
