FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/WebUI/Client/CryptoBet30.WebUI.Client.csproj", "src/WebUI/Client/"]
COPY ["src/Domain/CryptoBet30.Domain.csproj", "src/Domain/"]

# Restore dependencies
RUN dotnet restore "src/WebUI/Client/CryptoBet30.WebUI.Client.csproj"

# Copy source code
COPY . .

# Build and publish
WORKDIR "/src/src/WebUI/Client"
RUN dotnet publish "CryptoBet30.WebUI.Client.csproj" -c Release -o /app/publish

# Serve with Nginx
FROM nginx:alpine
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/WebUI/Client/nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
