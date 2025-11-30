# Implementation Plan: Claude Code & OpenAI Codex Subscription Providers

## Overview

Add support for Claude Code and OpenAI Codex subscription-based authentication to Brainarr, allowing users to leverage their existing subscriptions (Claude Max, ChatGPT Team/Plus) instead of pay-per-API-call keys.

## Credential File Formats

### Claude Code (`~/.claude/.credentials.json`)
```json
{
  "claudeAiOauth": {
    "accessToken": "sk-ant-oat01-...",
    "refreshToken": "sk-ant-ort01-...",
    "expiresAt": 1764536045389,
    "subscriptionType": "max",
    "rateLimitTier": "default_claude_max_20x"
  }
}
```

### OpenAI Codex (`~/.codex/auth.json`)
```json
{
  "tokens": {
    "access_token": "eyJ...",
    "refresh_token": "rt_...",
    "account_id": "..."
  },
  "last_refresh": "2025-11-29T16:20:53Z"
}
```

## Implementation Steps

### Step 1: Add Provider Enum Values
**File:** `Brainarr.Plugin/Configuration/Enums.cs`

Add new enum values:
```csharp
public enum AIProvider
{
    // ... existing providers (0-8) ...
    ClaudeCodeSubscription = 9,
    OpenAICodexSubscription = 10
}
```

### Step 2: Create Credential Loader Service
**New File:** `Brainarr.Plugin/Services/Support/SubscriptionCredentialLoader.cs`

Creates a shared service for loading credentials from JSON files:
- Cross-platform path resolution (Windows/Linux/macOS)
- JSON parsing with error handling
- Token extraction from nested structures
- Expiration checking and warnings
- File existence validation

Key methods:
- `LoadClaudeCodeCredentials(string path)` - returns accessToken or null
- `LoadCodexCredentials(string path)` - returns access_token or null
- `IsTokenExpired(long expiresAt)` - checks Claude token expiry
- `GetDefaultCredentialsPath(SubscriptionType type)` - platform-aware defaults

### Step 3: Create Provider Settings Classes
**New Files:**
- `Brainarr.Plugin/Configuration/Providers/ClaudeCodeSubscriptionSettings.cs`
- `Brainarr.Plugin/Configuration/Providers/OpenAICodexSubscriptionSettings.cs`

Each contains:
- `CredentialsPath` property with platform-aware default
- `Model` property with sensible default
- Standard settings (Temperature, MaxTokens, Timeout)
- FluentValidation validator

### Step 4: Add Settings Properties to BrainarrSettings
**File:** `Brainarr.Plugin/BrainarrSettings.cs`

Add new properties:
```csharp
// Claude Code Subscription
public string ClaudeCodeCredentialsPath { get; set; } = GetDefaultClaudePath();
public string? ClaudeCodeModelId { get; set; }

// OpenAI Codex Subscription
public string OpenAICodexCredentialsPath { get; set; } = GetDefaultCodexPath();
public string? OpenAICodexModelId { get; set; }
```

Add UI field definitions with conditional visibility (shown only when provider is selected).

### Step 5: Create Provider Implementations
**New Files:**
- `Brainarr.Plugin/Services/Providers/ClaudeCodeSubscriptionProvider.cs`
- `Brainarr.Plugin/Services/Providers/OpenAICodexSubscriptionProvider.cs`

Both providers:
1. Accept credentials path in constructor (not API key)
2. Use `SubscriptionCredentialLoader` to load token from file
3. Use standard API endpoints (Anthropic/OpenAI)
4. Include user-friendly error messages for missing/expired credentials
5. Implement token refresh warning (when close to expiry)

**ClaudeCodeSubscriptionProvider:**
- Endpoint: `https://api.anthropic.com/v1/messages`
- Auth header: `x-api-key: {accessToken}`
- Default model: `claude-sonnet-4-5-20250514`

