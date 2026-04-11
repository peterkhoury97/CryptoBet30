# HD Wallet Setup Guide - Professional Deposit System

## Overview

Every user gets a **unique deposit address** per network (Polygon, Tron, BSC).

**Benefits:**
- ✅ No manual TX hash verification needed
- ✅ Automatic deposit detection
- ✅ Professional UX (like Stake.com, Binance)
- ✅ Multi-network support
- ✅ Automatic sweeping to hot wallet

---

## Step 1: Generate Master Mnemonic (ONE TIME)

**On your LOCAL machine** (NOT the server):

```bash
# Install Nethereum tools
dotnet tool install -g Nethereum.Generator

# Generate mnemonic (12 words)
dotnet run --project tools/MnemonicGenerator.cs
```

**Or use Node.js:**
```bash
npm install bip39
node -e "const bip39 = require('bip39'); console.log(bip39.generateMnemonic());"
```

**You'll get something like:**
```
witch collapse practice feed shame open despair creek road again ice least
```

**⚠️ CRITICAL SECURITY:**
- Write these 12 words on paper (3 copies)
- Store in safe/vault
- **NEVER** commit to GitHub
- **NEVER** share with anyone
- If lost = all user deposits lost forever

---

## Step 2: Configure Server

**Edit `appsettings.Production.json`:**

```json
{
  "Blockchain": {
    "MasterMnemonic": "YOUR 12 WORDS HERE",
    "HotWalletAddress": "0xYOUR_HOT_WALLET_ADDRESS",
    
    "Polygon": {
      "RpcUrl": "https://polygon-rpc.com"
    },
    
    "Tron": {
      "RpcUrl": "https://api.trongrid.io"
    },
    
    "Binance": {
      "RpcUrl": "https://bsc-dataseed.binance.org"
    }
  },
  
  "Security": {
    "WalletEncryptionKey": "GENERATE_32_BYTE_KEY_HERE",
    "EncryptionKey": "SAME_OR_DIFFERENT_32_BYTE_KEY"
  }
}
```

**Generate encryption key:**
```bash
openssl rand -base64 32
```

---

## Step 3: How It Works

### User Deposits $100:

```
1. User clicks "Deposit" → selects Polygon network
2. Backend generates unique address: 0xUSER123...ABC
3. User sends $100 USDT to 0xUSER123...ABC
4. Background service checks address every 30 seconds
5. Detects $100 USDT incoming (0/12 confirmations)
6. Waits for 12 confirmations (~2 minutes)
7. Credits $100 to user balance
8. Sweeps $100 from 0xUSER123...ABC to hot wallet
9. User can play immediately
```

**No manual verification needed!**

---

## Step 4: Address Derivation (BIP44)

**Master Mnemonic:** `witch collapse practice...`

**Derivation Path:** `m/44'/60'/0'/0/{index}`

| User | Index | Address |
|------|-------|---------|
| User A (Polygon) | 0 | 0xAAAA...1111 |
| User B (Polygon) | 1 | 0xBBBB...2222 |
| User C (Polygon) | 2 | 0xCCCC...3333 |
| User A (Tron) | 0 | TUserA...111 |
| User B (Tron) | 1 | TUserB...222 |

**Each network has separate index counter.**

---

## Step 5: Background Monitoring Service

**Service runs every 30 seconds:**

```csharp
while (true)
{
    foreach (var userWallet in allWallets)
    {
        // Check USDT balance
        var balance = await GetUSDTBalance(userWallet.Address);
        
        if (balance > 0)
        {
            // New deposit detected!
            CreatePendingDeposit(userWallet, balance);
            
            // Wait for confirmations...
            if (confirmations >= 12)
            {
                CreditUser(userWallet.UserId, balance);
                SweepToHotWallet(userWallet, balance);
            }
        }
    }
    
    await Task.Delay(30_000); // 30 seconds
}
```

---

## Step 6: Deployment Checklist

### Before Going Live:

1. ✅ Generate master mnemonic (12 words)
2. ✅ Write it down on paper (3 copies)
3. ✅ Add to `appsettings.Production.json`
4. ✅ Generate encryption key (32 bytes)
5. ✅ Add hot wallet address
6. ✅ Configure RPC endpoints
7. ✅ Run database migration
8. ✅ Start deposit monitoring service
9. ✅ Test with $1 deposit
10. ✅ Verify automatic credit + sweep

### Test Flow:

