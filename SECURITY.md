# 🔒 Security Policy

## 📋 Table of Contents

- [🛡️ Security Overview](#️-security-overview)
- [🚨 Reporting Security Vulnerabilities](#-reporting-security-vulnerabilities)
- [🔐 Supported Versions](#-supported-versions)
- [🔍 Security Features](#-security-features)
- [🏠 Privacy & Data Protection](#-privacy--data-protection)
- [⚠️ Known Security Considerations](#️-known-security-considerations)
- [🛠️ Security Best Practices](#️-security-best-practices)
- [📋 Security Checklist](#-security-checklist)

---

## 🛡️ Security Overview

Brainarr takes security and privacy seriously. This document outlines our security practices, how to report vulnerabilities, and security considerations for users.

### 🎯 Security Principles

1. **Privacy by Design**: Local-first approach minimizes data exposure
2. **Secure by Default**: Safe default configurations
3. **Minimal Data**: Only necessary music metadata is processed
4. **Transparency**: Open source code for full auditability
5. **No Telemetry**: Zero data collection or tracking

---

## 🚨 Reporting Security Vulnerabilities

### 📧 How to Report

**DO NOT** report security vulnerabilities through public GitHub issues.

Instead, please report them responsibly:

1. **Email**: Send details to `security@brainarr.dev` (if available)
2. **GitHub Security**: Use [GitHub's private vulnerability reporting](https://github.com/yourusername/brainarr/security/advisories/new)
3. **Encrypted Communication**: Use PGP if sensitive details are involved

### 📝 What to Include

Please include as much information as possible:

- **Type of issue**: Authentication bypass, injection, etc.
- **Full paths** of source files related to the vulnerability
- **Location** of the affected source code (tag/branch/commit)
- **Special configuration** required to reproduce the issue
- **Step-by-step instructions** to reproduce the issue
- **Proof-of-concept or exploit code** (if possible)
- **Impact** of the issue, including how an attacker might exploit it

### ⏱️ Response Timeline

- **Initial Response**: Within 48 hours
- **Confirmation**: Within 1 week
- **Fix Development**: Varies by severity
- **Public Disclosure**: After fix is released and users have time to update

### 🏆 Acknowledgments

Security researchers who responsibly disclose vulnerabilities will be:
- Credited in the security advisory (if desired)
- Listed in our security acknowledgments
- Invited to review the fix before release

---

## 🔐 Supported Versions

| Version | Supported | Notes |
|---------|-----------|-------|
| 1.0.x   | ✅ | Current stable release |
| 0.x.x   | ❌ | Pre-release versions, no longer supported |

**Security updates** are provided for the current major version only. Users are strongly encouraged to upgrade to the latest version.

---

## 🔍 Security Features

### 🔒 Built-in Security Measures

#### API Key Protection
```csharp
[FieldDefinition(Label = "API Key", Privacy = PrivacyLevel.Password)]
public string ApiKey { get; set; }
```
- API keys are stored with password-level privacy
- Never logged or displayed in plain text
- Encrypted storage via Lidarr's security system

#### Input Validation
```csharp
RuleFor(c => c.OllamaUrl)
    .Must(BeValidUrl)
    .WithMessage("Please enter a valid URL");
```
- All user inputs are validated using FluentValidation
- URL validation prevents malicious endpoints
- Sanitization of all configuration values

#### Safe HTTP Clients
- Uses Lidarr's `IHttpClient` with built-in protections
- Automatic timeout handling
- Protection against SSRF attacks via Lidarr's safeguards

#### Rate Limiting
```csharp
public class RateLimiter
{
    // Prevents API abuse and DoS attacks
    private readonly Dictionary<string, TokenBucket> _buckets;
}
```
- Per-provider rate limiting prevents abuse
- Configurable limits respect API quotas
- Protection against accidental DoS of AI providers

### 🛡️ Security Architecture

#### Secure Provider Pattern
```csharp
public interface IAIProvider
{
    Task<bool> TestConnectionAsync();  // Safe connection testing
    Task<ServiceResult<List<ImportListItemInfo>>> GetRecommendationsAsync();
}
```
- Standardized provider interface reduces attack surface
- Consistent error handling prevents information leakage
- Secure communication patterns

#### Defensive Coding
- Comprehensive exception handling
- Safe JSON parsing with validation
- No dynamic code execution or eval
- Input sanitization at all boundaries

---

## 🏠 Privacy & Data Protection

### 📊 Data Collection Policy

**What Brainarr DOES collect:**
- Your music library metadata (artist names, album titles) - **only processed locally or sent to chosen AI provider**
- Configuration settings - **stored locally in Lidarr database**

**What Brainarr DOES NOT collect:**
- ❌ No telemetry or usage analytics
- ❌ No personal information
- ❌ No music files or content
- ❌ No listening habits or patterns
- ❌ No data sent to Brainarr developers

### 🔐 Data Processing

#### Local Providers (Ollama, LM Studio)
- ✅ **100% Private**: No data leaves your network
- ✅ **No Internet Required**: Processing happens locally
- ✅ **Full Control**: You control the AI model and data

#### Cloud Providers (OpenAI, Anthropic, etc.)
- ⚠️ **Music Metadata**: Artist/album names sent to AI provider
- ⚠️ **Provider Policies**: Subject to each provider's privacy policy
- ⚠️ **Network Transit**: Data encrypted in transit (HTTPS)
- ✅ **No Files**: Only metadata, never audio files
- ✅ **Minimal Data**: Only necessary information for recommendations

### 🔒 Data Security

#### Storage Security
- Configuration encrypted by Lidarr's security system
- API keys stored with maximum privacy protection
- No sensitive data in logs (in production builds)
- Temporary data cleared after processing

#### Network Security
- All API communications over HTTPS/TLS
- Certificate validation enforced
- No insecure HTTP connections to cloud providers
- Local providers can use HTTP (localhost only)

---

## ⚠️ Known Security Considerations

### 🌐 Network-Based Risks

#### Cloud Provider Dependencies
- **Risk**: Cloud providers could log or store your music metadata
- **Mitigation**: Use local providers (Ollama) for maximum privacy
- **Assessment**: Low risk - only artist/album names, no personal data

#### API Key Management
- **Risk**: API keys stored in Lidarr database
- **Mitigation**: Use Lidarr's encrypted storage, restrict database access
- **Assessment**: Standard risk - same as other Lidarr API integrations

#### Network Interception
- **Risk**: HTTPS traffic could theoretically be intercepted
- **Mitigation**: Use local providers or trusted networks
- **Assessment**: Very low risk - standard HTTPS protection

### 🏠 Local Security Considerations

#### Ollama Security
- **Risk**: Ollama runs a local web server (port 11434)
- **Mitigation**: Firewall rules, localhost-only binding
- **Assessment**: Low risk - standard for local AI services

#### File System Access
- **Risk**: Brainarr reads Lidarr's music library database
- **Mitigation**: Uses Lidarr's built-in API, no direct file access
- **Assessment**: No additional risk beyond Lidarr itself

### 🔍 Dependency Risks

Current dependencies are regularly scanned for vulnerabilities:
- **NLog**: Logging framework - standard security practices
- **Newtonsoft.Json**: JSON parsing - well-maintained, secure
- **FluentValidation**: Input validation - security-focused library

---

## 🛠️ Security Best Practices

### 🔒 For Users

#### API Key Security
1. **Generate new keys** specifically for Brainarr
2. **Use minimum permissions** required for the service
3. **Rotate keys regularly** (every 3-6 months)
4. **Don't share keys** or commit them to version control
5. **Monitor usage** through provider dashboards

#### Network Security
1. **Use HTTPS** for all cloud provider connections
2. **Secure your Lidarr instance** with authentication
3. **Keep Lidarr updated** to latest security patches
4. **Use VPN** if accessing remotely

#### Privacy Protection
1. **Prefer local providers** (Ollama) for maximum privacy
2. **Review provider privacy policies** before using cloud services
3. **Use dedicated API keys** not shared with other services
4. **Monitor what data is sent** in debug logs (temporarily)

### 🏢 For Administrators

#### System Security
1. **Regular Updates**: Keep Lidarr and Brainarr updated
2. **Access Control**: Restrict who can modify Brainarr settings
3. **Network Segmentation**: Isolate media servers when possible
4. **Backup Security**: Secure backups of Lidarr configuration

#### Monitoring
1. **Log Review**: Monitor Lidarr logs for unusual activity
2. **Network Monitoring**: Watch for unexpected outbound connections
3. **API Usage**: Monitor provider API usage for anomalies

### 👨‍💻 For Developers

#### Code Security
1. **Input Validation**: All user inputs must be validated
2. **Safe HTTP**: Use Lidarr's IHttpClient, never raw HttpClient
3. **No Secrets in Code**: Never hardcode API keys or secrets
4. **Secure Defaults**: Default configurations should be secure
5. **Error Handling**: Don't leak sensitive information in errors

#### Dependency Management
1. **Regular Updates**: Keep dependencies current
2. **Vulnerability Scanning**: Use automated security scanning
3. **Minimal Dependencies**: Only include necessary packages
4. **Security Reviews**: Review new dependencies for security issues

---

## 📋 Security Checklist

### ✅ Installation Security

- [ ] Downloaded Brainarr from official sources
- [ ] Verified checksums/signatures (when available)
- [ ] Installed in secure Lidarr environment
- [ ] Reviewed and configured security settings

### ✅ Configuration Security

- [ ] Used strong, unique API keys
- [ ] Enabled HTTPS for cloud providers
- [ ] Set appropriate rate limits
- [ ] Configured secure local provider URLs
- [ ] Reviewed privacy settings

### ✅ Operational Security

- [ ] Regular updates of Brainarr and Lidarr
- [ ] Monitoring of API usage and costs
- [ ] Regular review of provider access logs
- [ ] Backup of configuration data
- [ ] Incident response plan for security issues

### ✅ Network Security

- [ ] Firewall rules for local providers
- [ ] Secure remote access to Lidarr
- [ ] Network monitoring for unusual activity
- [ ] VPN usage for remote access

---

## 📞 Security Contacts

### 🚨 Emergency Security Issues
For critical security vulnerabilities that pose immediate risk:
- **GitHub Security**: Use private vulnerability reporting
- **Response Time**: Within 24 hours for critical issues

### 📧 General Security Questions
For questions about security practices or policies:
- **GitHub Discussions**: [Security Category](https://github.com/yourusername/brainarr/discussions/categories/security)
- **Email**: `security@brainarr.dev` (if available)

### 🔍 Security Research
Interested in security research or contributing to security:
- **Contributing Guide**: [CONTRIBUTING.md](CONTRIBUTING.md)
- **Security Testing**: Help us test new security features

---

## 📜 Security Acknowledgments

We thank the following security researchers for responsibly disclosing vulnerabilities:

*No vulnerabilities have been reported yet - but we're grateful for the community's vigilance!*

---

## 📝 Changelog

| Date | Change | Impact |
|------|--------|--------|
| 2025-01-16 | Initial security policy | Established security practices |
| Future | Security updates | Will be documented here |

---

**📧 Questions?** Contact us through the appropriate channels listed above.

**🔄 This security policy is regularly reviewed and updated to reflect current best practices.**

---

*Last Updated: January 2025*  
*Next Review: April 2025*