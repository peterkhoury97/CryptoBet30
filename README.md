# CryptoBet30 - Blockchain Gambling Platform

🎰 **30-Second Binary Price Prediction Game**

Built with .NET 9, Blazor WebAssembly, and Clean Architecture.

---

## 🎯 Features

- ⚡ **30-Second Game Cycles** - Fast-paced betting with 15-second lock window
- 🔢 **Digit Sum Prediction** - Predict if BTC/ETH price digit sum goes Higher or Lower
- 💰 **Pool-Based Payouts** - Winner takes from loser pool (minus 2% house edge)
- 🔗 **Blockchain Integration** - Polygon/BSC for low-fee deposits & withdrawals
- 📊 **Real-Time Updates** - SignalR WebSocket for instant price & pool updates
- 🎁 **Referral System** - Multi-tier commission structure (5/3/1%)
- 🔐 **Non-Custodial Option** - MetaMask/WalletConnect integration
- 📱 **Responsive UI** - Dark mode Tailwind CSS design

---

## 🏗️ Architecture

```
Clean Architecture Pattern:
├── Domain       - Entities, Value Objects, Domain Logic
├── Application  - Use Cases, Commands, Queries (MediatR)
├── Infrastructure - EF Core, Nethereum, Redis, SignalR
└── WebUI
    ├── Server   - ASP.NET Core Web API
    └── Client   - Blazor WebAssembly
```

---

## 🚀 Quick Start

### Prerequisites

- .NET 9 SDK
- PostgreSQL 16
- Redis 7
- Node.js 20+ (for Tailwind CSS)

### 1. Generate Solution

```bash
chmod +x generate-solution.sh
./generate-solution.sh
```

### 2. Configure Settings

Edit `src/WebUI/Server/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=cryptobet30;...",
    "Redis": "localhost:6379"
  },
  "Blockchain": {
    "RpcUrl": "https://polygon-rpc.com",
    "HotWalletPrivateKey": "YOUR_PRIVATE_KEY"
  }
}
```

⚠️ **SECURITY:** Never commit private keys! Use environment variables in production.

### 3. Database Setup

```bash
cd CryptoBet30

# Add migration
dotnet ef migrations add Initial \
  -p src/Infrastructure \
  -s src/WebUI/Server

# Update database
dotnet ef database update \
  -p src/Infrastructure \
  -s src/WebUI/Server
```

### 4. Run with Docker

```bash
docker-compose up -d
```

**Services:**
- API: http://localhost:5000
- Client: http://localhost:8080
- Swagger: http://localhost:5000/swagger

### 5. Or Run Locally

```bash
# Terminal 1: API
cd src/WebUI/Server
dotnet run

# Terminal 2: Client
cd src/WebUI/Client
dotnet run
```

---

## 🎮 How It Works

### Game Mechanics

1. **Round Start (T_0)**
   - Fetch BTC price: $67,245.12345678
   - Calculate digit sum: 6+7+2+4+5+1+2+3+4+5+6+7+8 = **60**

2. **Betting Window (0-15 seconds)**
   - Users place bets: Higher or Lower
   - Bets locked at 15-second mark

3. **Lock Period (15-30 seconds)**
   - No betting allowed
   - Price continues updating

4. **Settlement (T_30)**
   - Fetch final price: $67,312.87654321
   - Calculate digit sum: 6+7+3+1+2+8+7+6+5+4+3+2+1 = **55**
   - Result: **Lower** (55 < 60)
   - Winners receive payout from losing pool

### Payout Formula

```csharp
// Example:
// Total pool: 100 USDT (60 Higher, 40 Lower)
// Result: Lower wins
// Your bet: 10 USDT on Lower

Payout = (YourBet / WinningPool) * TotalPool * (1 - HouseEdge)
       = (10 / 40) * 100 * 0.98
       = 24.50 USDT
Profit = 14.50 USDT (145% ROI)
```

---

## 💻 API Endpoints

### Game

- `GET /api/game/current` - Get current round
- `GET /api/game/history` - Recent rounds
- `POST /api/bets` - Place bet

### Wallet

