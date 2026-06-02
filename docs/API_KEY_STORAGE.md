# API Key Storage Model

> Phase 1 documentation — branch `cleanup/delete-dead-enhanced-rate-limiter`.
> Phase 1.1 will implement DataProtection integration.

## Current Storage Model

### Where keys live

API keys for all cloud providers (OpenAI, Anthropic, Perplexity, OpenRouter, DeepSeek, Gemini, Groq, ZaiGlm, ZaiCoding) are stored as **plain-text strings** inside the `BrainarrSettings` partial class, specifically in `BrainarrSettings.Providers.cs`.

### How they are stored

Each provider has:

1. A **private backing field** — e.g., `private string? _openAIApiKey;`
2. A **public property** that calls `SanitizeApiKey(value)` on write — e.g., `OpenAIApiKey { get => _openAIApiKey; set => _openAIApiKey = SanitizeApiKey(value); }`
3. A **unified `ApiKey` property** (lines 8–55 of `BrainarrSettings.Providers.cs`) that dispatches to the correct backing field based on `Provider` enum.

`SanitizeApiKey` trims whitespace and removes obvious injection characters — it does **not** encrypt the value.

### How they are read

At runtime, `BrainarrSettings` is deserialized from Lidarr's own settings store (SQLite via NzbDrone). The `[FieldDefinition(..., Privacy = PrivacyLevel.Password)]` annotation on the `ApiKey` property tells the Lidarr UI to render the field as a password input and redact it in API responses, but the value is still stored as plaintext in the database.

### What `SanitizeApiKey` does

Sanitization is defensive input cleaning only — no encryption, no key derivation, no platform-native secret storage. The comment in the source (line 89) acknowledges this:

> "SECURITY: API keys are stored as strings and only marked as Password in UI fields. Do not log these values; consider external secret storage if needed."

### Subscription-based providers (Claude Code, OpenAI Codex)

These providers do not use API keys; instead they reference **credential file paths** on disk (e.g., `~/.claude/.credentials.json`). The paths are stored as plain strings in `ClaudeCodeCredentialsPath` and `OpenAICodexCredentialsPath`.

## Why DataProtection is recommended next

Plain-text storage in a database file means:

- Anyone with read access to the Lidarr SQLite database file can extract all API keys without any further attack.
- Backups of the database ship the keys in cleartext.
- Log scrubbing / UI redaction is the only protection layer, and that layer is incomplete (e.g., diagnostic tools may print settings objects).

### Recommended approach (Phase 1.1)

Common already ships `DataProtectionTokenProtector` (and its cross-platform factory `TokenProtectorFactory`) at:

```
ext/Lidarr.Plugin.Common/src/Security/TokenProtection/DataProtectionTokenProtector.cs
ext/Lidarr.Plugin.Common/src/Security/TokenProtection/TokenProtectorFactory.cs
```

`TokenProtectorFactory.CreateFromEnvironment()` returns the right protector for the current OS (DPAPI on Windows, keychain on macOS, SecretService or file-based AES on Linux). Wrapping each `set =>` in `protector.Protect(...)` and each `get =>` in `protector.Unprotect(...)` would provide OS-level secret storage with no change to the Lidarr settings schema.

## Files involved

| File | Role |
|------|------|
| `Brainarr.Plugin/BrainarrSettings.Providers.cs` | All API key backing fields, properties, and `SanitizeApiKey` |
| `Brainarr.Plugin/BrainarrSettings.cs` | `SanitizeApiKey` helper (defined in parent partial) |
| `ext/Lidarr.Plugin.Common/src/Security/TokenProtection/DataProtectionTokenProtector.cs` | Recommended replacement storage mechanism |
| `ext/Lidarr.Plugin.Common/src/Security/TokenProtection/TokenProtectorFactory.cs` | Cross-platform factory for `ITokenProtector` |

## TODO tracker

See `TODO` comment in `BrainarrSettings.Providers.cs` (added in Phase 1) and tracking issue to be filed for Phase 1.1.
