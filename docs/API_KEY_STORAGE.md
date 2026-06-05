# API Key Storage Model

> Updated for BRN-001: API keys are **encrypted at rest**. This supersedes the earlier "Phase 1 plaintext /
> Phase 1.1 DataProtection-recommended" model — that work is now implemented (see below).

## Current Storage Model

### Where keys live

API keys for all cloud providers (OpenAI, Anthropic, Perplexity, OpenRouter, DeepSeek, Gemini, Groq, ZaiGlm,
ZaiCoding) live in the `BrainarrSettings` partial class, specifically in `BrainarrSettings.Providers.cs`. The
private backing fields hold **ciphertext**, not plaintext.

### How they are stored (encrypted at rest)

Each provider has:

1. A **private backing field** that holds the **encrypted** value — e.g., `private string? _openAIApiKey;`
   (ciphertext in `lpc:ps:v1:` format).
2. A **public property** whose **setter encrypts** the incoming plaintext and whose **getter decrypts** on read —
   e.g. `OpenAIApiKey { get => DecryptApiKeyField(_openAIApiKey); set => _openAIApiKey = EncryptApiKeyField(value, ...); }`,
   where `EncryptApiKeyField`/`DecryptApiKeyField` wrap the protector below.
3. A **unified `ApiKey` property** that dispatches to the correct backing field based on the `Provider` enum.

Encryption uses Common's `IStringProtector`:

- `SharedProtector` is a `Lazy<IStringProtector>` initialized from `BrainarrApiKeyProtection.GetDefaultStringProtector()`.
- Setters call `SharedProtector.Value.Protect(plaintext)` → `lpc:ps:v1:`-prefixed ciphertext.
- Getters call `BrainarrApiKeyProtection.UnprotectString(storedValue, SharedProtector.Value)`. Values not in the
  protected format (e.g. a legacy plaintext value migrated from an older settings row) are returned as-is, so
  upgrades don't break existing configs.

`SanitizeApiKey` runs **before** encryption — it trims whitespace and strips obvious injection characters. It is
defensive input cleaning, not the encryption step.

### How they are read

`BrainarrSettings` is deserialized from Lidarr's own settings store (SQLite via NzbDrone). The backing field
materializes as the stored **ciphertext**; the property getter decrypts it on access. The
`[FieldDefinition(..., Privacy = PrivacyLevel.Password)]` annotation additionally tells the Lidarr UI to render
the field as a masked password input and redact it in API responses.

### Subscription-based providers (Claude Code, OpenAI Codex)

These providers do not use API keys; they reference **credential file paths** on disk (e.g.,
`~/.claude/.credentials.json`). The paths are stored as plain strings in `ClaudeCodeCredentialsPath` and
`OpenAICodexCredentialsPath` — they are not secrets themselves (the credential files they point to are
protected by the OS file permissions of the user running Lidarr).

## What this protects (and what it doesn't)

- **At rest:** the SQLite settings database holds ciphertext, not plaintext keys, so a read of the database file
  (or a database backup) does not directly yield the keys.
- **In the UI / API:** the `PrivacyLevel.Password` annotation masks + redacts the field.
- **In memory / logs:** keys are decrypted in memory when used, and the backing fields are ciphertext — do **not**
  log the backing fields (they are ciphertext) nor the decrypted property values. `HashApiKey` (the idempotency
  fingerprint) zeroes its transient UTF-8 buffer after hashing.

The `IStringProtector` strength depends on the platform protector resolved at runtime (DPAPI on Windows, etc.).

## Files involved

| File | Role |
|------|------|
| `Brainarr.Plugin/BrainarrSettings.Providers.cs` | Encrypted backing fields, encrypt-on-set / decrypt-on-get properties, `SanitizeApiKey`, `SharedProtector` |
| `Brainarr.Plugin/BrainarrSettings.cs` | `SanitizeApiKey` helper (parent partial) |
| `BrainarrApiKeyProtection` | `GetDefaultStringProtector()` + `UnprotectString(...)` over Common's `IStringProtector` |