- `GET /api/wallet/balance` - User balance
- `GET /api/wallet/deposit-address` - Get deposit address
- `POST /api/wallet/withdraw` - Request withdrawal
- `GET /api/wallet/transactions` - Transaction history

### Admin

- `GET /api/admin/stats` - Platform statistics
- `PATCH /api/admin/settings` - Update game settings
- `GET /api/admin/users` - User management

---

## 🔐 Security Features

1. **Rate Limiting**
   - 60 requests/minute (general)
   - 5 bets/10 seconds (anti-spam)

2. **JWT Authentication**
   - Secure wallet-based login
   - Token expiry & refresh

3. **HMAC Signing**
   - Internal balance transfers verified
   - Prevents tampering

4. **Cold Storage**
   - Auto-sweep excess funds
   - Hot wallet threshold: 100 USDT

5. **Input Validation**
   - FluentValidation on all commands
   - No negative bets or over-leverage

---

## 📊 Tech Stack

**Backend:**
- .NET 9
- Entity Framework Core 9
- MediatR (CQRS)
- SignalR
- Nethereum
- Redis
- PostgreSQL

**Frontend:**
- Blazor WebAssembly
- Tailwind CSS
- Fluxor (State Management)
- SignalR Client

**DevOps:**
- Docker & Docker Compose
- GitHub Actions (CI/CD)
- Serilog (Logging)

---

## 📁 Project Structure

```
CryptoBet30/
├── src/
│   ├── Domain/
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   ├── GameRound.cs
│   │   │   ├── Bet.cs
│   │   │   ├── Transaction.cs
│   │   │   └── Referral.cs
│   │   └── ValueObjects/
│   │
│   ├── Application/
│   │   ├── Commands/
│   │   │   ├── PlaceBet/
│   │   │   └── ProcessWithdrawal/
│   │   ├── Queries/
│   │   │   └── GetCurrentRound/
│   │   └── Services/
│   │       └── GameEngine.cs
│   │
│   ├── Infrastructure/
│   │   ├── Persistence/
│   │   │   └── ApplicationDbContext.cs
│   │   ├── Blockchain/
│   │   │   └── EthereumService.cs
│   │   ├── Caching/
│   │   │   └── RedisGameStateService.cs
│   │   └── SignalR/
│   │       └── GameHub.cs
│   │
│   └── WebUI/
│       ├── Client/
│       │   ├── Pages/
│       │   │   ├── Game.razor
│       │   │   └── Wallet.razor
│       │   └── Components/
│       │       ├── BettingPanel.razor
│       │       └── CountdownTimer.razor
│       └── Server/
│           └── Controllers/
│               ├── GameController.cs
│               └── WalletController.cs
│
├── docker-compose.yml
├── generate-solution.sh
└── README.md
```

---

## 🧪 Testing

```bash
# Run unit tests
dotnet test

# Run integration tests
dotnet test --filter Category=Integration

# Load testing (k6)
k6 run tests/load/betting-stress.js
```

---

## 🚢 Deployment

### Production Checklist

- [ ] Change JWT secret key
- [ ] Use environment variables for secrets
- [ ] Enable HTTPS (Let's Encrypt)
- [ ] Set up cold wallet sweeping
- [ ] Configure rate limiting
- [ ] Enable logging (Serilog → Seq/ELK)
- [ ] Set up monitoring (Prometheus + Grafana)
- [ ] Configure backups (PostgreSQL + Redis)

### Deploy to Cloud

**AWS:**
```bash
# ECS/Fargate deployment
aws ecs create-service ...
```

**Azure:**
```bash
# Container Apps
az containerapp create ...
```

---

## 📜 License

MIT License - Free to use and modify.

---

## 🤝 Contributing

1. Fork the repo
2. Create feature branch
3. Commit changes
4. Push to branch
5. Open Pull Request

---

## ⚠️ Disclaimer

This is a gambling platform. Use responsibly and comply with your local regulations. The developers are not responsible for any financial losses.

**Gambling can be addictive. Play responsibly.**

---

## 📞 Support

- **Issues:** https://github.com/yourusername/CryptoBet30/issues
- **Discussions:** https://github.com/yourusername/CryptoBet30/discussions
- **Email:** support@cryptobet30.com

---

Built with ❤️ by the CryptoBet30 Team
