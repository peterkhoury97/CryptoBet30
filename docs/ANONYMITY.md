# 🕶️ Anonymity & Privacy Guide

## Overview

This platform is designed with privacy and anonymity in mind. This guide explains how to deploy and operate it anonymously.

---

## 🚨 CRITICAL DISCLAIMERS

**⚖️ LEGAL WARNING:**  
Online gambling is illegal in many jurisdictions. Operating an unlicensed gambling platform may be a crime. This software is provided for **educational purposes only**. The developers assume NO liability for your use of this software.

**🔞 AGE RESTRICTION:**  
This platform must only be used by individuals 18+ (or legal gambling age in their jurisdiction).

**🌍 GEOGRAPHIC RESTRICTIONS:**  
This platform blocks users from 60+ countries including the US, UK, EU, Australia, and countries with strict gambling laws. See `GeoBlockingMiddleware.cs` for the full list.

---

## 🛡️ Anonymity Architecture

### 1. Hosting Options

#### Option A: Offshore VPS (Recommended)

**Providers:**
- **ExtraVM** (Romania, Luxembourg) - No KYC, crypto accepted
- **AlphaVPS** (Bulgaria) - Anonymous signup
- **BuyVM** (Luxembourg) - Privacy-focused

**Setup:**
```bash
# Purchase VPS with Monero/Bitcoin
# Use burner email (ProtonMail)
# Never connect without VPN
# Use SSH keys only (disable password auth)
```

**Cost:** $20-50/month

#### Option B: Tor Hidden Service (Maximum Anonymity)

```bash
# Install Tor
apt install tor

# Configure hidden service
nano /etc/tor/torrc

HiddenServiceDir /var/lib/tor/cryptobet/
HiddenServicePort 80 127.0.0.1:8080

# Restart and get .onion address
systemctl restart tor
cat /var/lib/tor/cryptobet/hostname
# → abc123xyz.onion
```

**Pros:** Completely untraceable, no IP exposure  
**Cons:** Slower, harder for users to access

#### Option C: Decentralized (IPFS)

```bash
# Deploy to IPFS
ipfs add -r build/

# Use ENS domain
# mycasino.eth → IPFS hash
```

**Pros:** Censorship-resistant, no central server  
**Cons:** Expensive, complex setup

---

### 2. Domain Registration

#### Option A: Njalla (Recommended)

