using System.Security.Cryptography;
using System.Text;
using Nethereum.HdWallet;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Microsoft.EntityFrameworkCore;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.Infrastructure.Blockchain;

/// <summary>
/// HD Wallet service - generates unique deposit addresses for each user
/// Uses BIP44 derivation: m/44'/60'/0'/0/{index}
/// </summary>
public interface IWalletGenerationService
{
    Task<UserWallet> GenerateUserWallet(Guid userId, string network);
    Task<string> GetUserDepositAddress(Guid userId, string network);
    string DecryptPrivateKey(string encryptedKey);
    string EncryptPrivateKey(string privateKey);
}

public class WalletGenerationService : IWalletGenerationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WalletGenerationService> _logger;
    private readonly Wallet _masterWallet;
    private readonly byte[] _encryptionKey;

    public WalletGenerationService(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<WalletGenerationService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;

        // Get master seed/mnemonic
        var mnemonic = configuration["Blockchain:MasterMnemonic"];
        if (string.IsNullOrEmpty(mnemonic))
        {
            throw new InvalidOperationException("Master mnemonic not configured. Generate one with: Nethereum.HdWallet");
        }

        _masterWallet = new Wallet(mnemonic, null);
        
        // Encryption key for storing child private keys
        var encKeyString = configuration["Security:WalletEncryptionKey"] 
            ?? configuration["Security:EncryptionKey"]
            ?? throw new InvalidOperationException("Wallet encryption key not configured");
        
        _encryptionKey = Convert.FromBase64String(encKeyString);

        _logger.LogInformation("HD Wallet service initialized with master wallet");
    }

    public async Task<UserWallet> GenerateUserWallet(Guid userId, string network)
    {
        network = network.ToUpper();

        // Check if wallet already exists
        var existing = await _context.Set<UserWallet>()
            .FirstOrDefaultAsync(w => w.UserId == userId && w.Network == network);

        if (existing != null)
        {
            return existing;
        }

        // Get next derivation index for this network
        var lastIndex = await _context.Set<UserWallet>()
            .Where(w => w.Network == network)
            .OrderByDescending(w => w.DerivationIndex)
            .Select(w => w.DerivationIndex)
            .FirstOrDefaultAsync();

        var newIndex = lastIndex + 1;

        // Derive child account: m/44'/60'/0'/0/{index}
        var account = _masterWallet.GetAccount(newIndex);
        var address = account.Address;
        var privateKey = account.PrivateKey;

        // Encrypt private key before storing
        var encryptedPrivateKey = EncryptPrivateKey(privateKey);

        var userWallet = UserWallet.Create(
            userId,
            network,
            address,
            encryptedPrivateKey,
            newIndex
        );

        await _context.Set<UserWallet>().AddAsync(userWallet);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Generated wallet for user {UserId} on {Network}: {Address} (index {Index})",
            userId,
            network,
            address,
            newIndex
        );

        return userWallet;
    }

    public async Task<string> GetUserDepositAddress(Guid userId, string network)
    {
        var wallet = await _context.Set<UserWallet>()
            .FirstOrDefaultAsync(w => w.UserId == userId && w.Network == network.ToUpper());

        if (wallet == null)
        {
            // Auto-generate if doesn't exist
            wallet = await GenerateUserWallet(userId, network);
        }

        return wallet.Address;
    }

    public string EncryptPrivateKey(string privateKey)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(privateKey);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string DecryptPrivateKey(string encryptedKey)
    {
        var fullCipher = Convert.FromBase64String(encryptedKey);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        // Extract IV from first 16 bytes
        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        // Extract ciphertext
        var cipherBytes = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
