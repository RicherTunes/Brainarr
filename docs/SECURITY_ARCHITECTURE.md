# Security Architecture - Brainarr Plugin

## Overview

Brainarr implements defense-in-depth security measures to protect sensitive data, prevent attacks, and ensure safe operation within the Lidarr ecosystem. This document details the security components and their implementation.

## Security Components

### 1. SecureApiKeyStorage

**Location:** `Services/Security/SecureApiKeyStorage.cs`

**Purpose:** Secure storage and management of API keys with encryption and memory protection.

**Security Features:**
- **Memory Protection:** API keys stored in `SecureString` to prevent memory dumps
- **Platform-Specific Encryption:**
  - Windows: DPAPI (Data Protection API) with user-scope encryption
  - Linux/Mac: AES-256 CBC mode with SHA-256 derived keys
- **Automatic Memory Clearing:** Sensitive data cleared from memory after use
- **Thread Safety:** All operations protected with locking mechanisms
- **Entropy-Based Security:** 32-byte cryptographically secure entropy for key derivation

**Usage Example:**
```csharp
// Store API key securely
storage.StoreApiKey("OpenAI", apiKey);

// Use API key with automatic cleanup
var result = await storage.UseApiKeyAsync("OpenAI", async (key) => {
    return await httpClient.SendWithApiKey(key);
});
```

### 2. SecureHttpClient

**Location:** `Services/Security/SecureHttpClient.cs`

**Purpose:** Secure HTTP communication with certificate validation and TLS enforcement.

**Security Features:**
- **TLS 1.2+ Enforcement:** Rejects connections using older TLS versions
- **Certificate Validation:** Custom certificate validation with pinning support
- **Request Sanitization:** Removes sensitive headers from logs
- **Response Validation:** Validates content types and sizes
- **Timeout Protection:** Prevents indefinite hangs

### 3. CertificateValidator

**Location:** `Services/Security/CertificateValidator.cs`

**Purpose:** X.509 certificate validation with optional pinning.

**Security Features:**
- **Chain Validation:** Validates full certificate chain
- **Expiration Checking:** Rejects expired certificates
- **Hostname Verification:** Ensures certificate matches requested host
- **Certificate Pinning:** Optional pinning for high-security providers
- **Revocation Checking:** CRL/OCSP validation when available

### 4. InputSanitizer

**Location:** `Services/Security/InputSanitizer.cs`

**Purpose:** Sanitizes user input and API responses to prevent injection attacks.

**Security Features:**
- **SQL Injection Prevention:** Escapes SQL special characters
- **Command Injection Prevention:** Blocks shell metacharacters
- **Path Traversal Prevention:** Validates and normalizes file paths
- **XSS Prevention:** HTML entity encoding for web contexts
- **JSON Injection Prevention:** Validates JSON structure

**Sanitization Methods:**
```csharp
// Sanitize user input
var safeName = InputSanitizer.SanitizeArtistName(userInput);
var safePath = InputSanitizer.SanitizePath(filePath);
var safeJson = InputSanitizer.ValidateJson(jsonString);
```

### 5. SecureJsonSerializer

**Location:** `Services/Security/SecureJsonSerializer.cs`

**Purpose:** Safe JSON serialization/deserialization with attack prevention.

**Security Features:**
- **Type Whitelisting:** Only deserializes allowed types
- **Depth Limiting:** Prevents stack overflow from deeply nested JSON
- **Size Limiting:** Rejects oversized payloads
- **Polymorphic Type Handling:** Safe handling of inheritance
- **Reference Loop Detection:** Prevents infinite loops

**Configuration:**
```csharp
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.None, // Prevent type injection
    MaxDepth = 32, // Limit nesting depth
    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
};
```

### 6. ThreadSafeRateLimiter

**Location:** `Services/Security/ThreadSafeRateLimiter.cs`

**Purpose:** Thread-safe rate limiting to prevent API abuse and DOS attacks.

**Security Features:**
- **Per-Provider Limits:** Different rate limits for each AI provider
- **Sliding Window Algorithm:** Accurate rate tracking
- **Thread-Safe Operations:** Lock-free implementation using ConcurrentDictionary
- **Automatic Cleanup:** Expired entries removed to prevent memory leaks
- **Burst Protection:** Prevents rapid successive requests

**Rate Limit Configuration:**
```csharp
var limits = new Dictionary<string, RateLimitConfig>
{
    ["openai"] = new RateLimitConfig { RequestsPerMinute = 60, BurstSize = 10 },
    ["anthropic"] = new RateLimitConfig { RequestsPerMinute = 50, BurstSize = 5 },
    ["local"] = new RateLimitConfig { RequestsPerMinute = 1000, BurstSize = 100 }
};
```

### 7. MusicBrainzRateLimiter

**Location:** `Services/Security/MusicBrainzRateLimiter.cs`

**Purpose:** Specialized rate limiter for MusicBrainz API compliance.

**Security Features:**
- **API Compliance:** Respects MusicBrainz rate limits (1 req/sec)
- **User-Agent Enforcement:** Ensures proper identification
- **Retry-After Handling:** Respects server throttling headers
- **Distributed Limiting:** Coordinates across multiple instances

