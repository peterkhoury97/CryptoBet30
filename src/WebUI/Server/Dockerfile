FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/WebUI/Server/CryptoBet30.WebUI.Server.csproj", "src/WebUI/Server/"]
COPY ["src/Infrastructure/CryptoBet30.Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/Application/CryptoBet30.Application.csproj", "src/Application/"]
COPY ["src/Domain/CryptoBet30.Domain.csproj", "src/Domain/"]

# Restore dependencies
RUN dotnet restore "src/WebUI/Server/CryptoBet30.WebUI.Server.csproj"

# Copy source code
COPY . .

# Build
WORKDIR "/src/src/WebUI/Server"
RUN dotnet build "CryptoBet30.WebUI.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CryptoBet30.WebUI.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CryptoBet30.WebUI.Server.dll"]