```bash
# 1. Start server
dotnet run --project src/WebUI/Server

# 2. Register test account
POST /api/auth/register

# 3. Get deposit address
GET /api/wallet/deposit-address?network=POLYGON
# Returns: 0x123...ABC

# 4. Send $1 USDT from your wallet to 0x123...ABC

# 5. Wait ~2 minutes

# 6. Check pending deposits
GET /api/deposits/pending
# Should show: 1 pending deposit, 12/12 confirmations

# 7. Check balance
GET /api/wallet/balance
# Should show: $1 USDT

# 8. Check hot wallet
# $1 should be swept to hot wallet
```

---

## Step 7: Security Best Practices

### Master Mnemonic:
- **Physical backup** (paper, steel plate)
- **Multiple locations** (home safe, bank vault, trusted family)
- **Never digital** (no cloud, no photos, no emails)
- **Test recovery** (restore wallet from words to verify)

### Encryption Key:
- **Different from mnemonic** (separate secret)
- **Environment variable** (not in appsettings file)
- **Rotate yearly** (generate new, re-encrypt all wallets)

### Hot Wallet:
- **Limited funds** (20-30% of total user deposits)
- **Monitor balance** (alert if < $5,000)
- **Cold storage** (70-80% in hardware wallet)

### RPC Endpoints:
- **Use paid services** (Infura, Alchemy for reliability)
- **Fallback nodes** (if primary fails)
- **Rate limit monitoring** (avoid hitting limits)

---

## Step 8: Monitoring & Alerts

### Set Up Alerts For:

**Low Confirmations:**
- If deposit stuck at 5/12 confirmations for >10 minutes

**Sweep Failures:**
- If funds can't be swept to hot wallet

**Hot Wallet Low:**
- If hot wallet balance < $5,000

**Monitoring Service Down:**
- If no deposits detected for >1 hour (even if users depositing)

**Duplicate Deposits:**
- Same TX hash processed twice

---

## Step 9: Troubleshooting

### Deposit Not Detected:

**Check:**
1. Is monitoring service running? (`systemctl status cryptobet-monitor`)
2. Is RPC endpoint working? (check logs)
3. Did user send to correct network? (Polygon, not Ethereum)
4. Did user send USDT? (not ETH/MATIC)

### Sweep Failed:

**Reasons:**
- Insufficient gas in user wallet (add 0.01 MATIC)
- RPC endpoint down
- Network congestion (try again later)

### User Wallet Generation Failed:

**Reasons:**
- Master mnemonic not configured
- Database connection lost
- Index collision (shouldn't happen)

---

## Step 10: Scaling

### 1,000 Users = 1,000 Unique Addresses

**Performance:**
- Monitoring 1,000 addresses every 30 seconds
- ~33 addresses/second
- Low load on RPC endpoint

**Optimization:**
- Batch RPC calls (check 50 addresses at once)
- Only check recently active wallets
- Use webhooks instead of polling (Alchemy, Infura)

### 10,000+ Users:

**Use Blockchain Indexers:**
- **The Graph** (subgraphs for USDT transfers)
- **Moralis** (webhook on transfer events)
- **Covalent** (historical transaction data)

**Benefits:**
- Real-time notifications
- No polling needed
- Lower server load

---

## API Endpoints (User-Facing)

### Get Deposit Address:
```
GET /api/wallet/deposit-address?network=POLYGON

Response:
{
  "address": "0x123...ABC",
  "network": {
    "name": "Polygon",
    "confirmations": 12,
    "estimatedTime": "~2 minutes"
  },
  "important": [
    "Only send USDT to this address",
    "Make sure you select Polygon network",
    "Deposits are automatically credited"
  ]
}
```

### View Pending Deposits:
```
GET /api/deposits/pending

Response:
{
  "pendingDeposits": [
    {
      "amount": 100,
      "confirmations": 8,
      "requiredConfirmations": 12,
      "txHash": "0xabc...",
      "explorerUrl": "https://polygonscan.com/tx/0xabc..."
    }
  ]
}
```

### View Deposit History:
```
GET /api/deposits/history

Response:
{
  "deposits": [
    {
      "amount": 100,
      "network": "POLYGON",
      "creditedAt": "2026-04-11T10:30:00Z",
      "txHash": "0xabc..."
    }
  ]
}
```

---

## Bottom Line

**You now have a PROFESSIONAL deposit system:**

✅ Unique address per user per network  
✅ Automatic detection (no manual verification)  
✅ Auto-sweep to hot wallet  
✅ Multi-network support  
✅ Like Stake.com, Binance, Coinbase  

**Users love it because:**
- Instant deposit detection
- No copying TX hashes
- Real-time confirmation tracking
- Professional UX

**You love it because:**
- Fully automated
- Scales to millions of users
- Secure (HD wallet + encryption)
- Easy to monitor

**Deploy and forget.** 🌿
