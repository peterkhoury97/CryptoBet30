#!/bin/bash

# CryptoBet30 Solution Generator
# Generates complete .NET 9 Clean Architecture gambling platform

set -e

echo "🎰 CryptoBet30 Solution Generator"
echo "=================================="
echo ""

# Check if .NET 9 SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found. Install from https://dotnet.microsoft.com/download"
    exit 1
fi

echo "✅ .NET SDK found: $(dotnet --version)"
echo ""

# Create solution
echo "📁 Creating solution structure..."
dotnet new sln -n CryptoBet30

# Create Domain layer
echo "🏛️  Creating Domain layer..."
dotnet new classlib -n CryptoBet30.Domain -o src/Domain -f net9.0
dotnet sln add src/Domain/CryptoBet30.Domain.csproj

# Create Application layer
echo "🔧 Creating Application layer..."
dotnet new classlib -n CryptoBet30.Application -o src/Application -f net9.0
dotnet sln add src/Application/CryptoBet30.Application.csproj

# Create Infrastructure layer
echo "⚙️  Creating Infrastructure layer..."
dotnet new classlib -n CryptoBet30.Infrastructure -o src/Infrastructure -f net9.0
dotnet sln add src/Infrastructure/CryptoBet30.Infrastructure.csproj

# Create Blazor WebAssembly UI
echo "🎨 Creating Blazor WebAssembly UI..."
dotnet new blazorwasm -n CryptoBet30.WebUI.Client -o src/WebUI/Client -f net9.0
dotnet sln add src/WebUI/Client/CryptoBet30.WebUI.Client.csproj

# Create ASP.NET Core Web API
echo "🌐 Creating Web API..."
dotnet new webapi -n CryptoBet30.WebUI.Server -o src/WebUI/Server -f net9.0
dotnet sln add src/WebUI/Server/CryptoBet30.WebUI.Server.csproj

# Add project references
echo "🔗 Adding project references..."
dotnet add src/Application/CryptoBet30.Application.csproj reference src/Domain/CryptoBet30.Domain.csproj
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj reference src/Domain/CryptoBet30.Domain.csproj
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj reference src/Application/CryptoBet30.Application.csproj
dotnet add src/WebUI/Server/CryptoBet30.WebUI.Server.csproj reference src/Infrastructure/CryptoBet30.Infrastructure.csproj
dotnet add src/WebUI/Client/CryptoBet30.WebUI.Client.csproj reference src/Domain/CryptoBet30.Domain.csproj

# Install NuGet packages
echo "📦 Installing NuGet packages..."

# Domain packages
dotnet add src/Domain/CryptoBet30.Domain.csproj package FluentValidation --version 11.11.0

# Application packages
dotnet add src/Application/CryptoBet30.Application.csproj package MediatR --version 12.4.1
dotnet add src/Application/CryptoBet30.Application.csproj package AutoMapper --version 13.0.1
dotnet add src/Application/CryptoBet30.Application.csproj package FluentValidation.DependencyInjectionExtensions --version 11.11.0

# Infrastructure packages
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Microsoft.EntityFrameworkCore --version 9.0.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package StackExchange.Redis --version 2.8.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Nethereum.Web3 --version 4.22.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Nethereum.Accounts --version 4.22.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Serilog.AspNetCore --version 8.0.2
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Serilog.Sinks.Console --version 6.0.0
dotnet add src/Infrastructure/CryptoBet30.Infrastructure.csproj package Serilog.Sinks.File --version 6.0.0

# Server packages
dotnet add src/WebUI/Server/CryptoBet30.WebUI.Server.csproj package Microsoft.AspNetCore.SignalR --version 9.0.0
dotnet add src/WebUI/Server/CryptoBet30.WebUI.Server.csproj package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add src/WebUI/Server/CryptoBet30.WebUI.Server.csproj package Swashbuckle.AspNetCore --version 7.2.0
dotnet add src/WebUI/Server/CryptoBet30.WebUI.Server.csproj package AspNetCoreRateLimit --version 5.0.0

# Client packages
dotnet add src/WebUI/Client/CryptoBet30.WebUI.Client.csproj package Microsoft.AspNetCore.SignalR.Client --version 9.0.0
dotnet add src/WebUI/Client/CryptoBet30.WebUI.Client.csproj package Blazored.LocalStorage --version 4.5.0
dotnet add src/WebUI/Client/CryptoBet30.WebUI.Client.csproj package Fluxor.Blazor.Web --version 6.1.1

echo ""
echo "✅ Solution created successfully!"
echo ""
echo "📋 Next steps:"
echo "1. cd CryptoBet30"
echo "2. Copy entity files from workspace/CryptoBet30/src/ to respective folders"
echo "3. Configure appsettings.json with:"
echo "   - PostgreSQL connection string"
echo "   - Redis connection"
echo "   - Blockchain RPC URL"
echo "   - Hot wallet private key (KEEP SECRET!)"
echo "4. Run migrations: dotnet ef migrations add Initial -p src/Infrastructure -s src/WebUI/Server"
echo "5. Update database: dotnet ef database update -p src/Infrastructure -s src/WebUI/Server"
echo "6. Run: dotnet run --project src/WebUI/Server"
echo ""
echo "🎰 Happy coding!"
