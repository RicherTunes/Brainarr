# Provider Contract Checklist

## Purpose

This document defines the **mandatory test structure** and **acceptance gates** that every Brainarr AI provider must pass before ship. It establishes a consistent quality bar across all providers.

---

## Test File Layout (Required Structure)

Every provider MUST have these test files in the following structure:

```
Brainarr.Tests/
├── Services/Providers/
│   ├── Contracts/                          # Contract compliance (mandatory)
│   │   ├── ProviderContractTestBase.cs     # Shared base class
│   │   ├── ProviderContractTestHelpers.cs  # Shared mock helpers
│   │   ├── {Provider}ProviderContractTests.cs  # Per-provider contract tests
│   │   └── SecurityContractTests/          # Security-specific contracts
│   │       └── {Provider}SecurityContractTests.cs
│   ├── {Provider}ProviderTests.cs          # Provider-specific unit tests
│   └── {Provider}ProviderHermeticTests.cs  # Hermetic subprocess tests (CLI providers)
├── Configuration/Providers/
│   └── {Provider}SettingsTests.cs          # Settings validation tests
└── Integration/
    └── {Provider}IntegrationTests.cs       # E2E integration (optional for nightly)
```

---

## Acceptance Gates

### Gate 1: Contract Tests (REQUIRED - PR Blocking)

Every provider must inherit from `ProviderContractTestBase<T>` and pass ALL contract tests:

| Test | Description | Failure Behavior |
|------|-------------|------------------|
| `GetRecommendations_WithTimeout_ReturnsEmptyList` | Request times out | Empty list, no crash |
| `GetRecommendations_WithCancellation_ReturnsEmptyList` | CancellationToken fires | Empty list, no crash |
| `GetRecommendations_With429RateLimit_ReturnsEmptyList` | 429 Too Many Requests | Empty list, user message |
| `GetRecommendations_With401Unauthorized_ReturnsEmptyList` | 401 Unauthorized | Empty list, user message |
| `GetRecommendations_With403Forbidden_ReturnsEmptyList` | 403 Forbidden | Empty list, user message |
| `GetRecommendations_With500ServerError_ReturnsEmptyList` | 5xx Server Error | Empty list, retry hint |
| `GetRecommendations_WithMalformedJson_ReturnsEmptyList` | Invalid JSON response | Empty list, no crash |
| `GetRecommendations_WithEmptyResponse_ReturnsEmptyList` | Empty body | Empty list |
| `GetRecommendations_WithUnexpectedSchema_ReturnsEmptyList` | Wrong JSON schema | Empty list |
| `GetRecommendations_WithNullContent_ReturnsEmptyList` | Null in content field | Empty list |
| `GetRecommendations_WithNetworkError_ReturnsEmptyList` | Connection failed | Empty list |
| `GetRecommendations_WithValidResponse_ParsesRecommendations` | Happy path | Non-empty list |
| `TestConnection_With401_ReturnsFalse` | Auth check failure | false |
| `TestConnection_WithTimeout_ReturnsFalse` | Connection check timeout | false |

**Traits:** `[Trait("Category", "Contract")]`, `[Trait("Provider", "{ProviderName}")]`

### Gate 2: Security Contract Tests (REQUIRED - PR Blocking)

| Test | Description | Requirement |
|------|-------------|-------------|
| `ApiKey_NeverAppearsInLogs` | Log output sanitization | API keys redacted as `***` |
| `ApiKey_NeverAppearsInExceptions` | Exception message sanitization | No key in Message or StackTrace |
| `ApiKey_NeverAppearsInUserMessage` | User-facing error sanitization | GetLastUserMessage() clean |
| `Credentials_NotSerializedToJson` | Settings serialization | [JsonIgnore] on sensitive fields |
| `AuthHeader_NotLoggedAtInfoLevel` | HTTP header logging | Auth header redacted |

**Traits:** `[Trait("Category", "Security")]`, `[Trait("Provider", "{ProviderName}")]`

### Gate 3: Settings Validation Tests (REQUIRED - PR Blocking)

| Test | Description |
|------|-------------|
| `Validate_WithValidSettings_ReturnsValid` | Happy path validation |
| `Validate_WithEmptyApiKey_ReturnsInvalid` | Required field validation |
| `Validate_WithInvalidUrl_ReturnsInvalid` | URL format validation |
| `Validate_WithInvalidTemperature_ReturnsInvalid` | Range validation |
| `Validate_WithInvalidTimeout_ReturnsInvalid` | Timeout bounds (5-600s) |
| `Validate_WithInvalidMaxTokens_ReturnsInvalid` | Token limit validation |