[Njalla.no](https://njal.la) - Privacy-focused registrar
- Registers domain in THEIR name
- You control via dashboard
- Pay with Bitcoin/Monero
- No WHOIS exposure

**Cost:** $15/year

#### Option B: ENS (Ethereum Name Service)

```
mycasino.eth
```

- Fully decentralized
- Can't be seized
- Crypto wallets resolve automatically
- No KYC

**Cost:** $5-20/year in gas

#### Option C: Regular Registrar + WHOIS Privacy

- Namecheap/Porkbun
- Enable WHOIS guard
- Pay with crypto (some allow)
- Still traceable via payment

---

### 3. Cloudflare Setup (Hide Origin IP)

**Why:** Even if someone finds your domain, they can't find your server IP.

```bash
# 1. Add domain to Cloudflare (free tier)
# 2. Update DNS records to point to your VPS
# 3. Enable "Proxied" mode (orange cloud ☁️)

# Result:
# User → Cloudflare → Your hidden VPS
# Cloudflare IP is public, yours stays hidden
```

**Bonus:** Cloudflare provides geo-IP headers (`CF-IPCountry`), so you don't need MaxMind database.

---

### 4. Operational Security (OPSEC)

#### VPN Always

**Recommended:** Mullvad VPN
- No logs policy
- Pay with Monero (anonymous)
- No email required
- $5/month

```bash
# Never connect to server without VPN!
mullvad account create
mullvad connect
ssh root@your-vps
```

#### Separate Identity

- **Email:** ProtonMail (no phone verification)
- **Name:** Use pseudonym, never real name
- **Payment:** Crypto only (Monero > Bitcoin)
- **GitHub:** Separate account, anonymous email
- **Communication:** Matrix/Element (no phone)

#### Device Separation

- Use separate laptop/phone for casino operations
- Never log in from personal devices
- Consider Tails OS (amnesiac, leaves no traces)

---

### 5. Payment Processing (Crypto Only)

**Already Implemented:**
- Polygon USDT deposits
- Blockchain withdrawals
- No fiat, no KYC

**Wallet Security:**

```
Hot Wallet (on server): Max $10k
- For automatic withdrawals
- If hacked, limited loss

Cold Wallet (offline): Bulk of funds
- Hardware wallet (Ledger, Trezor)
- Manually sweep hot wallet daily

Personal Wallet: Separate
- Never mix business/personal
- Different seed phrases
```

**Multi-sig Option:**

```
2-of-3 multisig setup:
- Key 1: Server (hot)
- Key 2: Your hardware wallet
- Key 3: Backup paper wallet

Even if server is hacked, thief needs 2 keys.
```

---

### 6. Database Encryption

**Already Implemented:**
- AES-256 encryption for sensitive data
- Hashed IPs (if logging enabled)
- Password hashing (PBKDF2)

**Additional Protection:**

```bash
# Encrypt entire PostgreSQL database
# Install encryption extension
sudo apt install postgresql-14-pgcrypto

# Enable encryption
psql -U cryptobet_user -d cryptobet30
CREATE EXTENSION IF NOT EXISTS pgcrypto;

# Encrypt columns
ALTER TABLE "Users" 
ADD COLUMN "EmailEncrypted" bytea;

UPDATE "Users" 
SET "EmailEncrypted" = pgp_sym_encrypt("Email", 'your-secret-key');
```

---

### 7. Geo-Blocking Implementation

**Blocked Countries:** 60+ (US, UK, EU, AU, etc.)

**How it works:**
1. Middleware checks user's IP
2. Gets country code from Cloudflare header or MaxMind GeoIP
3. Blocks if country in restricted list
4. Redirects to `/blocked` page

**See:** `GeoBlockingMiddleware.cs` for full list

---

### 8. Legal Protection

#### Disclaimers (Already Included)

- `/legal` page with full Terms of Service
- Age restrictions (18+)
- Country restrictions
- "No license" disclosure
- "Entertainment only" language
- Gambling addiction warnings

#### Corporate Structure Options

**Option A: Offshore Company ($$$)**
- Register LLC in Belize, Seychelles, Panama
- Costs: $1,000-2,000/year
- Nominee director (lawyer acts as owner)
- Protects personal assets

**Option B: No Company**
- Just operate it
- Higher personal risk
- Cheaper ($0)

---

### 9. Traffic Anonymization

#### Remove Tracking

```csharp
// Already done in appsettings.json:
// ❌ No Google Analytics
// ❌ No Sentry
// ❌ No third-party trackers

// Only local logging
"Serilog": {
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "File", "path": "logs/" }
  ]
}
```

#### IP Address Handling

```csharp
// Middleware hashes IPs before logging
var hashedIp = SHA256.HashData(
  Encoding.UTF8.GetBytes(ip + "salt")
);
```

---

### 10. GitHub Security

#### Remove Identifying Info

```bash
# Change git history
git config user.name "Anonymous"
git config user.email "anon@protonmail.com"

# Rewrite all commits
git filter-branch --env-filter '
export GIT_AUTHOR_NAME="Anonymous"
export GIT_AUTHOR_EMAIL="anon@protonmail.com"
export GIT_COMMITTER_NAME="Anonymous"
export GIT_COMMITTER_EMAIL="anon@protonmail.com"
' --tag-name-filter cat -- --branches --tags

git push --force --all
```

#### Or Make Private

```bash
# GitHub Settings → Visibility → Private
```

---

## 📋 Deployment Checklist

### Pre-Launch

- [ ] Purchase VPS with crypto (ExtraVM/AlphaVPS)
- [ ] Set up Mullvad VPN ($5/month)
- [ ] Create ProtonMail account
- [ ] Register domain via Njalla ($15/year)
- [ ] Add domain to Cloudflare (free)
- [ ] Generate new encryption keys (`openssl rand -base64 32`)
- [ ] Create separate crypto wallets (hot/cold/personal)
- [ ] Remove all personal info from code
- [ ] Test geo-blocking (US/UK should be blocked)

### Launch

- [ ] Deploy to VPS (only connect via VPN)
- [ ] Enable HTTPS (Let's Encrypt)
- [ ] Test all game functionality
- [ ] Verify provably fair system
- [ ] Monitor hot wallet balance
- [ ] Set up daily cold wallet sweeps

### Ongoing

- [ ] Never connect without VPN
- [ ] Monitor logs for abuse
- [ ] Update GeoIP database monthly
- [ ] Rotate encryption keys quarterly
- [ ] Keep hot wallet under $10k
- [ ] Back up database weekly
- [ ] Review Terms of Service page

---

## ⚠️ What Can Still Get You Caught

Even with all this:

1. **You KYC'd on crypto exchange** → Trail leads to you
   - **Fix:** Use non-KYC exchanges (Bisq, Localbitcoins)

2. **You used real email somewhere** → Leaked in data breach
   - **Fix:** ProtonMail with random username

3. **You connected once without VPN** → IP in server logs
   - **Fix:** Always VPN, or nuke logs

4. **Someone snitches** → If you tell anyone
   - **Fix:** Tell nobody. Ever.

5. **You're making too much money** → Tax authorities notice
   - **Fix:** Offshore company, or stay under $50k/year

6. **User reports you** → Authorities investigate
   - **Fix:** Block restricted countries, have good ToS

---

## 🎯 Anonymity Levels

### Level 1: Basic ($30/month)
- Offshore VPS
- Cloudflare proxy
- Crypto payments only
- WHOIS privacy

**Protects against:** Casual observers, bots

### Level 2: Advanced ($50/month)
- Level 1 +
- Njalla domain
- 24/7 VPN (Mullvad)
- Separate identity
- Encrypted database

**Protects against:** Doxxing, most legal requests

### Level 3: Maximum ($$$ + effort)
- Level 2 +
- Offshore company (nominee director)
- Tor hidden service
- Hardware wallets
- Tails OS
- Multi-sig funds

**Protects against:** Nation-state actors (maybe)

---

## 🌿 Final Thoughts

**Reality check:**  
Most crypto casinos (Stake, Roobet, etc.) operate openly with Curaçao licenses. They're not hiding.

**Your choice:**
1. **Go legitimate:** Get license, pay taxes, grow big
2. **Stay small/quiet:** Offshore, no license, under the radar
3. **Full anon:** Tor, everything above, stay paranoid

**My recommendation:**  
If you're in a country that bans gambling → full OPSEC.  
If you're in a crypto-friendly country → get a license, do it legally.

Good luck. 🕶️
