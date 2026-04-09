using Nethereum.Signer;
using Nethereum.Util;

namespace CryptoBet30.Infrastructure.Services;

/// <summary>
/// Verifies MetaMask/WalletConnect signatures for wallet authentication
/// </summary>
public class EthereumSignatureVerifier : IWalletSignatureVerifier
{
    private readonly ILogger<EthereumSignatureVerifier> _logger;

    public EthereumSignatureVerifier(ILogger<EthereumSignatureVerifier> logger)
    {
        _logger = logger;
    }

    public Task<bool> VerifySignature(string walletAddress, string message, string signature)
    {
        try
        {
            // Nethereum signature verification
            var signer = new EthereumMessageSigner();
            var recoveredAddress = signer.EncodeUTF8AndEcRecover(message, signature);
            
            var isValid = recoveredAddress.Equals(walletAddress, StringComparison.OrdinalIgnoreCase);
            
            if (!isValid)
            {
                _logger.LogWarning(
                    "Signature verification failed. Expected: {Expected}, Got: {Recovered}",
                    walletAddress,
                    recoveredAddress
                );
            }
            
            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature for {WalletAddress}", walletAddress);
            return Task.FromResult(false);
        }
    }
}