**Traits:** `[Trait("Category", "Configuration")]`, `[Trait("Provider", "{ProviderName}")]`

### Gate 4: Provider-Specific Unit Tests (REQUIRED - PR Blocking)

Provider-specific behavior tests beyond the standard contract:

| Test Pattern | Description |
|--------------|-------------|
| `{Feature}_WithValidInput_ReturnsExpected` | Feature-specific happy paths |
| `{Feature}_WithEdgeCase_HandlesGracefully` | Edge case handling |
| `ModelSelection_WithValidId_SetsModel` | Model switching |
| `UpdateModel_WithNewModel_UpdatesProvider` | Dynamic model updates |

**Traits:** `[Trait("Category", "Unit")]`, `[Trait("Provider", "{ProviderName}")]`

### Gate 5: Hermetic E2E Tests (REQUIRED for CLI/subprocess providers)

For providers that invoke external processes (Claude Code, CLI tools):

| Test | Description |
|------|-------------|
| `Execute_WithMockedCli_ReturnsOutput` | Subprocess invocation mocked |
| `Execute_WithCliTimeout_ReturnsEmpty` | Process timeout handling |
| `Execute_WithCliCrash_ReturnsEmpty` | Process crash handling |
| `Execute_WithCliStderr_LogsWarning` | Stderr capture and logging |
| `ParseOutput_WithNdjson_StreamsCorrectly` | NDJSON streaming parse |
| `ParseOutput_WithPartialLine_BuffersCorrectly` | Partial output buffering |

**Traits:** `[Trait("Category", "Hermetic")]`, `[Trait("Provider", "{ProviderName}")]`

### Gate 6: Nightly Live Tests (OPTIONAL - Not PR Blocking)

Real API integration tests that run nightly (requires secrets):

| Test | Description |
|------|-------------|
| `Live_TestConnection_ReturnsTrue` | Real auth check |
| `Live_GetRecommendations_ReturnsResults` | Real recommendation fetch |
| `Live_RateLimit_HandlesGracefully` | Real rate limit handling |

**Traits:** `[Trait("Category", "Live")]`, `[Trait("Provider", "{ProviderName}")]`

**Note:** These are excluded from PR CI. Run via `dotnet test --filter Category=Live` in nightly workflow with appropriate secrets.

---

## Claude Code Provider Acceptance Gates

The Claude Code provider (`ClaudeCodeSubscriptionProvider`) has additional gates due to its CLI subprocess architecture:

### Required Tests (All must pass for ship)

```csharp
// Unit Tests - Mock all subprocess interactions
[Trait("Category", "Unit")]
[Trait("Provider", "ClaudeCode")]
public class ClaudeCodeProviderTests
{
    // Settings UX
    [Fact] public void Settings_CliPathValidation_RejectsInvalidPath() { }
    [Fact] public void Settings_AuthDetection_IdentifiesLoggedInState() { }

    // Health Check
    [Fact] public void TestConnection_WithCliPresent_ReturnsTrue() { }
    [Fact] public void TestConnection_WithCliMissing_ReturnsFalse() { }
    [Fact] public void TestConnection_WithAuthExpired_ReturnsFalse() { }

    // Non-Streaming (buffered output)
    [Fact] public void GetRecommendations_WithValidOutput_ParsesJson() { }
    [Fact] public void GetRecommendations_WithTimeout_ReturnsEmpty() { }
    [Fact] public void GetRecommendations_WithProcessCrash_ReturnsEmpty() { }
}

// Contract Tests - Standard error scenarios
[Trait("Category", "Contract")]
[Trait("Provider", "ClaudeCode")]
public class ClaudeCodeProviderContractTests : ProviderContractTestBase<ClaudeCodeSubscriptionProvider>
{
    // Inherits all 14 standard contract tests
}

// Security Tests - No secrets leak
[Trait("Category", "Security")]
[Trait("Provider", "ClaudeCode")]
public class ClaudeCodeSecurityContractTests
{
    [Fact] public void CliArgs_NeverContainSecrets() { }
    [Fact] public void ProcessOutput_RedactsSensitiveData() { }
    [Fact] public void ErrorLogs_NoCredentialLeakage() { }
}

// Hermetic Tests - Subprocess mocking
[Trait("Category", "Hermetic")]
[Trait("Provider", "ClaudeCode")]
public class ClaudeCodeProviderHermeticTests
{
    // NDJSON Streaming
    [Fact] public void ParseNdjson_WithCompleteLines_YieldsMessages() { }
    [Fact] public void ParseNdjson_WithPartialLine_BuffersUntilNewline() { }
    [Fact] public void ParseNdjson_WithMixedOutput_FiltersNonJson() { }

    // Process Lifecycle
    [Fact] public void StartProcess_WithValidPath_StartsSuccessfully() { }
    [Fact] public void StopProcess_WithTimeout_KillsGracefully() { }
    [Fact] public void StopProcess_WithHang_ForcesTermination() { }
}
```