## Security Best Practices

### API Key Management

1. **Never Log API Keys:** Use `[Sensitive]` attribute on fields
2. **Immediate Encryption:** Encrypt keys as soon as received
3. **Minimal Exposure:** Convert to plain text only when needed
4. **Automatic Cleanup:** Use `using` blocks or try-finally patterns

### Input Validation

1. **Whitelist Approach:** Define allowed characters/patterns
2. **Length Limits:** Enforce maximum lengths on all inputs
3. **Type Validation:** Verify data types before processing
4. **Encoding Validation:** Check character encoding (UTF-8)

### Network Security

1. **HTTPS Only:** Never transmit sensitive data over HTTP
2. **Certificate Validation:** Always validate SSL certificates
3. **Timeout Settings:** Set appropriate timeouts on all requests
4. **Retry Limits:** Prevent infinite retry loops

### Error Handling

1. **Generic Errors:** Don't expose internal details in errors
2. **Log Sanitization:** Remove sensitive data from logs
3. **Rate Limit Errors:** Return appropriate 429 responses
4. **Security Events:** Log security-relevant events separately

## Threat Model

### Identified Threats

| Threat | Mitigation | Component |
|--------|------------|-----------|
| API Key Theft | Encryption, SecureString | SecureApiKeyStorage |
| Man-in-the-Middle | TLS, Certificate Pinning | SecureHttpClient |
| Injection Attacks | Input Sanitization | InputSanitizer |
| Deserialization Attacks | Type Whitelisting | SecureJsonSerializer |
| DOS/Rate Limit Abuse | Rate Limiting | ThreadSafeRateLimiter |
| Memory Dumps | SecureString, Clearing | SecureApiKeyStorage |
| Path Traversal | Path Validation | InputSanitizer |
| Certificate Spoofing | Chain Validation | CertificateValidator |

### Security Boundaries

1. **Plugin Boundary:** Isolated from Lidarr core
2. **Provider Boundary:** Each provider isolated
3. **Network Boundary:** All external communication encrypted
4. **Memory Boundary:** Sensitive data in protected memory

## Compliance & Standards

### Standards Compliance

- **OWASP Top 10:** Addresses all relevant vulnerabilities
- **CWE/SANS Top 25:** Implements recommended mitigations
- **PCI DSS:** API key handling follows PCI guidelines
- **.NET Security Guidelines:** Follows Microsoft security best practices

### Cryptographic Standards

- **Encryption:** AES-256 (NIST approved)
- **Hashing:** SHA-256 (NIST approved)
- **Random Generation:** System.Security.Cryptography.RandomNumberGenerator
- **Key Derivation:** PBKDF2 with sufficient iterations

## Security Testing

### Automated Testing

```bash
# Run security-focused tests
dotnet test --filter Category=Security

# Static analysis
dotnet tool run security-scan

# Dependency vulnerability scan
dotnet list package --vulnerable
```

### Manual Testing Checklist

- [ ] API keys not visible in logs
- [ ] HTTPS enforcement working
- [ ] Rate limiting prevents abuse
- [ ] Input sanitization blocks injections
- [ ] Certificate validation rejects invalid certs
- [ ] Memory cleared after sensitive operations
- [ ] Error messages don't leak information

## Incident Response

### Security Issue Reporting

1. **Email:** security@brainarr.com (encrypted)
2. **GitHub:** Private security advisory
3. **Response Time:** 24-48 hours

### Update Process

1. **Critical:** Immediate patch release
2. **High:** Within 7 days
3. **Medium:** Within 30 days
4. **Low:** Next regular release

## Future Improvements

### Planned Enhancements

1. **Hardware Security Module (HSM) Support:** For enterprise deployments
2. **Azure Key Vault Integration:** Cloud key management
3. **OAuth 2.0 Support:** Modern authentication for providers
4. **Audit Logging:** Comprehensive security event logging
5. **Mutual TLS:** Client certificate authentication

### Research Areas

1. **Homomorphic Encryption:** Process encrypted recommendations
2. **Zero-Knowledge Proofs:** Verify without exposing data
3. **Secure Multi-Party Computation:** Distributed processing
4. **Post-Quantum Cryptography:** Future-proof encryption

## Security Checklist for Developers

### Before Committing Code

- [ ] No hardcoded secrets or API keys
- [ ] All inputs validated and sanitized
- [ ] Sensitive data uses SecureString
- [ ] Network calls use SecureHttpClient
- [ ] Error messages are generic
- [ ] Logging excludes sensitive data
- [ ] Unit tests cover security scenarios
- [ ] Documentation updated if needed

### Code Review Focus

- [ ] Authentication/authorization logic
- [ ] Cryptographic implementations
- [ ] Input validation boundaries
- [ ] Error handling paths
- [ ] Third-party dependencies
- [ ] Configuration security

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [.NET Security Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/security/)
- [NIST Cryptographic Standards](https://csrc.nist.gov/projects/cryptographic-standards-and-guidelines)
- [CWE/SANS Top 25](https://cwe.mitre.org/top25/)