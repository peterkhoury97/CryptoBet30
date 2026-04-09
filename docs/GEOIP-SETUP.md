# GeoIP Setup Instructions

## Overview
The platform uses MaxMind GeoLite2 to block access from restricted countries.

## Option 1: Use Cloudflare (Easiest)

If your site is behind Cloudflare (recommended for anonymity):

1. Add your domain to Cloudflare
2. Enable "Proxied" mode (orange cloud)
3. Cloudflare automatically adds `CF-IPCountry` header
4. No GeoIP database needed!

**Advantage:** Zero setup, automatic IP hiding

## Option 2: MaxMind GeoLite2 Database (Self-hosted)

### Download GeoLite2 Database

```bash
# Create directory
sudo mkdir -p /var/lib/GeoIP

# Download GeoLite2-Country database
cd /var/lib/GeoIP
sudo wget https://git.io/GeoLite2-Country.mmdb

# Or sign up for free account and download from:
# https://dev.maxmind.com/geoip/geolite2-free-geolocation-data

# Set permissions
sudo chmod 644 GeoLite2-Country.mmdb
```

### Install MaxMind NuGet Package

```bash
cd src/WebUI/Server
dotnet add package MaxMind.GeoIP2
```

### Update appsettings.json

```json
"GeoIP": {
  "DatabasePath": "/var/lib/GeoIP/GeoLite2-Country.mmdb"
}
```

### Auto-update script (optional)

Create `/usr/local/bin/update-geoip.sh`:

```bash
#!/bin/bash
cd /var/lib/GeoIP
wget -O GeoLite2-Country.mmdb.new https://git.io/GeoLite2-Country.mmdb
mv GeoLite2-Country.mmdb.new GeoLite2-Country.mmdb
chmod 644 GeoLite2-Country.mmdb
```

Add to crontab:
```bash
# Update GeoIP database weekly
0 3 * * 0 /usr/local/bin/update-geoip.sh
```

## Option 3: Disable Geo-blocking (Development Only)

For local testing, leave `DatabasePath` empty:

```json
"GeoIP": {
  "DatabasePath": ""
}
```

Middleware will skip blocking when no database is configured.

## Blocked Countries List

The following countries are blocked (see `GeoBlockingMiddleware.cs`):

**North America:** US, CA  
**Europe:** GB, FR, DE, IT, ES, NL, BE, DK, SE, NO, FI, PL, GR, PT, CZ, RO, BG, HR, HU  
**Oceania:** AU, NZ  
**Asia:** CN, KP, SG, KR, MY, TH, VN, PH, ID  
**Middle East:** SA, AE, QA, KW, BH, OM, IQ, IR, AF, PK  
**Others:** ZA, CU, SY, KH  

## Testing

Test geo-blocking by setting `CF-IPCountry` header:

```bash
# Simulate US access (blocked)
curl -H "CF-IPCountry: US" http://localhost:8080/

# Simulate Brazil access (allowed)
curl -H "CF-IPCountry: BR" http://localhost:8080/
```

## Troubleshooting

**Problem:** Everyone is blocked  
**Solution:** Check if GeoIP database path is correct

**Problem:** Nobody is blocked  
**Solution:** Ensure middleware is registered in `Program.cs`:
```csharp
app.UseMiddleware<GeoBlockingMiddleware>();
```

**Problem:** VPN users bypass blocking  
**Solution:** This is intentional for privacy. Add language in Terms that VPN use is prohibited and grounds for account termination.
