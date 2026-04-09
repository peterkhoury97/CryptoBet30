# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |

## Reporting a Vulnerability

**DO NOT** open public issues for security vulnerabilities.

Instead, email security reports to: **security@cryptobet30.com**

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

We will respond within 48 hours and provide a timeline for fixes.

## Security Best Practices

### For Deployment:

1. **Never commit private keys or secrets**
   - Use environment variables
   - Rotate keys regularly

2. **Enable HTTPS**
   - Use Let's Encrypt for free SSL
   - Enforce HTTPS redirects

3. **Rate Limiting**
   - Configure `AspNetCoreRateLimit` in production
   - Monitor for DDoS attempts

4. **Cold Wallet Sweeping**
   - Set `SweepThreshold` to minimize hot wallet exposure
   - Store cold wallet keys offline

5. **Database Security**
   - Use strong PostgreSQL passwords
   - Restrict network access
   - Enable SSL connections

6. **Logging & Monitoring**
   - Forward logs to secure aggregation service
   - Set up alerts for suspicious activity
   - Never log sensitive data (keys, passwords)

## Known Security Considerations

- Hot wallet keys stored in environment variables (production should use HSM/KMS)
- JWT secret must be 256+ bits and rotated regularly
- Blockchain confirmations set to 12 (adjust for different chains)
- CORS must be restricted in production

## Compliance

This platform handles financial transactions. Ensure compliance with:
- Local gambling laws
- KYC/AML regulations
- Data privacy (GDPR, CCPA)
- Financial licensing requirements
