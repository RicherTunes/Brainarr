using System;
using System.Security.Cryptography;
using System.Text;

using Lidarr.Plugin.Common.Interfaces;

using Microsoft.Extensions.Logging;

using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public partial class BrainarrSettings
    {
        // ===== Encryption infrastructure (BRN-001) =====
        // Lazily-initialised protector shared across all instances.  Uses
        // TokenProtectorFactory.CreateFromEnvironment() which picks the right
        // platform back-end (DPAPI / Keychain / DataProtection+AES).
        private static readonly Lazy<IStringProtector> SharedProtector =
            new(() => BrainarrApiKeyProtection.GetDefaultStringProtector());

        // Per-instance idempotency caches — one pair per API key field.
        // If the caller sets the same plaintext key that was last loaded, we re-use the original encrypted
        // blob so non-deterministic protectors (DPAPI, Keychain) do not produce spurious ciphertext changes
        // on each save. F-03: we keep a one-way HASH of the last plaintext (not the plaintext itself), so a
        // heap dump of a live settings instance cannot recover the API key from these bookkeeping fields.
        private string? _lastEncryptedPerplexityApiKey;
        private string? _lastDecryptedPerplexityApiKeyHash;
        private string? _lastEncryptedOpenAIApiKey;
        private string? _lastDecryptedOpenAIApiKeyHash;
        private string? _lastEncryptedAnthropicApiKey;
        private string? _lastDecryptedAnthropicApiKeyHash;
        private string? _lastEncryptedOpenRouterApiKey;
        private string? _lastDecryptedOpenRouterApiKeyHash;
        private string? _lastEncryptedDeepSeekApiKey;
        private string? _lastDecryptedDeepSeekApiKeyHash;
        private string? _lastEncryptedGeminiApiKey;
        private string? _lastDecryptedGeminiApiKeyHash;
        private string? _lastEncryptedGroqApiKey;
        private string? _lastDecryptedGroqApiKeyHash;
        private string? _lastEncryptedZaiGlmApiKey;
        private string? _lastDecryptedZaiGlmApiKeyHash;

        // ===== Private helpers =====

        /// <summary>
        /// Encrypts <paramref name="plaintext"/>. If its hash equals the
        /// <paramref name="lastDecryptedHash"/> from the last load/set, re-uses
        /// <paramref name="lastEncryptedBlob"/> for idempotency. The comparison is on a hash so the plaintext
        /// key is never retained in a long-lived field (F-03).
        /// </summary>
        private static string? EncryptApiKeyField(
            string? plaintext,
            string? lastDecryptedHash,
            string? lastEncryptedBlob)
        {
            if (lastEncryptedBlob is not null
                && string.Equals(HashApiKey(plaintext), lastDecryptedHash, StringComparison.Ordinal))
            {
                return lastEncryptedBlob;
            }

            // Inline sanitisation (mirrors SanitizeApiKey instance method, static-safe)
            if (!string.IsNullOrWhiteSpace(plaintext))
            {
                plaintext = plaintext.Trim();
                if (plaintext.Length > 500)
                    throw new ArgumentException("API key exceeds maximum allowed length");
            }

            return SharedProtector.Value.Protect(plaintext);
        }

        /// <summary>
        /// F-03: a one-way fingerprint of an API key plaintext, used only to detect "same key as last time"
        /// for the idempotency cache without retaining the plaintext. Returns null for null input. API keys
        /// are high-entropy, so a SHA-256 of the value is not practically reversible from a heap dump.
        /// </summary>
        private static string? HashApiKey(string? plaintext)
        {
            if (plaintext is null)
            {
                return null;
            }

            // R2-10: the UTF-8 encoding holds the API-key plaintext — zero it after hashing so it doesn't
            // linger on the managed heap (consistent with the rest of the F-03 secret-handling path).
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            try
            {
                return Convert.ToHexString(SHA256.HashData(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        /// <summary>
        /// Decrypts <paramref name="storedValue"/>. If it is already plaintext
        /// (i.e. not in lpc:ps:v1: format), returns it as-is (back-compat).
        /// </summary>
        private static string? DecryptApiKeyField(string? storedValue)
            => BrainarrApiKeyProtection.UnprotectString(storedValue, SharedProtector.Value);

        // ===== Test-support helpers =====
        // Expose internals required by BrainarrApiKeyProtectionTests (BRN-001 characterization suite).

        /// <summary>
        /// Returns the raw encrypted backing-field value for the named API key property.
        /// Used by tests to assert the on-disk value is encrypted (starts with lpc:ps:v1:).
        /// </summary>
        internal string? GetRawEncryptedApiKey(string propertyName) => propertyName switch
        {
            nameof(PerplexityApiKey) => _perplexityApiKey,
            nameof(OpenAIApiKey) => _openAIApiKey,
            nameof(AnthropicApiKey) => _anthropicApiKey,
            nameof(OpenRouterApiKey) => _openRouterApiKey,
            nameof(DeepSeekApiKey) => _deepSeekApiKey,
            nameof(GeminiApiKey) => _geminiApiKey,
            nameof(GroqApiKey) => _groqApiKey,
            nameof(ZaiGlmApiKey) => _zaiGlmApiKey,
            _ => throw new ArgumentException($"Unknown API key property: {propertyName}", nameof(propertyName))
        };

        /// <summary>
        /// Simulates the Lidarr ORM deserialisation path: writes a raw (potentially
        /// legacy plaintext) value directly into the named API key backing field, emitting
        /// a deprecation warning if the value is not already encrypted.
        /// </summary>
        internal void LoadRawApiKey(string propertyName, string? rawValue, ILogger? logger = null)
        {
            var protector = SharedProtector.Value;

            if (!string.IsNullOrWhiteSpace(rawValue) && !protector.IsProtected(rawValue))
            {
                logger?.LogWarning(
                    "BRN-001: {PropertyName} is plaintext (unencrypted legacy format). " +
                    "It will be encrypted on the next save. Run Migrate-BrainarrSettings.ps1 to encrypt immediately.",
                    propertyName);
            }

            switch (propertyName)
            {
                case nameof(PerplexityApiKey):
                    _lastEncryptedPerplexityApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _perplexityApiKey = rawValue;
                    _lastDecryptedPerplexityApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(OpenAIApiKey):
                    _lastEncryptedOpenAIApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _openAIApiKey = rawValue;
                    _lastDecryptedOpenAIApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(AnthropicApiKey):
                    _lastEncryptedAnthropicApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _anthropicApiKey = rawValue;
                    _lastDecryptedAnthropicApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(OpenRouterApiKey):
                    _lastEncryptedOpenRouterApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _openRouterApiKey = rawValue;
                    _lastDecryptedOpenRouterApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(DeepSeekApiKey):
                    _lastEncryptedDeepSeekApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _deepSeekApiKey = rawValue;
                    _lastDecryptedDeepSeekApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(GeminiApiKey):
                    _lastEncryptedGeminiApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _geminiApiKey = rawValue;
                    _lastDecryptedGeminiApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(GroqApiKey):
                    _lastEncryptedGroqApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _groqApiKey = rawValue;
                    _lastDecryptedGroqApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                case nameof(ZaiGlmApiKey):
                    _lastEncryptedZaiGlmApiKey = protector.IsProtected(rawValue) ? rawValue : null;
                    _zaiGlmApiKey = rawValue;
                    _lastDecryptedZaiGlmApiKeyHash = HashApiKey(DecryptApiKeyField(rawValue));
                    break;
                default:
                    throw new ArgumentException($"Unknown API key property: {propertyName}", nameof(propertyName));
            }
        }

        [FieldDefinition(3, Label = "API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Enter your API key for the selected provider. Not needed for local providers (Ollama/LM Studio)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#api-keys")]
        public string ApiKey
        {
            get => Provider switch
            {
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                AIProvider.ZaiGlm => ZaiGlmApiKey,
                // ZaiCoding reuses the ZaiGlmApiKey field — same Z.AI account credential,
                // only the endpoint and wire format differ between the two providers.
                AIProvider.ZaiCoding => ZaiGlmApiKey,
                _ => null
            };
            set
            {
                switch (Provider)
                {
                    case AIProvider.Perplexity:
                        PerplexityApiKey = value;
                        break;
                    case AIProvider.OpenAI:
                        OpenAIApiKey = value;
                        break;
                    case AIProvider.Anthropic:
                        AnthropicApiKey = value;
                        break;
                    case AIProvider.OpenRouter:
                        OpenRouterApiKey = value;
                        break;
                    case AIProvider.DeepSeek:
                        DeepSeekApiKey = value;
                        break;
                    case AIProvider.Gemini:
                        GeminiApiKey = value;
                        break;
                    case AIProvider.Groq:
                        GroqApiKey = value;
                        break;
                    case AIProvider.ZaiCoding:
                        ZaiGlmApiKey = value;
                        break;
                    case AIProvider.ZaiGlm:
                        ZaiGlmApiKey = value;
                        break;
                }
            }
        }

        // Hidden backing fields for all providers
        public string OllamaUrl
        {
            get => string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl;
            set => _ollamaUrl = NormalizeHttpUrlOrOriginal(value);
        }

        // Internal property for validation - returns actual value without defaults
        internal string OllamaUrlRaw => _ollamaUrl;

        public string OllamaModel
        {
            get => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel;
            set => _ollamaModel = value;
        }

        public string LMStudioUrl
        {
            get => string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl;
            set => _lmStudioUrl = NormalizeHttpUrlOrOriginal(value);
        }

        // Internal property for validation - returns actual value without defaults
        internal string LMStudioUrlRaw => _lmStudioUrl;

        public string LMStudioModel
        {
            get => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel;
            set => _lmStudioModel = value;
        }

        // Hidden backing properties for all API-based providers.
        // SECURITY (BRN-001): Values are stored encrypted-at-rest via Common's IStringProtector.
        // The backing fields hold ciphertext in lpc:ps:v1: format; the public property
        // getters decrypt on read and setters encrypt on write.
        // Do NOT log these backing fields directly — they are ciphertext.
        private string? _perplexityApiKey;
        private string? _openAIApiKey;
        private string? _anthropicApiKey;
        private string? _openRouterApiKey;
        private string? _deepSeekApiKey;
        private string? _geminiApiKey;
        private string? _groqApiKey;
        private string? _zaiGlmApiKey;

        public string PerplexityApiKey
        {
            get => DecryptApiKeyField(_perplexityApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedPerplexityApiKeyHash, _lastEncryptedPerplexityApiKey);
                _perplexityApiKey = newEncrypted;
                _lastEncryptedPerplexityApiKey = newEncrypted;
                _lastDecryptedPerplexityApiKeyHash = HashApiKey(value);
            }
        }
        // New canonical model id properties per provider
        public string? PerplexityModelId { get; set; }
        // Backward-compat aliases for tests and legacy code
        public string? PerplexityModel { get => PerplexityModelId; set => PerplexityModelId = ProviderModelNormalizer.Normalize(AIProvider.Perplexity, value); }
        public string? OpenAIApiKey
        {
            get => DecryptApiKeyField(_openAIApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedOpenAIApiKeyHash, _lastEncryptedOpenAIApiKey);
                _openAIApiKey = newEncrypted;
                _lastEncryptedOpenAIApiKey = newEncrypted;
                _lastDecryptedOpenAIApiKeyHash = HashApiKey(value);
            }
        }
        public string? OpenAIModelId { get; set; }
        public string? OpenAIModel { get => OpenAIModelId; set => OpenAIModelId = value; }
        public string? AnthropicApiKey
        {
            get => DecryptApiKeyField(_anthropicApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedAnthropicApiKeyHash, _lastEncryptedAnthropicApiKey);
                _anthropicApiKey = newEncrypted;
                _lastEncryptedAnthropicApiKey = newEncrypted;
                _lastDecryptedAnthropicApiKeyHash = HashApiKey(value);
            }
        }
        public string? AnthropicModelId { get; set; }
        public string? AnthropicModel { get => AnthropicModelId; set => AnthropicModelId = value; }
        public string? OpenRouterApiKey
        {
            get => DecryptApiKeyField(_openRouterApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedOpenRouterApiKeyHash, _lastEncryptedOpenRouterApiKey);
                _openRouterApiKey = newEncrypted;
                _lastEncryptedOpenRouterApiKey = newEncrypted;
                _lastDecryptedOpenRouterApiKeyHash = HashApiKey(value);
            }
        }
        public string? OpenRouterModelId { get; set; }
        public string? OpenRouterModel { get => OpenRouterModelId; set => OpenRouterModelId = value; }
        public string? DeepSeekApiKey
        {
            get => DecryptApiKeyField(_deepSeekApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedDeepSeekApiKeyHash, _lastEncryptedDeepSeekApiKey);
                _deepSeekApiKey = newEncrypted;
                _lastEncryptedDeepSeekApiKey = newEncrypted;
                _lastDecryptedDeepSeekApiKeyHash = HashApiKey(value);
            }
        }
        public string? DeepSeekModelId { get; set; }
        public string? DeepSeekModel { get => DeepSeekModelId; set => DeepSeekModelId = value; }
        public string? GeminiApiKey
        {
            get => DecryptApiKeyField(_geminiApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedGeminiApiKeyHash, _lastEncryptedGeminiApiKey);
                _geminiApiKey = newEncrypted;
                _lastEncryptedGeminiApiKey = newEncrypted;
                _lastDecryptedGeminiApiKeyHash = HashApiKey(value);
            }
        }
        public string? GeminiModelId { get; set; }
        public string? GeminiModel { get => GeminiModelId; set => GeminiModelId = value; }
        public string? GroqApiKey
        {
            get => DecryptApiKeyField(_groqApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedGroqApiKeyHash, _lastEncryptedGroqApiKey);
                _groqApiKey = newEncrypted;
                _lastEncryptedGroqApiKey = newEncrypted;
                _lastDecryptedGroqApiKeyHash = HashApiKey(value);
            }
        }
        public string? GroqModelId { get; set; }
        public string? GroqModel { get => GroqModelId; set => GroqModelId = value; }

        // Z.AI (Zhipu) GLM provider — OpenAI-compatible chat completions.
        public string? ZaiGlmApiKey
        {
            get => DecryptApiKeyField(_zaiGlmApiKey);
            set
            {
                var newEncrypted = EncryptApiKeyField(value, _lastDecryptedZaiGlmApiKeyHash, _lastEncryptedZaiGlmApiKey);
                _zaiGlmApiKey = newEncrypted;
                _lastEncryptedZaiGlmApiKey = newEncrypted;
                _lastDecryptedZaiGlmApiKeyHash = HashApiKey(value);
            }
        }
        public string? ZaiGlmModelId { get; set; }
        public string? ZaiGlmModel { get => ZaiGlmModelId; set => ZaiGlmModelId = value; }

        // Z.AI Coding Plan — model id is stored separately from ZaiGlmModelId because
        // the two providers serve overlapping-but-distinct model catalogs (the Coding
        // endpoint admits Coding-Plan-gated models the PaaS endpoint does not, and
        // vice-versa). API key is reused across both — see the unified ApiKey setter above.
        public string? ZaiCodingModelId { get; set; }
        public string? ZaiCodingModel { get => ZaiCodingModelId; set => ZaiCodingModelId = value; }

        // ===== Subscription-based Providers (Claude Code / OpenAI Codex) =====
        // These use credential files instead of API keys — NOT affected by BRN-001.
        // Credential file paths do not contain secret material; the credential files
        // themselves are managed by their respective CLIs (claude, codex).

        private string? _claudeCodeCredentialsPath;
        private string? _openAICodexCredentialsPath;

        /// <summary>
        /// Path to Claude Code credentials file (~/.claude/.credentials.json by default).
        /// Run 'claude login' to generate credentials.
        /// </summary>
        [FieldDefinition(38, Label = "Claude Code Credentials Path", Type = FieldType.Path,
            HelpText = "Path to your Claude Code credentials file. Default: ~/.claude/.credentials.json\nRun 'claude login' in terminal to authenticate. The Test button will validate your credentials.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#claude-code-subscription")]
        public string ClaudeCodeCredentialsPath
        {
            get => string.IsNullOrEmpty(_claudeCodeCredentialsPath)
                ? Services.SubscriptionCredentialLoader.GetDefaultClaudeCodePath()
                : _claudeCodeCredentialsPath;
            set => _claudeCodeCredentialsPath = value;
        }

        public string? ClaudeCodeModelId { get; set; }

        /// <summary>
        /// Path to OpenAI Codex auth file (~/.codex/auth.json by default).
        /// Run 'codex auth login' to generate credentials.
        /// </summary>
        [FieldDefinition(39, Label = "OpenAI Codex Credentials Path", Type = FieldType.Path,
            HelpText = "Path to your OpenAI Codex auth file. Default: ~/.codex/auth.json\nRun 'codex auth login' in terminal to authenticate. The Test button will validate your credentials.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#openai-codex-subscription")]
        public string OpenAICodexCredentialsPath
        {
            get => string.IsNullOrEmpty(_openAICodexCredentialsPath)
                ? Services.SubscriptionCredentialLoader.GetDefaultCodexPath()
                : _openAICodexCredentialsPath;
            set => _openAICodexCredentialsPath = value;
        }

        public string? OpenAICodexModelId { get; set; }

        // No backward-compat properties; canonical fields are *ModelId
    }
}
