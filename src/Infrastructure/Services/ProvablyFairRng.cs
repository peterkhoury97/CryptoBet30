using System.Security.Cryptography;
using System.Text;

namespace CryptoBet30.Infrastructure.Services;

/// <summary>
/// Provably fair random number generator
/// Used for game outcomes to ensure transparency and prevent manipulation
/// </summary>
public class ProvablyFairRng
{
    /// <summary>
    /// Generate cryptographic random seed
    /// </summary>
    public static string GenerateSeed()
    {
        var bytes = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Hash seed with SHA-256 (for public commitment)
    /// </summary>
    public static string HashSeed(string seed)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(seed);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Combine server seed + client seed + nonce for additional randomness
    /// </summary>
    public static string CombineSeeds(string serverSeed, string clientSeed, int nonce)
    {
        var combined = $"{serverSeed}:{clientSeed}:{nonce}";
        return HashSeed(combined);
    }

    /// <summary>
    /// Calculate outcome from seed hash
    /// Returns true for Higher, false for Lower
    /// </summary>
    public static bool CalculateOutcome(string seedHash)
    {
        // Use first 8 bytes of hash as number
        var hashBytes = Convert.FromBase64String(seedHash);
        var number = BitConverter.ToUInt64(hashBytes, 0);
        
        // 50/50 split: even = Higher, odd = Lower
        return number % 2 == 0;
    }

    /// <summary>
    /// Calculate outcome with house edge adjustment
    /// Returns Higher/Lower but slightly favors the house
    /// </summary>
    public static BetOutcome CalculateOutcomeWithEdge(string seedHash, decimal houseEdgePercent = 2.5m)
    {
        var hashBytes = Convert.FromBase64String(seedHash);
        var number = BitConverter.ToUInt64(hashBytes, 0);
        
        // Map to 0-10000 range (0.01% precision)
        var result = (number % 10000) / 100.0m;
        
        // 50/50 base, but house edge shifts threshold
        var higherThreshold = 50m - (houseEdgePercent / 2m);
        
        return result < higherThreshold ? BetOutcome.Higher : BetOutcome.Lower;
    }

    /// <summary>
    /// Generate outcome as digit (0-9) for digit sum comparison
    /// </summary>
    public static int GenerateDigitSum(string seedHash)
    {
        var hashBytes = Convert.FromBase64String(seedHash);
        var number = BitConverter.ToUInt64(hashBytes, 0);
        
        // Map to 0-99 range (like digit sum of BTC price)
        return (int)(number % 100);
    }

    /// <summary>
    /// Verify seed matches hash (for players to check fairness)
    /// </summary>
    public static bool VerifySeed(string seed, string expectedHash)
    {
        var actualHash = HashSeed(seed);
        return actualHash == expectedHash;
    }

    /// <summary>
    /// Generate client seed from browser (user-side randomness)
    /// </summary>
    public static string GenerateClientSeed()
    {
        // In production, this should come from client browser
        // For now, generate server-side as fallback
        return GenerateSeed().Substring(0, 16);
    }
}
