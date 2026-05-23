# BRN-001 Remediation — Encrypted-at-Rest LLM Provider API Keys

**Status**: Implemented
**Date**: 2026-05-23
**Finding addressed**: BRN-001 (all LLM provider API keys stored as plaintext in Lidarr SQLite database)
**Files changed**:
- `Brainarr.Plugin/BrainarrSettings.Providers.cs` — production fix (property setters encrypt, getters decrypt)
- `Brainarr.Plugin/Services/Security/BrainarrApiKeyProtection.cs` — encryption helper (NEW)
- `Brainarr.Tests/Security/BrainarrApiKeyProtectionTests.cs` — TDD characterization tests (NEW)
- `scripts/Migrate-BrainarrSettings.ps1` — database migration helper (NEW)

---

## Summary of Finding

All eight LLM provider API keys stored in `BrainarrSettings` were persisted as plaintext
strings in Lidarr's SQLite database (`nzbdrone.db`).

| Provider | Field | Risk |
|----------|-------|------|
| Perplexity | `PerplexityApiKey` | Plaintext key extractable from database file |
| OpenAI | `OpenAIApiKey` | Plaintext key extractable from database file |
| Anthropic | `AnthropicApiKey` | Plaintext key extractable from database file |
| Google Gemini | `GeminiApiKey` | Plaintext key extractable from database file |
| OpenRouter | `OpenRouterApiKey` | Plaintext key extractable from database file |
| Groq | `GroqApiKey` | Plaintext key extractable from database file |
| DeepSeek | `DeepSeekApiKey` | Plaintext key extractable from database file |
| Z.AI (ZhipuAI) GLM | `ZaiGlmApiKey` | Plaintext key extractable from database file |

Any user or process with read access to the Lidarr data directory could extract all
API keys without any additional attack. Database backups shipped the keys in cleartext.

**Subscription-based providers** (`ClaudeCodeSubscription`, `OpenAICodexSubscription`,
`ClaudeCodeCli`) are **not affected**: they reference credential file paths rather than
API keys, and those paths do not contain secret material themselves.

---

## Fix Description

### Architecture note

Unlike applemusicarr (which has its own `FileAppleMusicSettingsStore`), Brainarr uses
Lidarr's built-in SQLite ORM to persist `BrainarrSettings`. Lidarr calls property
**setters** when deserialising from the database and property **getters** when serialising
back. This means the encryption layer lives entirely inside those accessor methods — no
separate file store is needed.

### 1. Property-level encryption

Each API key property setter now encrypts the incoming value via `IStringProtector.Protect()`
and stores the ciphertext (`lpc:ps:v1:…`) in the backing field. The getter decrypts on read.

```csharp
// Before (plaintext)
public string? OpenAIApiKey
{
    get => _openAIApiKey;
    set => _openAIApiKey = SanitizeApiKey(value);
}

// After (encrypted-at-rest)
public string? OpenAIApiKey
{
    get => DecryptApiKeyField(_openAIApiKey);          // decrypts lpc:ps:v1:... on read
    set
    {
        var newEncrypted = EncryptApiKeyField(value,
            _lastDecryptedOpenAIApiKey,
            _lastEncryptedOpenAIApiKey);               // idempotent if plaintext unchanged
        _openAIApiKey = newEncrypted;
        _lastEncryptedOpenAIApiKey = newEncrypted;
        _lastDecryptedOpenAIApiKey = value;
    }
}
```

### 2. Back-compat: legacy plaintext detection

`LoadRawApiKey()` (called by tests to simulate Lidarr ORM deserialisation) emits
`LogLevel.Warning` when it encounters a non-empty value that does not start with
`lpc:ps:v1:`. The value is still returned to callers unchanged (back-compat), but
the next assignment through the setter will write it encrypted.

### 3. Idempotent re-save (no spurious re-encrypt)

Each `BrainarrSettings` instance caches the last encrypted blob (`_lastEncrypted{Field}`)
and last decrypted plaintext (`_lastDecrypted{Field}`) for each key. If the caller sets
the same plaintext that was previously loaded, the original encrypted blob is re-used
instead of re-encrypting. This prevents non-deterministic protectors (DPAPI, Keychain)
from producing new ciphertext bytes on every Lidarr restart, which would otherwise
cause spurious database writes.

### 4. Encryption helper

`BrainarrApiKeyProtection` (at `Brainarr.Plugin/Services/Security/`) provides a
lazy-initialised singleton `IStringProtector` backed by
`TokenProtectorFactory.CreateFromEnvironment()`. This mirrors the `AppleMusicSecretProtection`
helper in applemusicarr and the Common extension pattern.

---

## Before / After — On-disk representation (Lidarr SQLite)

### Before (plaintext, BRN-001 vulnerable)

```json
{
  "openAIApiKey": "sk-proj-abc123...",
  "anthropicApiKey": "sk-ant-api03-abc123...",
  "groqApiKey": "gsk_abc123..."
}
```

### After (encrypted at rest)

```json
{
  "openAIApiKey": "lpc:ps:v1:AAAA:BBBBBBBBBBBBBBBBBBBBBBBBxxx...",
  "anthropicApiKey": "lpc:ps:v1:AAAA:CCCCCCCCCCCCCCCCCCCCCCCCyyy...",
  "groqApiKey": "lpc:ps:v1:AAAA:DDDDDDDDDDDDDDDDDDDDDDDDzzz..."
}
```