### PR-Hermetic E2E Stub Gate

A minimal E2E test that proves the integration works without real CLI:

```csharp
[Trait("Category", "E2E")]
[Trait("Provider", "ClaudeCode")]
public class ClaudeCodeE2EStubTests
{
    [Fact]
    public async Task E2E_WithMockedCli_ProducesRecommendations()
    {
        // Arrange: Mock CLI that returns canned NDJSON
        var mockCli = CreateMockCliWithCannedOutput();
        var provider = new ClaudeCodeSubscriptionProvider(mockCli, settings, logger);

        // Act: Full recommendation flow
        var results = await provider.GetRecommendationsAsync("Test library");

        // Assert: Proves end-to-end parsing works
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.NotNull(r.Artist));
    }
}
```

---

## Test Trait Reference

| Trait | Value | Description |
|-------|-------|-------------|
| Category | `Contract` | Standard provider contract tests |
| Category | `Security` | Security/redaction tests |
| Category | `Configuration` | Settings validation tests |
| Category | `Unit` | Provider-specific unit tests |
| Category | `Hermetic` | Subprocess tests with mocked processes |
| Category | `E2E` | End-to-end integration tests |
| Category | `Live` | Real API tests (nightly only) |
| Provider | `{Name}` | Provider-specific filter |

## CI Workflow Integration

```yaml
# PR Gate (must pass)
- name: Run Contract Tests
  run: dotnet test --filter "Category=Contract|Category=Security|Category=Configuration|Category=Unit|Category=Hermetic|Category=E2E"

# Nightly (optional, with secrets)
- name: Run Live Tests
  run: dotnet test --filter "Category=Live"
  env:
    OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
    ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
```

---

## New Provider Checklist

When adding a new provider, copy this checklist:

- [ ] Create `Services/Providers/{Provider}/{Provider}Provider.cs`
- [ ] Create `Configuration/Providers/{Provider}Settings.cs`
- [ ] Create `Configuration/Providers/{Provider}SettingsValidator.cs`
- [ ] Create test files:
  - [ ] `Tests/Services/Providers/Contracts/{Provider}ProviderContractTests.cs`
  - [ ] `Tests/Services/Providers/Contracts/SecurityContractTests/{Provider}SecurityContractTests.cs`
  - [ ] `Tests/Services/Providers/{Provider}ProviderTests.cs`
  - [ ] `Tests/Configuration/Providers/{Provider}SettingsTests.cs`
  - [ ] (If CLI-based) `Tests/Services/Providers/{Provider}ProviderHermeticTests.cs`
- [ ] All 14 contract tests passing
- [ ] All 5 security tests passing
- [ ] Settings validation tests passing
- [ ] Provider-specific unit tests passing
- [ ] (If CLI-based) Hermetic tests passing
- [ ] Documentation updated in CLAUDE.md

---

## Diagnostic Output Shape (Test Connection)

All providers should produce consistent diagnostic output:

```json
{
  "success": true,
  "provider": "claudecode",
  "authMethod": "cli-session",
  "model": "claude-sonnet-4-20250514",
  "latencyMs": 234,
  "error": null
}
```

Or on failure:

```json
{
  "success": false,
  "provider": "claudecode",
  "authMethod": "cli-session",
  "model": null,
  "latencyMs": 5000,
  "error": {
    "code": "CLI-001",
    "message": "Claude Code CLI not found at configured path",
    "hint": "Install Claude Code CLI: npm install -g @anthropic-ai/claude-code"
  }
}
```

---

## Error Code Registry

| Code | Provider | Description |
|------|----------|-------------|
| `AUTH-001` | All | Invalid API key / credentials |
| `AUTH-002` | All | Expired credentials |
| `AUTH-003` | All | Insufficient permissions |
| `RATE-001` | All | Rate limit exceeded |
| `RATE-002` | All | Daily quota exceeded |
| `NET-001` | All | Network connection failed |
| `NET-002` | All | Request timeout |
| `PARSE-001` | All | Malformed JSON response |
| `PARSE-002` | All | Unexpected response schema |
| `CLI-001` | ClaudeCode | CLI not found |
| `CLI-002` | ClaudeCode | CLI not authenticated |
| `CLI-003` | ClaudeCode | CLI process crashed |
| `CLI-004` | ClaudeCode | CLI output parse error |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-01-30 | Initial checklist for Month 1 Claude Code ship-quality |
