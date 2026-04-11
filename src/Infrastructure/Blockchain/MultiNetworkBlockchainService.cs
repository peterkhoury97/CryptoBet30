using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Util;

namespace CryptoBet30.Infrastructure.Blockchain;

/// <summary>
/// Multi-network blockchain service (Polygon, Tron, BSC, Arbitrum)
/// Routes operations to correct network handler
/// </summary>
public class MultiNetworkBlockchainService : IBlockchainService
{
    private readonly PolygonService _polygon;
    private readonly TronService _tron;
    private readonly BscService _bsc;
    private readonly ArbitrumService _arbitrum;
    private readonly ILogger<MultiNetworkBlockchainService> _logger;

    public MultiNetworkBlockchainService(
        PolygonService polygon,
        TronService tron,
        BscService bsc,
        ArbitrumService arbitrum,
        ILogger<MultiNetworkBlockchainService> logger)
    {
        _polygon = polygon;
        _tron = tron;
        _bsc = bsc;
        _arbitrum = arbitrum;
        _logger = logger;
    }

    public string GetDepositAddress() => _polygon.GetDepositAddress(); // Default to Polygon

    public async Task<string> GetUserDepositAddress(Guid userId)
    {
        // For MVP, same address across all networks
        // In production, generate unique addresses per user per network
        return _polygon.GetDepositAddress();
    }

    public async Task<decimal> GetBalance(string address) => await _polygon.GetBalance(address);

    public async Task<decimal> GetHotWalletBalance(string network = "POLYGON")
    {
        return network.ToUpper() switch
        {
            "POLYGON" => await _polygon.GetBalance(_polygon.GetDepositAddress()),
            "TRON" => await _tron.GetBalance(_tron.GetDepositAddress()),
            "BINANCE" => await _bsc.GetBalance(_bsc.GetDepositAddress()),
            "ARBITRUM" => await _arbitrum.GetBalance(_arbitrum.GetDepositAddress()),
            _ => throw new ArgumentException($"Unsupported network: {network}")
        };
    }

    public async Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount, string network = "POLYGON")
    {
        return network.ToUpper() switch
        {
            "POLYGON" => await _polygon.SendWithdrawal(destinationAddress, amount),
            "TRON" => await _tron.SendWithdrawal(destinationAddress, amount),
            "BINANCE" => await _bsc.SendWithdrawal(destinationAddress, amount),
            "ARBITRUM" => await _arbitrum.SendWithdrawal(destinationAddress, amount),
            _ => throw new ArgumentException($"Unsupported network: {network}")
        };
    }

    public async Task<bool> VerifyDeposit(string txHash, decimal expectedAmount, string network = "POLYGON")
    {
        return network.ToUpper() switch
        {
            "POLYGON" => await _polygon.VerifyDeposit(txHash, expectedAmount),
            "TRON" => await _tron.VerifyDeposit(txHash, expectedAmount),
            "BINANCE" => await _bsc.VerifyDeposit(txHash, expectedAmount),
            "ARBITRUM" => await _arbitrum.VerifyDeposit(txHash, expectedAmount),
            _ => throw new ArgumentException($"Unsupported network: {network}")
        };
    }

    public async Task<List<string>> GetPendingDeposits() => await _polygon.GetPendingDeposits();

    public async Task<decimal> EstimateWithdrawalFee(string network = "POLYGON")
    {
        return network.ToUpper() switch
        {
            "POLYGON" => 0.01m,
            "TRON" => 0.001m,
            "BINANCE" => 0.05m,
            "ARBITRUM" => 0.001m,
            _ => 0.01m
        };
    }
}