**OpenAICodexSubscriptionProvider:**
- Endpoint: `https://api.openai.com/v1/chat/completions`
- Auth header: `Authorization: Bearer {access_token}`
- Default model: `gpt-4o`

### Step 6: Register Providers in Registry
**File:** `Brainarr.Plugin/Services/Core/ProviderRegistry.cs`

Add factory registrations:
```csharp
Register(AIProvider.ClaudeCodeSubscription, (settings, http, logger) =>
    new ClaudeCodeSubscriptionProvider(
        http, logger,
        settings.ClaudeCodeCredentialsPath,
        settings.ClaudeCodeModelId ?? "claude-sonnet-4-5-20250514",
        httpExec: _httpExec));

Register(AIProvider.OpenAICodexSubscription, (settings, http, logger) =>
    new OpenAICodexSubscriptionProvider(
        http, logger,
        settings.OpenAICodexCredentialsPath,
        settings.OpenAICodexModelId ?? "gpt-4o",
        httpExec: _httpExec));
```

### Step 7: Update Provider Availability Check
**File:** `Brainarr.Plugin/Services/Core/AIProviderFactory.cs`

Add availability checks:
```csharp
AIProvider.ClaudeCodeSubscription =>
    File.Exists(ExpandPath(settings.ClaudeCodeCredentialsPath)),
AIProvider.OpenAICodexSubscription =>
    File.Exists(ExpandPath(settings.OpenAICodexCredentialsPath)),
```

### Step 8: Add Unit Tests
**New Files:**
- `Brainarr.Tests/Services/ClaudeCodeSubscriptionProviderTests.cs`
- `Brainarr.Tests/Services/OpenAICodexSubscriptionProviderTests.cs`
- `Brainarr.Tests/Services/Support/SubscriptionCredentialLoaderTests.cs`

Test coverage:
- Credential loading from valid files
- Missing file handling
- Invalid JSON handling
- Token extraction
- Expired token detection
- API request/response handling
- Error scenarios

## File Changes Summary

### New Files (7)
1. `Services/Support/SubscriptionCredentialLoader.cs`
2. `Configuration/Providers/ClaudeCodeSubscriptionSettings.cs`
3. `Configuration/Providers/OpenAICodexSubscriptionSettings.cs`
4. `Services/Providers/ClaudeCodeSubscriptionProvider.cs`
5. `Services/Providers/OpenAICodexSubscriptionProvider.cs`
6. `Tests/Services/ClaudeCodeSubscriptionProviderTests.cs`
7. `Tests/Services/OpenAICodexSubscriptionProviderTests.cs`

### Modified Files (4)
1. `Configuration/Enums.cs` - Add enum values
2. `BrainarrSettings.cs` - Add settings properties and UI fields
3. `Services/Core/ProviderRegistry.cs` - Register new providers
4. `Services/Core/AIProviderFactory.cs` - Add availability checks

## Security Considerations

1. **Token Redaction**: Never log full tokens, only first 8 chars
2. **File Permissions**: Warn if credential files are world-readable
3. **Expiration Handling**: Check token expiry before each request
4. **Path Validation**: Prevent directory traversal attacks in path settings
5. **Secure Storage**: Tokens loaded in memory only when needed

## UI/UX

When user selects "Claude Code Subscription" or "OpenAI Codex Subscription":
- Show credentials path field with platform-aware default
- Show "Test Connection" that validates both file exists and token works
- Show helpful error messages:
  - "Credentials file not found. Run `claude login` or `codex auth login`"
  - "Token expired. Run `claude login` to refresh"
  - "Invalid credentials format. Expected JSON with accessToken field"

## Testing Strategy

1. **Unit Tests**: Mock file system and HTTP client
2. **Integration Tests**: Test with real credential files (CI skip)
3. **Manual Testing**: Verify end-to-end with real subscriptions

## Rollout

1. Implement behind feature flag initially (optional)
2. Document in README and CHANGELOG
3. Add to provider comparison documentation
