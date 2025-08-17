# Brainarr Security Guide

## Overview

This guide covers security best practices for deploying and operating Brainarr in production environments. Follow these guidelines to protect your API keys, music library data, and system resources.

## Table of Contents
- [API Key Management](#api-key-management)
- [Network Security](#network-security)
- [Data Privacy](#data-privacy)
- [Local vs Cloud Providers](#local-vs-cloud-providers)
- [Authentication & Authorization](#authentication--authorization)
- [Secure Deployment](#secure-deployment)
- [Security Checklist](#security-checklist)

## API Key Management

### Storing API Keys Securely

#### ✅ DO:
```bash
# Use environment variables
export OPENAI_API_KEY="sk-..."
export ANTHROPIC_API_KEY="sk-ant-..."

# Use secure key vaults
# Azure Key Vault
az keyvault secret set --vault-name MyVault --name openai-key --value "sk-..."

# HashiCorp Vault
vault kv put secret/brainarr openai_key="sk-..."

# Docker Secrets
echo "sk-..." | docker secret create openai_key -
```

#### ❌ DON'T:
```bash
# Never commit keys to git
git add config.json  # Contains API keys - BAD!

# Never log API keys
echo "Using API key: $OPENAI_API_KEY"  # Logs expose keys

# Never share config files with keys
scp lidarr-config.tar.gz user@public-server:  # Includes keys
```

### API Key Rotation

**Implement regular key rotation:**

```bash
#!/bin/bash
# rotate-keys.sh - Run monthly via cron

# 1. Generate new keys from providers
NEW_KEY=$(generate_new_api_key)

# 2. Update Lidarr configuration
sqlite3 /var/lib/lidarr/lidarr.db \
  "UPDATE Config SET Value='$NEW_KEY' WHERE Key='BrainarrOpenAIKey';"

# 3. Restart Lidarr
systemctl restart lidarr

# 4. Revoke old keys
revoke_old_api_key $OLD_KEY
```

### Protecting Keys in Memory

```csharp
// Keys are stored with PrivacyLevel.Password in Brainarr
[FieldDefinition(3, Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
public string ApiKey { get; set; }

// This ensures:
// - Keys are encrypted in database
// - Keys are masked in UI
// - Keys are excluded from logs
// - Keys are not included in backups without encryption
```

### Key Permissions

**Principle of Least Privilege:**

| Provider | Minimal Permissions Required |
|----------|------------------------------|
| OpenAI | `model.read`, `completion.write` |
| Anthropic | `chat:write` |
| Google | `generativelanguage.models.generateContent` |
| Azure OpenAI | `Cognitive Services User` role |

**Never use admin/owner keys for Brainarr!**

## Network Security

### Local Provider Security

**Ollama Security Configuration:**

```yaml
# ollama-config.yaml
bind: 127.0.0.1  # Only localhost
port: 11434
ssl: false  # Not needed for localhost
auth: false  # Local only, no auth needed
```

**LM Studio Security:**
```json
{
  "server": {
    "host": "127.0.0.1",
    "port": 1234,
    "corsOrigins": ["http://localhost:8686"],
    "requireAuth": false
  }
}
```

### Cloud Provider Security

**Always use HTTPS:**
```csharp
// Brainarr enforces HTTPS for cloud providers
if (!endpoint.StartsWith("https://"))
{
    throw new SecurityException("HTTPS required for cloud providers");
}
```

**IP Whitelisting (where supported):**

```bash
# OpenAI IP restrictions
curl -X POST https://api.openai.com/v1/organizations/settings \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -d '{
    "ip_whitelist": ["203.0.113.0/24"]
  }'
```

### Firewall Rules

**Recommended firewall configuration:**

```bash
# Allow Lidarr
sudo ufw allow 8686/tcp

# Allow local AI providers (localhost only)
sudo ufw allow from 127.0.0.1 to any port 11434  # Ollama
sudo ufw allow from 127.0.0.1 to any port 1234   # LM Studio

# Block external access to AI providers
sudo ufw deny from any to any port 11434
sudo ufw deny from any to any port 1234

# Allow outbound HTTPS for cloud providers
sudo ufw allow out 443/tcp
```

**Docker network isolation:**

```yaml
# docker-compose.yml
version: '3.8'

services:
  lidarr:
    image: linuxserver/lidarr
    networks:
      - internal
      - external
  
  ollama:
    image: ollama/ollama
    networks:
      - internal  # Only internal network
    expose:
      - "11434"  # Not published to host

networks:
  internal:
    internal: true  # No external access
  external:
    # Internet access for Lidarr
```

## Data Privacy

### What Data Is Sent to AI Providers?

**Local Providers (Ollama, LM Studio):**
- ✅ **Nothing leaves your network**
- All processing happens locally
- Complete privacy

**Cloud Providers:**
Data sent includes:
- Music library statistics (artist count, genre distribution)
- Sample artist names (configurable via SamplingStrategy)
- No personal information
- No file paths or system information

### Minimizing Data Exposure

**Use Minimal Sampling Strategy:**
```json
{
  "SamplingStrategy": "Minimal",
  "MaxRecommendations": 5
}
```

**Sanitize Artist Names:**
```csharp
// Brainarr automatically sanitizes sensitive data
artistName = RemovePersonalInfo(artistName);
artistName = TruncateLength(artistName, 50);
```

### GDPR Compliance

**For EU users:**

1. **Use local providers** for complete GDPR compliance
2. **For cloud providers**, ensure:
   - Provider is GDPR compliant
   - Data Processing Agreement (DPA) is in place
   - Right to deletion is supported

**Data retention:**
```bash
# Clear all cached recommendations
rm -rf /var/lib/lidarr/.cache/brainarr/

# Remove from database
sqlite3 /var/lib/lidarr/lidarr.db \
  "DELETE FROM ImportListStatus WHERE ProviderId = 
   (SELECT Id FROM ImportLists WHERE Implementation = 'Brainarr');"
```

## Local vs Cloud Providers

### Security Comparison

| Aspect | Local (Ollama/LM Studio) | Cloud (OpenAI/Anthropic/etc) |
|--------|--------------------------|------------------------------|
| **Data Privacy** | 100% Private | Data sent to provider |
| **Network Exposure** | None (localhost only) | Internet required |
| **API Key Risk** | None | Key theft possible |
| **Compliance** | Full control | Depends on provider |
| **Audit Trail** | Local logs only | Provider + local logs |
| **Data Residency** | Guaranteed local | Varies by provider |
| **Cost Security** | No cost = no risk | Potential bill shock |

### Recommendation by Security Level

**Maximum Security (Air-gapped/Classified):**
- ✅ Ollama with local models only
- ✅ No internet connectivity
- ✅ All processing on-premises

**High Security (Corporate/Regulated):**
- ✅ LM Studio or Ollama
- ⚠️ OpenAI/Anthropic with enterprise agreement
- ✅ VPN/proxy for cloud access

**Standard Security (Home/Small Business):**
- ✅ Any provider with proper API key management
- ✅ HTTPS enforcement
- ✅ Regular key rotation

## Authentication & Authorization

### Lidarr Authentication

**Enable authentication:**
```xml
<!-- config.xml -->
<Config>
  <AuthenticationMethod>Forms</AuthenticationMethod>
  <AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>
</Config>
```

**Strong passwords:**
```bash
# Generate strong password
openssl rand -base64 32

# Or use password manager
bitwarden generate -uln --length 32
```

### API Access Control

**Restrict Lidarr API access:**
```nginx
# nginx reverse proxy with IP restrictions
server {
    listen 443 ssl;
    server_name lidarr.example.com;
    
    location /api {
        allow 192.168.1.0/24;  # Local network
        allow 203.0.113.5;      # Specific IP
        deny all;
        
        proxy_pass http://localhost:8686;
    }
}
```

### Multi-Factor Authentication

**For cloud provider accounts:**
1. Enable MFA on OpenAI, Anthropic, etc.
2. Use authenticator apps, not SMS
3. Store backup codes securely

## Secure Deployment

### Production Deployment Checklist

#### System Hardening
- [ ] Run Lidarr as non-root user
- [ ] Set proper file permissions (640 for configs)
- [ ] Enable SELinux/AppArmor
- [ ] Regular security updates
- [ ] Disable unnecessary services

#### Network Security
- [ ] Use reverse proxy (nginx/Caddy)
- [ ] Enable SSL/TLS with valid certificates
- [ ] Implement rate limiting
- [ ] Configure fail2ban
- [ ] Use VPN for remote access

#### Application Security
- [ ] Enable Lidarr authentication
- [ ] Use strong, unique passwords
- [ ] Regular backup encryption
- [ ] Log monitoring and alerting
- [ ] API key rotation schedule

### Docker Security

**Secure Docker deployment:**

```dockerfile
# Dockerfile
FROM linuxserver/lidarr:latest

# Run as non-root
USER lidarr:lidarr

# Read-only root filesystem
RUN chmod -R 755 /app

# Health check
HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl -f http://localhost:8686/ping || exit 1
```

```yaml
# docker-compose.yml
version: '3.8'

services:
  lidarr:
    image: linuxserver/lidarr
    user: "1000:1000"  # Non-root UID
    read_only: true     # Read-only container
    tmpfs:
      - /tmp
      - /var/run
    security_opt:
      - no-new-privileges:true
      - apparmor:docker-default
    cap_drop:
      - ALL
    cap_add:
      - CHOWN
      - SETUID
      - SETGID
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=UTC
    secrets:
      - openai_key
      - anthropic_key

secrets:
  openai_key:
    external: true
  anthropic_key:
    external: true
```

### Kubernetes Security

```yaml
# brainarr-security-policy.yaml
apiVersion: policy/v1beta1
kind: PodSecurityPolicy
metadata:
  name: brainarr-psp
spec:
  privileged: false
  allowPrivilegeEscalation: false
  requiredDropCapabilities:
    - ALL
  volumes:
    - 'configMap'
    - 'emptyDir'
    - 'projected'
    - 'secret'
    - 'persistentVolumeClaim'
  hostNetwork: false
  hostIPC: false
  hostPID: false
  runAsUser:
    rule: 'MustRunAsNonRoot'
  seLinux:
    rule: 'RunAsAny'
  fsGroup:
    rule: 'RunAsAny'
  readOnlyRootFilesystem: true
```

## Security Monitoring

### Log Monitoring

**What to monitor:**

```bash
# Failed API authentication attempts
grep "401\|403" /var/log/lidarr/lidarr.txt

# Unusual API usage patterns
grep "Brainarr.*GetRecommendations" /var/log/lidarr/lidarr.txt | \
  awk '{print $1}' | sort | uniq -c | sort -rn

# Configuration changes
grep "Settings.*Brainarr.*changed" /var/log/lidarr/lidarr.txt
```

**Alerting setup:**

```yaml
# prometheus-alerts.yml
groups:
  - name: brainarr_security
    rules:
      - alert: HighAPIUsage
        expr: rate(brainarr_api_calls[5m]) > 100
        annotations:
          summary: "Unusual API usage detected"
      
      - alert: AuthenticationFailures
        expr: rate(lidarr_auth_failures[5m]) > 5
        annotations:
          summary: "Multiple auth failures detected"
```

### Security Scanning

**Regular security scans:**

```bash
# Scan for exposed API keys
grep -r "sk-[a-zA-Z0-9]\{48\}" /var/lib/lidarr/ 2>/dev/null

# Check for weak permissions
find /var/lib/lidarr -type f -perm /077 -ls

# Scan Docker images
docker scan linuxserver/lidarr

# Dependency scanning
dotnet list package --vulnerable
```

## Security Checklist

### Daily Checks
- [ ] Review authentication logs
- [ ] Check API usage metrics
- [ ] Monitor error rates

### Weekly Checks
- [ ] Review provider costs
- [ ] Check for unusual patterns
- [ ] Verify backup integrity

### Monthly Checks
- [ ] Rotate API keys
- [ ] Update all components
- [ ] Review security alerts
- [ ] Test disaster recovery

### Quarterly Checks
- [ ] Security audit
- [ ] Penetration testing
- [ ] Policy review
- [ ] Training update

## Incident Response

### If API Key is Compromised

1. **Immediate Actions:**
   ```bash
   # Revoke compromised key immediately
   # Via provider dashboard or API
   
   # Stop Lidarr
   systemctl stop lidarr
   
   # Generate new key
   # Update configuration
   # Restart with new key
   systemctl start lidarr
   ```

2. **Investigation:**
   - Review logs for unauthorized usage
   - Check provider billing/usage
   - Identify compromise source
   - Document incident

3. **Prevention:**
   - Implement key rotation
   - Enhance monitoring
   - Review access controls
   - Update security procedures

### If System is Breached

1. **Isolate:**
   ```bash
   # Disconnect from network
   iptables -P INPUT DROP
   iptables -P OUTPUT DROP
   ```

2. **Assess:**
   - Identify breach vector
   - Determine data exposure
   - Check for persistence

3. **Recover:**
   - Restore from clean backup
   - Rotate all credentials
   - Patch vulnerabilities
   - Enhance monitoring

## Security Resources

### Documentation
- [OWASP Security Guidelines](https://owasp.org)
- [CIS Security Benchmarks](https://cisecurity.org)
- [NIST Cybersecurity Framework](https://nist.gov/cyberframework)

### Tools
- **Secrets Scanning**: TruffleHog, GitLeaks
- **Dependency Scanning**: Snyk, Dependabot
- **Container Scanning**: Trivy, Clair
- **Network Security**: nmap, Wireshark

### Provider Security Pages
- [OpenAI Security](https://openai.com/security)
- [Anthropic Security](https://anthropic.com/security)
- [Google AI Security](https://cloud.google.com/security)

## Contact

For security issues, please report privately to the maintainers rather than creating public issues.