using Microsoft.EntityFrameworkCore;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();
    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<DiceRoll> DiceRolls => Set<DiceRoll>();
    public DbSet<ReferralEarning> ReferralEarnings => Set<ReferralEarning>();
    public DbSet<DailyCheckIn> DailyCheckIns => Set<DailyCheckIn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.WalletAddress).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            
            entity.Property(e => e.WalletAddress)
                .IsRequired()
                .HasMaxLength(42);
            
            entity.Property(e => e.Email)
                .HasMaxLength(255);
            
            entity.Property(e => e.AvailableBalance)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.LockedBetBalance)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.BonusBalance)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.TotalReferralEarnings)
                .HasPrecision(18, 8);
            
            // Self-referencing relationship
            entity.HasOne(e => e.ReferredBy)
                .WithMany()
                .HasForeignKey(e => e.ReferredByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // GameRound Configuration
        modelBuilder.Entity<GameRound>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.StartTime, e.Phase });
            entity.HasIndex(e => e.Asset);
            
            entity.Property(e => e.Asset)
                .IsRequired()
                .HasMaxLength(10);
            
            entity.Property(e => e.OpenPrice)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.ClosePrice)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.TotalBetsHigher)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.TotalBetsLower)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.Phase)
                .HasConversion<string>();
            
            entity.Property(e => e.Result)
                .HasConversion<string>();
        });

        // Bet Configuration
        modelBuilder.Entity<Bet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.PlacedAt });
            entity.HasIndex(e => new { e.GameRoundId, e.Prediction });
            
            entity.Property(e => e.Amount)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.Payout)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.HouseEdgePercentage)
                .HasPrecision(5, 2);
            
            entity.Property(e => e.Prediction)
                .HasConversion<string>();
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.Bets)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.GameRound)
                .WithMany(g => g.Bets)
                .HasForeignKey(e => e.GameRoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Transaction Configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.BlockchainTxHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => new { e.Status, e.Type });
            
            entity.Property(e => e.Amount)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.Type)
                .HasConversion<string>();
            
            entity.Property(e => e.Status)
                .HasConversion<string>();
            
            entity.Property(e => e.BlockchainTxHash)
                .HasMaxLength(66);
            
            entity.Property(e => e.DestinationAddress)
                .HasMaxLength(42);
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.Transactions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Referral Configuration
        modelBuilder.Entity<Referral>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ReferrerId, e.CreatedAt });
            
            entity.Property(e => e.CommissionRate)
                .HasPrecision(5, 2);
            
            entity.Property(e => e.TotalEarned)
                .HasPrecision(18, 8);
        });

        // ReferralEarning Configuration
        modelBuilder.Entity<ReferralEarning>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ReferrerId, e.CreatedAt });
            entity.HasIndex(e => e.ReferredUserId);
            
            entity.Property(e => e.Amount)
                .HasPrecision(18, 8);
            
            entity.Property(e => e.Source)
                .HasMaxLength(50);
            
            entity.HasOne(e => e.Referrer)
                .WithMany(u => u.ReferralEarningsReceived)
                .HasForeignKey(e => e.ReferrerId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.ReferredUser)
                .WithMany(u => u.ReferralEarningsGiven)
                .HasForeignKey(e => e.ReferredUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DailyCheckIn Configuration
        modelBuilder.Entity<DailyCheckIn>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.CheckInDate }).IsUnique();
            
            entity.Property(e => e.RewardAmount)
                .HasPrecision(18, 8);
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.CheckIns)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
