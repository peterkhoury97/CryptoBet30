using MediatR;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.Application.Commands.Game;

public record PlaceBetCommand(
    Guid UserId,
    decimal Amount,
    BetOutcome Prediction
) : IRequest<PlaceBetResult>;

public record PlaceBetResult(
    bool Success,
    Guid? BetId = null,
    decimal? PotentialPayout = null,
    string? Error = null
);

public class PlaceBetHandler : IRequestHandler<PlaceBetCommand, PlaceBetResult>
{
    private readonly ApplicationDbContext _context;
    private readonly IGameStateService _gameStateService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaceBetHandler> _logger;

    public PlaceBetHandler(
        ApplicationDbContext context,
        IGameStateService gameStateService,
        IConfiguration configuration,
        ILogger<PlaceBetHandler> logger)
    {
        _context = context;
        _gameStateService = gameStateService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PlaceBetResult> Handle(PlaceBetCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get current round
            var currentRound = await _gameStateService.GetCurrentRound();
            
            if (currentRound == null)
            {
                return new PlaceBetResult(false, Error: "No active round");
            }

            if (currentRound.Phase != "Betting")
            {
                return new PlaceBetResult(false, Error: "Betting is locked");
            }

            // Validate bet amount
            var minBet = decimal.Parse(_configuration["Game:MinimumBet"] ?? "0.01");
            var maxBet = decimal.Parse(_configuration["Game:MaximumBet"] ?? "100");
            
            if (request.Amount < minBet)
            {
                return new PlaceBetResult(false, Error: $"Minimum bet is {minBet} USDT");
            }
            
            if (request.Amount > maxBet)
            {
                return new PlaceBetResult(false, Error: $"Maximum bet is {maxBet} USDT");
            }

            // Get user
            var user = await _context.Users.FindAsync(request.UserId, cancellationToken);
            
            if (user == null)
            {
                return new PlaceBetResult(false, Error: "User not found");
            }

            // Check balance
            if (user.AvailableBalance < request.Amount)
            {
                return new PlaceBetResult(false, Error: "Insufficient balance");
            }

            // Get multiplier from config
            var multiplierKey = request.Prediction == BetOutcome.Higher ? "HigherWin" : "LowerWin";
            var multiplier = decimal.Parse(_configuration[$"Game:Multipliers:{multiplierKey}"] ?? "1.95");

            // Lock balance
            user.LockBalanceForBet(request.Amount);

            // Create bet
            var bet = Bet.Create(
                request.UserId,
                currentRound.Id,
                request.Amount,
                request.Prediction,
                multiplier
            );

            await _context.Bets.AddAsync(bet, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Update game state cache
            await _gameStateService.UpdateBettingPool(
                currentRound.Id,
                request.Amount,
                request.Prediction
            );

            await _gameStateService.AddUserBet(
                request.UserId,
                currentRound.Id,
                new UserBetDto
                {
                    BetId = bet.Id,
                    Amount = request.Amount,
                    Prediction = request.Prediction.ToString(),
                    PlacedAt = bet.PlacedAt
                }
            );

            var potentialPayout = request.Amount * multiplier;

            _logger.LogInformation(
                "Bet placed: User {UserId}, Amount {Amount}, Prediction {Prediction}, Multiplier {Multiplier}x",
                request.UserId,
                request.Amount,
                request.Prediction,
                multiplier
            );

            return new PlaceBetResult(
                true,
                bet.Id,
                potentialPayout
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing bet for user {UserId}", request.UserId);
            return new PlaceBetResult(false, Error: "Failed to place bet");
        }
    }
}
