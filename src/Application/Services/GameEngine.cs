using MediatR;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.Application.Services;

/// <summary>
/// Core 30-second game engine
/// Manages game rounds, betting windows, and settlements
/// </summary>
public class GameEngine : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GameEngine> _logger;
    private readonly IHubContext<GameHub> _hubContext;
    private GameRound? _currentRound;
    private Timer? _roundTimer;

    public GameEngine(
        IServiceProvider serviceProvider,
        ILogger<GameEngine> logger,
        IHubContext<GameHub> hubContext)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game Engine started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            await StartNewRound();
            await Task.Delay(30000, stoppingToken); // 30 seconds per round
        }
    }

    private async Task StartNewRound()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var priceService = scope.ServiceProvider.GetRequiredService<IPriceService>();

        try
        {
            // Create new round
            _currentRound = GameRound.Create("BTC");
            
            // Get opening price
            var openPrice = await priceService.GetCurrentPrice("BTC");
            _currentRound.SetOpenPrice(openPrice);
            
            await dbContext.GameRounds.AddAsync(_currentRound);
            await dbContext.SaveChangesAsync();
            
            _logger.LogInformation(
                "Round {RoundId} started. Open price: {Price}, Digit sum: {Sum}",
                _currentRound.Id,
                openPrice,
                _currentRound.OpenPriceDigitSum
            );
            
            // Broadcast round start
            await _hubContext.Clients.All.SendAsync("RoundStarted", new
            {
                roundId = _currentRound.Id,
                asset = "BTC",
                openPrice = openPrice,
                openDigitSum = _currentRound.OpenPriceDigitSum,
                lockTime = _currentRound.LockTime,
                endTime = _currentRound.EndTime
            });
            
            // Schedule betting lock at 15 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(15000);
                await LockBetting();
            });
            
            // Schedule settlement at 30 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(30000);
                await SettleRound();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting new round");
        }
    }

    private async Task LockBetting()
    {
        if (_currentRound == null) return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _currentRound.LockBetting();
        await dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Round {RoundId} locked. Waiting for settlement.", _currentRound.Id);
        
        await _hubContext.Clients.All.SendAsync("BettingLocked", new
        {
            roundId = _currentRound.Id,
            totalBetsHigher = _currentRound.TotalBetsHigher,
            totalBetsLower = _currentRound.TotalBetsLower
        });
    }

    private async Task SettleRound()
    {
        if (_currentRound == null) return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var priceService = scope.ServiceProvider.GetRequiredService<IPriceService>();

        try
        {
            // Get closing price
            var closePrice = await priceService.GetCurrentPrice("BTC");
            _currentRound.Settle(closePrice);
            
            var result = _currentRound.Result!.Value;
            var totalPool = _currentRound.TotalBetsHigher + _currentRound.TotalBetsLower;
            var winningPool = result == BetOutcome.Higher 
                ? _currentRound.TotalBetsHigher 
                : _currentRound.TotalBetsLower;
            
            _logger.LogInformation(
                "Round {RoundId} settled. Close price: {Price}, Digit sum: {Sum}, Result: {Result}",
                _currentRound.Id,
                closePrice,
                _currentRound.ClosePriceDigitSum,
                result
            );
            
            // Settle all bets (fixed multiplier payouts)
            var bets = await dbContext.Bets
                .Include(b => b.User)
                .Where(b => b.GameRoundId == _currentRound.Id)
                .ToListAsync();
            
            foreach (var bet in bets)
            {
                // Settle with actual result
                bet.Settle(result);
                
                // Unlock balance
                bet.User.UnlockBalanceAfterBet(bet.Amount);
                
                if (bet.IsWin && bet.Payout.HasValue)
                {
                    // Credit winnings (Amount * Multiplier)
                    bet.User.CreditWinnings(bet.Payout.Value);
                    
                    // Process referral commission (5% of profit)
                    if (bet.User.ReferredByUserId.HasValue)
                    {
                        var referrer = await dbContext.Users.FindAsync(bet.User.ReferredByUserId.Value);
                        if (referrer != null)
                        {
                            var profit = bet.GetNetProfit();
                            var commission = profit * 0.05m; // 5% of profit
                            referrer.AddReferralEarnings(commission);
                        }
                    }
                }
            }
            
            await dbContext.SaveChangesAsync();
            
            // Broadcast settlement
            await _hubContext.Clients.All.SendAsync("RoundSettled", new
            {
                roundId = _currentRound.Id,
                closePrice = closePrice,
                closeDigitSum = _currentRound.ClosePriceDigitSum,
                result = result.ToString(),
                totalBets = bets.Count,
                totalWinners = bets.Count(b => b.IsWin),
                totalPool = totalPool
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error settling round {RoundId}", _currentRound.Id);
        }
    }

    public GameRound? GetCurrentRound() => _currentRound;
}

public interface IPriceService
{
    Task<decimal> GetCurrentPrice(string asset);
}
