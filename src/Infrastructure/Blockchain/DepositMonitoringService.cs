using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using CryptoBet30.Infrastructure.Persistence;
using CryptoBet30.Domain.Entities;

namespace CryptoBet30.Infrastructure.Blockchain;

/// <summary>
/// Background service that monitors blockchain for deposits to user wallets
/// Runs every 30 seconds, checks all user addresses for new transactions
/// </summary>
public class DepositMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DepositMonitoringService> _logger;
    private readonly IConfiguration _configuration;

    public DepositMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<DepositMonitoringService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deposit Monitoring Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorDepositsAsync();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in deposit monitoring loop");
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); // Wait longer on error
            }
        }

        _logger.LogInformation("Deposit Monitoring Service stopped");
    }

    private async Task MonitorDepositsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Monitor each network
        await MonitorNetwork("POLYGON", context);
        await MonitorNetwork("ARBITRUM", context);
        // await MonitorNetwork("TRON", context); // Enable when Tron is ready
        // await MonitorNetwork("BINANCE", context); // Enable when BSC is ready
    }

    private async Task MonitorNetwork(string network, ApplicationDbContext context)
    {
        try
        {
            // Get all user wallets for this network
            var userWallets = await context.Set<UserWallet>()
                .Where(w => w.Network == network)
                .ToListAsync();

            if (!userWallets.Any())
            {
                return; // No wallets to monitor
            }

            // Connect to blockchain
            var rpcUrl = network switch
            {
                "POLYGON" => _configuration["Blockchain:Polygon:RpcUrl"] ?? "https://polygon-rpc.com",
                "TRON" => _configuration["Blockchain:Tron:RpcUrl"],
                "BINANCE" => _configuration["Blockchain:Binance:RpcUrl"] ?? "https://bsc-dataseed.binance.org",
                "ARBITRUM" => _configuration["Blockchain:Arbitrum:RpcUrl"] ?? "https://arb1.arbitrum.io/rpc",
                _ => throw new ArgumentException($"Unknown network: {network}")
            };

            var web3 = new Web3(rpcUrl);
            var usdtContractAddress = GetUsdtContractAddress(network);

            var requiredConfirmations = network switch
            {
                "POLYGON" => 12,
                "TRON" => 19,
                "BINANCE" => 15,
                "ARBITRUM" => 10,
                _ => 12
            };

            // Check each wallet
            foreach (var wallet in userWallets)
            {
                try
                {
                    await CheckWalletForDeposits(wallet, web3, usdtContractAddress, requiredConfirmations, context);
                    wallet.UpdateLastChecked();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking wallet {Address} on {Network}", wallet.Address, network);
                }
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring network {Network}", network);
        }
    }

    private async Task CheckWalletForDeposits(
        UserWallet wallet,
        Web3 web3,
        string usdtContractAddress,
        int requiredConfirmations,
        ApplicationDbContext context)
    {
        // Get latest block
        var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        var latestBlockNumber = (long)latestBlock.Value;

        // Check USDT balance (ERC-20)
        var contract = web3.Eth.GetContract(ERC20_ABI, usdtContractAddress);
        var balanceFunction = contract.GetFunction("balanceOf");
        var balance = await balanceFunction.CallAsync<System.Numerics.BigInteger>(wallet.Address);

        if (balance > 0)
        {
            // Convert from wei to USDT (6 decimals for USDT)
            var amount = (decimal)balance / 1_000_000m;

            _logger.LogInformation(
                "Detected deposit: {Amount} USDT to {Address} (User {UserId})",
                amount,
                wallet.Address,
                wallet.UserId
            );

            // Get transaction details
            var transferEvents = await GetTransferEvents(web3, usdtContractAddress, wallet.Address, latestBlockNumber);

            foreach (var transfer in transferEvents)
            {
                // Check if already processed
                var existing = await context.Set<PendingDeposit>()
                    .FirstOrDefaultAsync(d => d.TxHash == transfer.TxHash);

                if (existing == null)
                {
                    // New deposit detected
                    var pending = PendingDeposit.Create(
                        wallet.UserId,
                        wallet.Network,
                        transfer.From,
                        wallet.Address,
                        transfer.TxHash,
                        transfer.Amount,
                        requiredConfirmations
                    );

                    await context.Set<PendingDeposit>().AddAsync(pending);
                    
                    _logger.LogInformation(
                        "Created pending deposit: {Amount} USDT, TX: {TxHash}",
                        transfer.Amount,
                        transfer.TxHash
                    );
                }
                else
                {
                    // Update confirmations
                    var confirmations = latestBlockNumber - transfer.BlockNumber;
                    existing.UpdateConfirmations((int)confirmations);

                    if (existing.IsConfirmed && !existing.IsCredited)
                    {
                        // Credit user
                        await CreditUserDeposit(existing, wallet, context);
                    }

                    if (existing.IsCredited && !existing.IsSwept)
                    {
                        // Sweep funds to hot wallet
                        await SweepFundsToHotWallet(wallet, existing, context);
                    }
                }
            }
        }
    }

    private async Task CreditUserDeposit(PendingDeposit deposit, UserWallet wallet, ApplicationDbContext context)
    {
        var user = await context.Users.FindAsync(deposit.UserId);
        if (user == null) return;

        // Credit user balance
        user.Deposit(deposit.Amount);
        user.RecordDeposit(deposit.Amount);

        // Create transaction record
        var transaction = Transaction.CreateDeposit(
            deposit.UserId,
            deposit.Amount,
            deposit.TxHash,
            deposit.Network
        );
        transaction.MarkAsCompleted(deposit.TxHash);

        await context.Transactions.AddAsync(transaction);

        // Update wallet stats
        wallet.RecordDeposit(deposit.Amount, deposit.TxHash);

        // Mark as credited
        deposit.MarkAsCredited();

        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Credited {Amount} USDT to user {UserId} from deposit {TxHash}",
            deposit.Amount,
            deposit.UserId,
            deposit.TxHash
        );
    }

    private async Task SweepFundsToHotWallet(UserWallet wallet, PendingDeposit deposit, ApplicationDbContext context)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var walletService = scope.ServiceProvider.GetRequiredService<IWalletGenerationService>();

            // Decrypt user wallet private key
            var privateKey = walletService.DecryptPrivateKey(wallet.EncryptedPrivateKey);

            // Get hot wallet address
            var hotWalletAddress = _configuration["Blockchain:HotWalletAddress"]
                ?? throw new InvalidOperationException("Hot wallet address not configured");

            // Send funds to hot wallet
            var web3 = new Web3(new Nethereum.Web3.Accounts.Account(privateKey), GetRpcUrl(wallet.Network));
            var usdtContract = GetUsdtContractAddress(wallet.Network);

            var contract = web3.Eth.GetContract(ERC20_ABI, usdtContract);
            var transferFunction = contract.GetFunction("transfer");

            var amountWei = (System.Numerics.BigInteger)(deposit.Amount * 1_000_000m);
            var txHash = await transferFunction.SendTransactionAsync(
                wallet.Address,
                hotWalletAddress,
                amountWei
            );

            deposit.MarkAsSwept(txHash);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Swept {Amount} USDT from {From} to hot wallet, TX: {TxHash}",
                deposit.Amount,
                wallet.Address,
                txHash
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sweep funds from wallet {Address}", wallet.Address);
        }
    }

    private async Task<List<TransferEvent>> GetTransferEvents(
        Web3 web3,
        string contractAddress,
        string toAddress,
        long latestBlock)
    {
        var events = new List<TransferEvent>();

        try
        {
            // Get Transfer events for last 100 blocks
            var fromBlock = Math.Max(0, latestBlock - 100);
            
            var contract = web3.Eth.GetContract(ERC20_ABI, contractAddress);
            var transferEvent = contract.GetEvent("Transfer");

            var filter = transferEvent.CreateFilterInput(
                new BlockParameter((ulong)fromBlock),
                new BlockParameter((ulong)latestBlock)
            );

            var logs = await transferEvent.GetAllChangesAsync(filter);

            foreach (var log in logs)
            {
                var decoded = log.Event as dynamic;
                var to = decoded.To?.ToString();

                if (to?.Equals(toAddress, StringComparison.OrdinalIgnoreCase) == true)
                {
                    var from = decoded.From?.ToString();
                    var value = decoded.Value;
                    var amount = (decimal)value / 1_000_000m;

                    events.Add(new TransferEvent
                    {
                        From = from,
                        To = to,
                        Amount = amount,
                        TxHash = log.Log.TransactionHash,
                        BlockNumber = (long)log.Log.BlockNumber.Value
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transfer events");
        }

        return events;
    }

    private string GetUsdtContractAddress(string network)
    {
        return network switch
        {
            "POLYGON" => "0xc2132D05D31c914a87C6611C10748AEb04B58e8F", // USDT on Polygon
            "BINANCE" => "0x55d398326f99059fF775485246999027B3197955", // USDT on BSC
            "ARBITRUM" => "0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9", // USDT on Arbitrum
            "TRON" => "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", // USDT on Tron
            _ => throw new ArgumentException($"Unknown network: {network}")
        };
    }

    private string GetRpcUrl(string network)
    {
        return network switch
        {
            "POLYGON" => _configuration["Blockchain:Polygon:RpcUrl"] ?? "https://polygon-rpc.com",
            "BINANCE" => _configuration["Blockchain:Binance:RpcUrl"] ?? "https://bsc-dataseed.binance.org",
            "ARBITRUM" => _configuration["Blockchain:Arbitrum:RpcUrl"] ?? "https://arb1.arbitrum.io/rpc",
            _ => throw new ArgumentException($"Unknown network: {network}")
        };
    }

    private const string ERC20_ABI = @"[{""constant"":true,""inputs"":[{""name"":""_owner"",""type"":""address""}],""name"":""balanceOf"",""outputs"":[{""name"":""balance"",""type"":""uint256""}],""type"":""function""},{""constant"":false,""inputs"":[{""name"":""_to"",""type"":""address""},{""name"":""_value"",""type"":""uint256""}],""name"":""transfer"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""},{""anonymous"":false,""inputs"":[{""indexed"":true,""name"":""from"",""type"":""address""},{""indexed"":true,""name"":""to"",""type"":""address""},{""indexed"":false,""name"":""value"",""type"":""uint256""}],""name"":""Transfer"",""type"":""event""}]";
}

public class TransferEvent
{
    public string From { get; set; }
    public string To { get; set; }
    public decimal Amount { get; set; }
    public string TxHash { get; set; }
    public long BlockNumber { get; set; }
}