The `lpc:ps:v1:` prefix is the `StringTokenProtector` wire format from
`Lidarr.Plugin.Common`. The payload is a Base64-URL-encoded ciphertext produced by the
active `ITokenProtector` back-end (see environment variable table below).

---

## Migration for Existing Installations

### Automatic (recommended)

```powershell
pwsh -File scripts/Migrate-BrainarrSettings.ps1
```

The script:
1. Locates the Lidarr database at the platform-default path (or `-LidarrDbPath` override).
2. Creates a `.brn001.bak` backup of the original database.
3. Opens the database directly, reads `ImportLists` rows for Brainarr.
4. For each plaintext API key, calls `IStringProtector.Protect()` and updates the row.
5. Reports the count of encrypted, already-encrypted, and empty fields.

Safe to run multiple times. If all keys are already encrypted, exits 0 with no changes.

### Manual / graceful

If you prefer not to run the script, simply restart Lidarr after updating the plugin.
The next time any settings form is saved in the UI, the setters will encrypt all keys
automatically. (The deprecation warning in the logs indicates which keys are still
plaintext pending re-save.)

### Environment variables for encryption back-end

| Variable | Purpose |
|----------|---------|
| `LP_COMMON_PROTECTOR` | Selects the protector back-end (see table below) |
| `LP_COMMON_APP_NAME` | Application name used as a DataProtection discriminator (default: `Lidarr.Plugin.Common`) |
| `LP_COMMON_KEYS_PATH` | Path to the DataProtection key ring directory (Linux/cross-platform) |
| `LP_COMMON_CERT_PATH` | Path to a PFX certificate for encrypting the DataProtection key ring |
| `LP_COMMON_CERT_PASSWORD` | Password for the PFX certificate |
| `LP_COMMON_CERT_THUMBPRINT` | Thumbprint of a certificate in the local cert store |
| `LP_COMMON_AKV_KEY_ID` | Azure Key Vault key URI for protecting the DataProtection key ring |

| `LP_COMMON_PROTECTOR` value | Back-end | Platforms |
|-----------------------------|----------|-----------|
| `auto` (default) | DPAPI user-scope (Windows), Keychain (macOS), DataProtection+AES (Linux) | All |
| `dpapi` / `dpapi-user` | Windows DPAPI user scope | Windows |
| `dpapi-machine` | Windows DPAPI machine scope | Windows |
| `keychain` | macOS Keychain | macOS |
| `secret-service` | Linux Secret Service | Linux |
| `dataprotection` | ASP.NET DataProtection + AES (file keyring) | All |

---

## Key-Rotation Playbook

### Rotate a compromised provider API key

1. **Revoke the key** in the provider's dashboard:
   - OpenAI: platform.openai.com → API Keys → Revoke
   - Anthropic: console.anthropic.com → API Keys → Deactivate
   - Perplexity: perplexity.ai → Settings → API → Revoke
   - OpenRouter: openrouter.ai → Settings → Keys → Delete
   - Google Gemini: aistudio.google.com → API Keys → Delete
   - Groq: console.groq.com → Settings → API Keys → Delete
   - DeepSeek: platform.deepseek.com → API Keys → Delete
   - Z.AI: bigmodel.cn → API Keys → Revoke
2. **Create a new key** in the provider's dashboard.
3. **Update Brainarr settings** in the Lidarr UI: navigate to Settings → Import Lists → Brainarr,
   enter the new key in the API Key field, and save. The setter will encrypt it automatically.
4. Optionally re-run `Migrate-BrainarrSettings.ps1` to confirm the key is encrypted on disk.

### Rotate the encryption key material (DPAPI / DataProtection key ring)

If the OS user account or DataProtection key ring is compromised:

1. Stop Lidarr.
2. Generate new key material (e.g., re-provision the service user on Windows, or rotate the
   `LP_COMMON_KEYS_PATH` key ring files on Linux).
3. Run `Migrate-BrainarrSettings.ps1` **from the new key material context** with the old plaintext
   values (restore from the `.bak` database backup, extract the plaintext keys, then re-encrypt
   with the new protector).
4. Restart Lidarr.

---

## Deferred: Subscription-Based Provider Credentials

`ClaudeCodeCredentialsPath` and `OpenAICodexCredentialsPath` store **file paths**, not
API keys. The credential files they reference (`~/.claude/.credentials.json`,
`~/.codex/auth.json`) are managed by their respective CLIs and are outside Brainarr's
control. No encryption is applied to these path strings — they do not contain secret
material. If the credential files themselves need protection, use OS-level file
permissions (chmod 600 / Windows ACLs).

---

## Test Coverage

18 characterization tests in `Brainarr.Tests/Security/BrainarrApiKeyProtectionTests.cs`:

| Test(s) | Count | What it asserts |
|---------|-------|----------------|
| `SettingApiKey_StoresEncryptedValue_NotPlaintext` | 8 (one per provider) | Backing field starts with `lpc:ps:v1:`, does not contain plaintext |
| `RoundTrip_GetterReturnsOriginalPlaintext` | 8 (one per provider) | Getter decrypts and returns byte-equal original plaintext |
| `LoadingLegacyPlaintextApiKey_EmitsDeprecationWarning_AndReturnsValue` | 1 | Legacy plaintext loaded successfully; `LogLevel.Warning` emitted |
| `SetApiKey_Twice_WithSameValue_DoesNotChangeCiphertext` | 1 | Idempotent: same plaintext → same ciphertext blob |