/// <summary>
/// Polygon network service
/// </summary>
public class PolygonService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PolygonService> _logger;
    private readonly Web3 _web3;
    private readonly Account _hotWallet;
    private readonly string _depositAddress;

    public PolygonService(IConfiguration configuration, ILogger<PolygonService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var rpcUrl = configuration["Blockchain:Polygon:RpcUrl"] ?? "https://polygon-rpc.com";
        var privateKey = configuration["Blockchain:HotWalletPrivateKey"]
            ?? throw new InvalidOperationException("Hot wallet private key not configured");
        
        _hotWallet = new Account(privateKey);
        _web3 = new Web3(_hotWallet, rpcUrl);
        _depositAddress = _hotWallet.Address;
        
        _logger.LogInformation("Polygon service initialized. Deposit: {Address}", _depositAddress);
    }

    public string GetDepositAddress() => _depositAddress;

    public async Task<decimal> GetBalance(string address)
    {
        try
        {
            var balance = await _web3.Eth.GetBalance.SendRequestAsync(address);
            return Web3.Convert.FromWei(balance.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Polygon balance for {Address}", address);
            return 0;
        }
    }

    public async Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount)
    {
        try
        {
            _logger.LogInformation("Polygon withdrawal: {Amount} to {Address}", amount, destinationAddress);

            var transaction = await _web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(destinationAddress, amount);
            
            _logger.LogInformation("Polygon withdrawal success: {TxHash}", transaction.TransactionHash);
            return (true, transaction.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Polygon withdrawal failed for {Address}", destinationAddress);
            return (false, string.Empty);
        }
    }

    public async Task<bool> VerifyDeposit(string txHash, decimal expectedAmount)
    {
        try
        {
            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            if (receipt == null || !receipt.Succeeded()) return false;

            var transaction = await _web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash);
            if (transaction == null) return false;

            if (!transaction.To.Equals(_depositAddress, StringComparison.OrdinalIgnoreCase)) return false;

            var actualAmount = Web3.Convert.FromWei(transaction.Value);
            if (actualAmount < expectedAmount * 0.99m) return false;

            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var confirmations = currentBlock.Value - receipt.BlockNumber.Value;
            
            return confirmations >= 12;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Polygon deposit {TxHash}", txHash);
            return false;
        }
    }

    public async Task<List<string>> GetPendingDeposits() => new List<string>();
}

/// <summary>
/// Tron network service (TRC-20 USDT)
/// </summary>
public class TronService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TronService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _depositAddress;
    private readonly string _privateKey;

    public TronService(IConfiguration configuration, ILogger<TronService> logger, HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
        
        _depositAddress = configuration["Blockchain:Tron:DepositAddress"]
            ?? throw new InvalidOperationException("Tron deposit address not configured");
        
        _privateKey = configuration["Blockchain:Tron:PrivateKey"]
            ?? throw new InvalidOperationException("Tron private key not configured");
        
        _logger.LogInformation("Tron service initialized. Deposit: {Address}", _depositAddress);
    }

    public string GetDepositAddress() => _depositAddress;

    public async Task<decimal> GetBalance(string address)
    {
        try
        {
            // Call Tron API to get TRX/USDT balance
            // For MVP, return 0
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Tron balance for {Address}", address);
            return 0;
        }
    }

    public async Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount)
    {
        try
        {
            _logger.LogInformation("Tron withdrawal: {Amount} to {Address}", amount, destinationAddress);

            // Use TronNet or TronSharp library to send TRC-20 USDT
            // For MVP, log and return mock success
            
            var mockTxHash = $"tron_{Guid.NewGuid():N}";
            _logger.LogWarning("MOCK: Tron withdrawal not implemented. Would send {Amount} to {Address}", amount, destinationAddress);
            
            return (true, mockTxHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tron withdrawal failed for {Address}", destinationAddress);
            return (false, string.Empty);
        }
    }

    public async Task<bool> VerifyDeposit(string txHash, decimal expectedAmount)
    {
        try
        {
            // Query Tronscan API or TronGrid to verify transaction
            // For MVP, return true
            _logger.LogWarning("MOCK: Tron deposit verification not implemented for {TxHash}", txHash);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Tron deposit {TxHash}", txHash);
            return false;
        }
    }

    public async Task<List<string>> GetPendingDeposits() => new List<string>();
}

