# Brainarr Security Best Practices

## Overview

This document outlines security best practices for deploying and managing Brainarr, with particular focus on API key management, data privacy, and secure configuration.

## Table of Contents
- [API Key Management](#api-key-management)
- [Data Privacy](#data-privacy)
- [Network Security](#network-security)
- [Configuration Security](#configuration-security)
- [Deployment Security](#deployment-security)
- [Audit and Compliance](#audit-and-compliance)
- [Secure JSON Handling](#secure-json-handling)

## API Key Management

### Storage Best Practices

#### ❌ Never Do This
```csharp
// NEVER hardcode API keys in source code
public class BadExample
{
    private const string API_KEY = "sk-abc123..."; // NEVER!
    private readonly string _apiKey = "sk-ant-..."; // NEVER!
}
```

```json
// NEVER commit API keys in configuration files
{
  "openai_key": "sk-proj-abc123...",  // NEVER!
  "anthropic_key": "sk-ant-api03..."  // NEVER!
}
```

#### ✅ Do This Instead

**Environment Variables:**
```bash
# Set environment variables (Linux/Mac)
export OPENAI_API_KEY="sk-..."
export ANTHROPIC_API_KEY="sk-ant-..."
export GEMINI_API_KEY="AIza..."

# Set environment variables (Windows)
set OPENAI_API_KEY=sk-...
set ANTHROPIC_API_KEY=sk-ant-...
```

**Secure Configuration:**
```csharp
public class SecureConfiguration
{
    public string GetApiKey(string provider)
    {
        // Read from environment
        var key = Environment.GetEnvironmentVariable($"{provider}_API_KEY");
        
        // Or from secure store
        if (string.IsNullOrEmpty(key))
        {
            key = SecretManager.GetSecret($"brainarr/{provider}/apikey");
        }
        
        return key;
    }
}
```

### Key Rotation

#### Automated Rotation Script
```bash
#!/bin/bash
# rotate-keys.sh - Rotate API keys quarterly

# Backup old keys
vault kv get secret/brainarr > backup-$(date +%Y%m%d).json

# Generate new keys from providers
echo "Generate new API keys from:"
echo "- https://platform.openai.com/api-keys"
echo "- https://console.anthropic.com/settings/keys"
echo "- https://aistudio.google.com/apikey"

# Update vault
read -p "Enter new OpenAI key: " OPENAI_KEY
read -p "Enter new Anthropic key: " ANTHROPIC_KEY

vault kv put secret/brainarr \
  openai_key="$OPENAI_KEY" \
  anthropic_key="$ANTHROPIC_KEY" \
  rotated_at="$(date -Iseconds)"

# Restart services
systemctl restart lidarr

echo "Key rotation complete"
```

### Secrets Management

#### Using HashiCorp Vault
```bash
# Store secrets in Vault
vault kv put secret/brainarr \
  openai_key="sk-..." \
  anthropic_key="sk-ant-..." \
  gemini_key="AIza..."

# Read in application
vault kv get -field=openai_key secret/brainarr
```

#### Using Azure Key Vault
```csharp
public class AzureKeyVaultProvider
{
    private readonly SecretClient _client;
    
    public AzureKeyVaultProvider(string vaultUrl)
    {
        _client = new SecretClient(
            new Uri(vaultUrl),
            new DefaultAzureCredential());
    }
    
    public async Task<string> GetApiKeyAsync(string provider)
    {
        var secret = await _client.GetSecretAsync($"brainarr-{provider}-key");
        return secret.Value.Value;
    }
}
```

#### Using AWS Secrets Manager
```bash
# Store secret
aws secretsmanager create-secret \
  --name brainarr/api-keys \
  --secret-string '{"openai":"sk-...","anthropic":"sk-ant-..."}'

# Retrieve secret
aws secretsmanager get-secret-value \
  --secret-id brainarr/api-keys \
  --query SecretString \
  --output text
```

### Access Control

#### Principle of Least Privilege
```yaml
# Kubernetes Secret with RBAC
apiVersion: v1
kind: Secret
metadata:
  name: brainarr-api-keys
  namespace: lidarr
type: Opaque
data:
  openai-key: <base64-encoded>
  anthropic-key: <base64-encoded>

---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: brainarr-secret-reader
  namespace: lidarr
rules:
- apiGroups: [""]
  resources: ["secrets"]
  resourceNames: ["brainarr-api-keys"]
  verbs: ["get", "list"]
```

## Data Privacy

### Local-First Architecture

**Privacy Levels:**

| Provider | Data Transmission | Privacy Level | Recommendation |
|----------|------------------|---------------|----------------|
| Ollama | None (100% local) | 🟢 Maximum | Sensitive data |
| LM Studio | None (100% local) | 🟢 Maximum | Sensitive data |
| OpenRouter | Encrypted HTTPS | 🟡 Medium | General use |
| OpenAI | Encrypted HTTPS | 🟡 Medium | Non-sensitive |
| Anthropic | Encrypted HTTPS | 🟡 Medium | Non-sensitive |
| Gemini | Encrypted HTTPS | 🟡 Medium | Non-sensitive |

### Data Minimization

```csharp
public class PrivacyEnhancedAnalyzer
{
    public string SanitizeLibraryData(List<Artist> artists)
    {
        // Only send genre statistics, not specific artists
        var genreStats = artists
            .GroupBy(a => a.Genre)
            .Select(g => new { Genre = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(10); // Limit data exposure
        
        return JsonConvert.SerializeObject(genreStats);
    }
    
    public string AnonymizeUserData(LibraryProfile profile)
    {
        // Remove personally identifiable information
        return new
        {
            GenrePreferences = profile.TopGenres,
            ListeningPatterns = profile.Patterns,
            // Don't include: UserId, Email, Location, etc.
        }.ToJson();
    }
}
```

### GDPR Compliance

```csharp
public class GDPRCompliance
{
    // Right to be forgotten
    public async Task DeleteUserDataAsync(string userId)
    {
        // Clear all caches
        await _cache.RemoveByUserAsync(userId);
        
        // Delete recommendation history
        await _database.DeleteRecommendationHistoryAsync(userId);
        
        // Remove from logs
        await _logger.PurgeUserLogsAsync(userId);
        
        // Audit trail
        await _auditLog.LogDeletionAsync(userId, DateTime.UtcNow);
    }
    
    // Data portability
    public async Task<string> ExportUserDataAsync(string userId)
    {
        var data = new
        {
            Recommendations = await _database.GetUserRecommendationsAsync(userId),
            Preferences = await _database.GetUserPreferencesAsync(userId),
            ExportDate = DateTime.UtcNow
        };
        
        return JsonConvert.SerializeObject(data, Formatting.Indented);
    }
}
```

## Network Security

### TLS/SSL Configuration

```nginx
# Nginx SSL configuration for Lidarr
server {
    listen 443 ssl http2;
    server_name lidarr.example.com;
    
    # Strong SSL configuration
    ssl_certificate /etc/ssl/certs/lidarr.crt;
    ssl_certificate_key /etc/ssl/private/lidarr.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    
    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options nosniff;
    add_header X-Frame-Options DENY;
    add_header X-XSS-Protection "1; mode=block";
    
    location / {
        proxy_pass http://localhost:8686;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### Firewall Rules

```bash
# UFW firewall configuration
# Allow only local access to AI providers
ufw allow from 127.0.0.1 to any port 11434 comment 'Ollama local only'
ufw allow from 127.0.0.1 to any port 1234 comment 'LM Studio local only'

# Allow Lidarr from specific IPs only
ufw allow from 192.168.1.0/24 to any port 8686 comment 'Lidarr internal only'

# Deny all other traffic
ufw default deny incoming
ufw default allow outgoing
ufw enable
```

### API Endpoint Security

```csharp
public class SecureEndpoint
{
    private readonly RateLimiter _rateLimiter;
    private readonly IpWhitelist _whitelist;
    
    public async Task<IActionResult> HandleRequest(HttpRequest request)
    {
        // IP whitelisting
        if (!_whitelist.IsAllowed(request.RemoteIpAddress))
        {
            return new StatusCodeResult(403);
        }
        
        // Rate limiting
        if (!await _rateLimiter.AllowRequestAsync(request.RemoteIpAddress))
        {
            return new StatusCodeResult(429);
        }
        
        // Input validation
        if (!ValidateInput(request.Body))
        {
            return new BadRequestResult();
        }
        
        // Process request
        return await ProcessSecureRequest(request);
    }
}
```

## Configuration Security

### Secure Defaults

```csharp
public class SecureDefaults
{
    public class BrainarrSecureSettings
    {
        // Secure by default
        public bool UseLocalProvidersOnly { get; set; } = true;
        public bool EnableTelemetry { get; set; } = false;
        public bool LogSensitiveData { get; set; } = false;
        public int MaxRequestsPerMinute { get; set; } = 10;
        public bool RequireAuthentication { get; set; } = true;
        public string[] AllowedOrigins { get; set; } = new[] { "localhost" };
    }
}
```

### Configuration Encryption

```csharp
public class EncryptedConfiguration
{
    private readonly byte[] _key;
    
    public void SaveSecureConfig(BrainarrSettings settings)
    {
        // Encrypt sensitive fields
        settings.ApiKey = Encrypt(settings.ApiKey);
        
        // Save to file with restricted permissions
        var json = JsonConvert.SerializeObject(settings);
        File.WriteAllText("/etc/lidarr/brainarr.conf", json);
        
        // Set file permissions (600 - owner read/write only)
        File.SetUnixFileMode("/etc/lidarr/brainarr.conf", 
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    
    private string Encrypt(string plainText)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = _key;
            aes.GenerateIV();
            
            var encryptor = aes.CreateEncryptor();
            var encrypted = encryptor.TransformFinalBlock(
                Encoding.UTF8.GetBytes(plainText), 0, plainText.Length);
            
            return Convert.ToBase64String(aes.IV.Concat(encrypted).ToArray());
        }
    }
}
```

### File Permissions

```bash
# Secure file permissions
chmod 600 /etc/lidarr/brainarr.conf  # Config file - owner only
chmod 755 /var/lib/lidarr/plugins/RicherTunes/Brainarr  # Plugin directory
chmod 644 /var/lib/lidarr/plugins/RicherTunes/Brainarr/*.dll  # DLL files

# Ownership
chown lidarr:lidarr /etc/lidarr/brainarr.conf
chown -R lidarr:lidarr /var/lib/lidarr/plugins/RicherTunes/Brainarr
```

## Deployment Security

### Container Security

```dockerfile
# Secure Dockerfile
FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine AS runtime

# Non-root user
RUN addgroup -g 1000 lidarr && \
    adduser -u 1000 -G lidarr -s /bin/sh -D lidarr

# Copy application
WORKDIR /app
COPY --chown=lidarr:lidarr ./dist .

# Security hardening
RUN apk update && \
    apk upgrade && \
    apk add --no-cache dumb-init && \
    rm -rf /var/cache/apk/*

# Read-only root filesystem
RUN chmod -R 755 /app && \
    mkdir -p /tmp/lidarr && \
    chown -R lidarr:lidarr /tmp/lidarr

USER lidarr

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8686/health"]

ENTRYPOINT ["dumb-init", "--"]
CMD ["dotnet", "Lidarr.Plugin.Brainarr.dll"]
```

### Kubernetes Security

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: lidarr-brainarr
spec:
  securityContext:
    runAsNonRoot: true
    runAsUser: 1000
    fsGroup: 1000
    seccompProfile:
      type: RuntimeDefault
  
  containers:
  - name: lidarr
    image: lidarr-brainarr:latest
    
    securityContext:
      allowPrivilegeEscalation: false
      readOnlyRootFilesystem: true
      capabilities:
        drop:
        - ALL
        add:
        - NET_BIND_SERVICE
    
    resources:
      limits:
        memory: "512Mi"
        cpu: "500m"
      requests:
        memory: "256Mi"
        cpu: "250m"
    
    volumeMounts:
    - name: tmp
      mountPath: /tmp
    - name: config
      mountPath: /config
      readOnly: true
    
  volumes:
  - name: tmp
    emptyDir: {}
  - name: config
    secret:
      secretName: brainarr-config
      defaultMode: 0400
```

## Audit and Compliance

### Security Logging

```csharp
public class SecurityAuditLogger
{
    private readonly ILogger<SecurityAuditLogger> _logger;
    
    public void LogApiKeyUsage(string provider, string userId, bool success)
    {
        _logger.LogInformation(
            "API_KEY_USAGE Provider={Provider} User={UserId} Success={Success} Time={Time}",
            provider, HashUserId(userId), success, DateTime.UtcNow);
    }
    
    public void LogSuspiciousActivity(string activity, string source)
    {
        _logger.LogWarning(
            "SUSPICIOUS_ACTIVITY Activity={Activity} Source={Source} Time={Time}",
            activity, source, DateTime.UtcNow);
        
        // Alert security team
        SendSecurityAlert(activity, source);
    }
    
    private string HashUserId(string userId)
    {
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(userId));
            return Convert.ToBase64String(hash);
        }
    }
}
```

### Vulnerability Scanning

```bash
# Dependency scanning
dotnet list package --vulnerable --include-transitive

# Container scanning
trivy image lidarr-brainarr:latest

# SAST scanning
dotnet tool install --global security-scan
security-scan /path/to/brainarr

# Secret scanning
gitleaks detect --source . --verbose
```

### Security Monitoring

```yaml
# Prometheus alerts for security monitoring
groups:
- name: brainarr_security
  rules:
  - alert: HighFailedAuthRate
    expr: rate(brainarr_auth_failures_total[5m]) > 10
    for: 5m
    annotations:
      summary: "High authentication failure rate"
      
  - alert: UnusualAPIUsage
    expr: rate(brainarr_api_calls_total[1h]) > 1000
    for: 10m
    annotations:
      summary: "Unusual API usage pattern detected"
      
  - alert: SuspiciousProviderSwitch
    expr: increase(brainarr_provider_switches_total[10m]) > 5
    for: 5m
    annotations:
      summary: "Rapid provider switching detected"
```

## Incident Response

### Security Incident Playbook

1. **Detection**
   - Monitor logs for suspicious activity
   - Set up alerts for anomalies
   - Regular security audits

2. **Containment**
   ```bash
   # Immediately revoke compromised keys
   systemctl stop lidarr
   rm /etc/lidarr/brainarr.conf
   ```

3. **Eradication**
   - Rotate all API keys
   - Update to latest version
   - Patch vulnerabilities

4. **Recovery**
   - Deploy clean configuration
   - Restore from secure backup
   - Monitor closely

5. **Lessons Learned**
   - Document incident
   - Update security procedures
   - Implement preventive measures

## Security Checklist

### Development
- [ ] No hardcoded secrets in code
- [ ] All dependencies up to date
- [ ] Security scanning in CI/CD
- [ ] Code review for security issues
- [ ] Input validation on all endpoints

### Deployment
- [ ] API keys in secure storage
- [ ] TLS/SSL properly configured
- [ ] Firewall rules configured
- [ ] File permissions set correctly
- [ ] Running as non-root user

### Runtime
- [ ] Regular key rotation schedule
- [ ] Security monitoring enabled
- [ ] Audit logging configured
- [ ] Rate limiting active
- [ ] Backup and recovery tested

### Compliance
- [ ] GDPR compliance verified
- [ ] Data retention policies defined
- [ ] Privacy policy updated
- [ ] Security documentation current
- [ ] Incident response plan ready

## Additional Resources

- [OWASP Security Guidelines](https://owasp.org/www-project-application-security-verification-standard/)
- [NIST Cybersecurity Framework](https://www.nist.gov/cyberframework)
- [CIS Security Controls](https://www.cisecurity.org/controls)
- [API Security Best Practices](https://owasp.org/www-project-api-security/)
## Secure JSON Handling

Brainarr processes JSON from two very different sources:
- Strict application JSON (configs, typed models)
- Provider outputs (LLM responses, free-form text that may contain HTML-like literals)

To balance safety with robustness, the serializer exposes two modes:

- Strict: `SecureJsonSerializer.Deserialize/ParseDocument`
  - Applies heuristic content checks for known attack vectors (e.g., `<script>`, `javascript:`, `__proto__`, `$ref`)
  - Enforces size and depth limits
  - Used for application-owned JSON only

- Relaxed: `SecureJsonSerializer.ParseDocumentRelaxed`
  - Skips heuristic string-pattern checks but preserves size and depth limits
  - Intended for provider responses where HTML-like strings are treated as data
  - Always pair with downstream sanitization before rendering or logging

Sanitization is performed by `RecommendationSanitizer`, which removes XSS, SQLi, path traversal and control characters, clamps confidence, and normalizes Unicode. Critical fields (artist/album) are validated against malicious patterns pre-sanitization to prevent tainted identifiers from entering the pipeline.
