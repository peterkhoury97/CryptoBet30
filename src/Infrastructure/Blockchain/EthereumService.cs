using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;

namespace CryptoBet30.Infrastructure.Blockchain;

/// <summary>
/// Ethereum/Polygon blockchain service using Nethereum
/// Handles deposits, withdrawals, and transaction monitoring
/// </summary>
public class EthereumService : IBlockchainService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EthereumService> _logger;
    private readonly Web3 _web3;
    private readonly Account _hotWallet;
    private readonly string _depositAddress;

    public EthereumService(IConfiguration configuration, ILogger<EthereumService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var rpcUrl = configuration["Blockchain:RpcUrl"] 
            ?? "https://polygon-rpc.com"; // Default to Polygon
        
        var privateKey = configuration["Blockchain:HotWalletPrivateKey"]
            ?? throw new InvalidOperationException("Hot wallet private key not configured");
        
        _hotWallet = new Account(privateKey);
        _web3 = new Web3(_hotWallet, rpcUrl);
        _depositAddress = _hotWallet.Address;
        
        _logger.LogInformation("Ethereum service initialized. Hot wallet: {Address}", _depositAddress);
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
            _logger.LogError(ex, "Error getting balance for {Address}", address);
            return 0;
        }
    }

    public async Task<(bool Success, string TxHash)> SendWithdrawal(
        string destinationAddress, 
        decimal amount)
    {
        try
        {
            _logger.LogInformation(
                "Processing withdrawal: {Amount} to {Address}", 
                amount, 
                destinationAddress
            );

            // Convert amount to Wei
            var amountInWei = Web3.Convert.ToWei(amount);
            
            // Get current gas price
            var gasPrice = await _web3.Eth.GasPrice.SendRequestAsync();
            
            // Estimate gas
            var gas = new HexBigInteger(21000); // Standard transfer
            
            // Create transaction
            var transaction = await _web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(
                    destinationAddress,
                    amount,
                    gasPriceGwei: null, // Use network gas price
                    gas: gas
                );
            
            _logger.LogInformation(
                "Withdrawal successful. TxHash: {TxHash}, Gas used: {Gas}",
                transaction.TransactionHash,
                transaction.GasUsed
            );
            
            return (true, transaction.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Withdrawal failed for {Address}", destinationAddress);
            return (false, string.Empty);
        }
    }

    public async Task<bool> VerifyDeposit(string txHash, decimal expectedAmount)
    {
        try
        {
            var receipt = await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            
            if (receipt == null || !receipt.Succeeded())
            {
                _logger.LogWarning("Transaction {TxHash} not found or failed", txHash);
                return false;
            }
            
            var transaction = await _web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash);
            
            if (transaction == null)
            {
                _logger.LogWarning("Transaction {TxHash} not found", txHash);
                return false;
            }
            
            // Verify destination is our deposit address
            if (!transaction.To.Equals(_depositAddress, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Transaction {TxHash} destination mismatch. Expected: {Expected}, Got: {Got}",
                    txHash,
                    _depositAddress,
                    transaction.To
                );
                return false;
            }
            
            // Verify amount
            var actualAmount = Web3.Convert.FromWei(transaction.Value);
            
            if (actualAmount < expectedAmount * 0.99m) // Allow 1% tolerance for rounding
            {
                _logger.LogWarning(
                    "Transaction {TxHash} amount mismatch. Expected: {Expected}, Got: {Got}",
                    txHash,
                    expectedAmount,
                    actualAmount
                );
                return false;
            }
            
            // Verify confirmations (wait for at least 12 confirmations on Polygon)
            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var confirmations = currentBlock.Value - receipt.BlockNumber.Value;
            
            if (confirmations < 12)
            {
                _logger.LogInformation(
                    "Transaction {TxHash} pending confirmations: {Confirmations}/12",
                    txHash,
                    confirmations
                );
                return false;
            }
            
            _logger.LogInformation(
                "Deposit verified: {TxHash}, Amount: {Amount}, Confirmations: {Confirmations}",
                txHash,
                actualAmount,
                confirmations
            );
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying deposit {TxHash}", txHash);
            return false;
        }
    }

    public async Task<List<string>> GetPendingDeposits()
    {
        try
        {
            var currentBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var fromBlock = new BlockParameter(currentBlock.Value - 1000); // Last ~30 minutes on Polygon
            var toBlock = BlockParameter.CreateLatest();
            
            var filter = await _web3.Eth.Filters.NewFilterInput.SendRequestAsync(
                new NewFilterInput
                {
                    FromBlock = fromBlock,
                    ToBlock = toBlock,
                    Address = new[] { _depositAddress }
                }
            );
            
            // Get all transactions to our deposit address
            // Note: This is simplified - in production, use event logs or indexing service
            
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for deposits");
            return new List<string>();
        }
    }

    public async Task<decimal> EstimateWithdrawalFee()
    {
        try
        {
            var gasPrice = await _web3.Eth.GasPrice.SendRequestAsync();
            var gas = 21000; // Standard transfer
            
            var feeInWei = gasPrice.Value * gas;
            var feeInEth = Web3.Convert.FromWei(feeInWei);
            
            return feeInEth;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error estimating withdrawal fee");
            return 0.001m; // Default fallback
        }
    }
}

public interface IBlockchainService
{
    string GetDepositAddress();
    Task<decimal> GetBalance(string address);
    Task<(bool Success, string TxHash)> SendWithdrawal(string destinationAddress, decimal amount);
    Task<bool> VerifyDeposit(string txHash, decimal expectedAmount);
    Task<List<string>> GetPendingDeposits();
    Task<decimal> EstimateWithdrawalFee();
}