/// <summary>
/// Binance Smart Chain service (BEP-20 USDT)
/// </summary>
public class BscService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BscService> _logger;
    private readonly Web3 _web3;
    private readonly Account _hotWallet;
    private readonly string _depositAddress;

    public BscService(IConfiguration configuration, ILogger<BscService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var rpcUrl = configuration["Blockchain:BSC:RpcUrl"] ?? "https://bsc-dataseed.binance.org";
        var privateKey = configuration["Blockchain:HotWalletPrivateKey"]
            ?? throw new InvalidOperationException("Hot wallet private key not configured");
        
        _hotWallet = new Account(privateKey);
        _web3 = new Web3(_hotWallet, rpcUrl);
        _depositAddress = _hotWallet.Address;
        
        _logger.LogInformation("BSC service initialized. Deposit: {Address}", _depositAddress);
    }

    public string GetDepositAddress() => _depositAddress;

    public async Task<decimal> GetBalance(string address)
    {
        try
        {
            var balance = await _web3.Eth.GetBalance.SendRequestAsync(address);
            return Web3.Convert.FromWei(balance.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting BSC balance for {Address}", address);
            return 0;
        }
    }

    public async Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount)
    {
        try
        {
            _logger.LogInformation("BSC withdrawal: {Amount} to {Address}", amount, destinationAddress);

            var transaction = await _web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(destinationAddress, amount);
            
            _logger.LogInformation("BSC withdrawal success: {TxHash}", transaction.TransactionHash);
            return (true, transaction.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BSC withdrawal failed for {Address}", destinationAddress);
            return (false, string.Empty);
        }
    }

    public async Task<bool> VerifyDeposit(string txHash, decimal expectedAmount)
    {
        try
        {
            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            if (receipt == null || !receipt.Succeeded()) return false;

            var transaction = await _web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash);
            if (transaction == null) return false;

            if (!transaction.To.Equals(_depositAddress, StringComparison.OrdinalIgnoreCase)) return false;

            var actualAmount = Web3.Convert.FromWei(transaction.Value);
            if (actualAmount < expectedAmount * 0.99m) return false;

            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var confirmations = currentBlock.Value - receipt.BlockNumber.Value;
            
            return confirmations >= 15;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying BSC deposit {TxHash}", txHash);
            return false;
        }
    }

    public async Task<List<string>> GetPendingDeposits() => new List<string>();
}

/// <summary>
/// Arbitrum One service (cheapest fees - $0.001)
/// </summary>
public class ArbitrumService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArbitrumService> _logger;
    private readonly Web3 _web3;
    private readonly Account _hotWallet;
    private readonly string _depositAddress;

    public ArbitrumService(IConfiguration configuration, ILogger<ArbitrumService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var rpcUrl = configuration["Blockchain:Arbitrum:RpcUrl"] ?? "https://arb1.arbitrum.io/rpc";
        var privateKey = configuration["Blockchain:HotWalletPrivateKey"]
            ?? throw new InvalidOperationException("Hot wallet private key not configured");
        
        _hotWallet = new Account(privateKey);
        _web3 = new Web3(_hotWallet, rpcUrl);
        _depositAddress = _hotWallet.Address;
        
        _logger.LogInformation("Arbitrum service initialized. Deposit: {Address}", _depositAddress);
    }

    public string GetDepositAddress() => _depositAddress;

    public async Task<decimal> GetBalance(string address)
    {
        try
        {
            var balance = await _web3.Eth.GetBalance.SendRequestAsync(address);
            return Web3.Convert.FromWei(balance.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Arbitrum balance for {Address}", address);
            return 0;
        }
    }

    public async Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount)
    {
        try
        {
            _logger.LogInformation("Arbitrum withdrawal: {Amount} to {Address}", amount, destinationAddress);

            var transaction = await _web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(destinationAddress, amount);
            
            _logger.LogInformation("Arbitrum withdrawal success: {TxHash}", transaction.TransactionHash);
            return (true, transaction.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arbitrum withdrawal failed for {Address}", destinationAddress);
            return (false, string.Empty);
        }
    }

    public async Task<bool> VerifyDeposit(string txHash, decimal expectedAmount)
    {
        try
        {
            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            if (receipt == null) return false;

            var transaction = await _web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash);
            if (transaction == null) return false;

            if (!transaction.To.Equals(_depositAddress, StringComparison.OrdinalIgnoreCase)) return false;

            var actualAmount = Web3.Convert.FromWei(transaction.Value);
            if (actualAmount < expectedAmount * 0.99m) return false;

            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var confirmations = currentBlock.Value - receipt.BlockNumber.Value;
            
            return confirmations >= 10; // Arbitrum: 10 confirmations
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Arbitrum deposit {TxHash}", txHash);
            return false;
        }
    }

    public async Task<List<string>> GetPendingDeposits() => new List<string>();
}

public interface IBlockchainService
{
    string GetDepositAddress();
    Task<string> GetUserDepositAddress(Guid userId);
    Task<decimal> GetBalance(string address);
    Task<decimal> GetHotWalletBalance(string network = "POLYGON");
    Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount, string network = "POLYGON");
    Task<bool> VerifyDeposit(string txHash, decimal expectedAmount, string network = "POLYGON");
    Task<List<string>> GetPendingDeposits();
    Task<decimal> EstimateWithdrawalFee(string network = "POLYGON");
}
