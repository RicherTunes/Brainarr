using System;
using System.Collections.Generic;

using Lidarr.Plugin.Common.Extensions;
using Lidarr.Plugin.Common.Interfaces;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using NzbDrone.Core.ImportLists.Brainarr;

using Xunit;

namespace Brainarr.Tests.Security;

/// <summary>
/// TDD characterization tests for BRN-001:
/// API keys for all LLM providers must never appear plaintext in Lidarr's settings store.
///
/// The BrainarrSettings class is a plain DTO serialised/deserialised by Lidarr's SQLite ORM.
/// Because the ORM writes field values via property setters and reads them via getters, the
/// encryption layer lives entirely inside those accessors — no separate file store is needed.
///
/// Design contract tested here:
/// 1. After assigning a plaintext key, <see cref="BrainarrSettings.GetRawEncryptedApiKey"/> must
///    return a value that starts with "lpc:ps:v1:" and does NOT contain the original plaintext.
/// 2. Calling the public property getter after assignment returns the original plaintext (round-trip).
/// 3. Injecting a raw (legacy) plaintext value via <see cref="BrainarrSettings.LoadRawApiKey"/> emits
///    a deprecation warning and still returns the value on the next getter call.
/// 4. Setting the same plaintext key twice must NOT produce a new ciphertext (idempotency).
/// </summary>
public sealed class BrainarrApiKeyProtectionTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IStringProtector CreateTestProtector()
    {
        var services = new ServiceCollection();
        services.AddTokenProtection();
        return services.BuildServiceProvider().GetRequiredService<IStringProtector>();
    }

    // -----------------------------------------------------------------------
    // Test 1 (parameterised): Each provider API key is encrypted before persistence.
    //
    // We write a plaintext key via the public property setter and then inspect the
    // internal raw value via GetRawEncryptedApiKey (a test-only helper on BrainarrSettings).
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("PerplexityApiKey",   "pplx-testkey-1234567890abcdef")]
    [InlineData("OpenAIApiKey",       "sk-openai-testkey-1234567890abcdef")]
    [InlineData("AnthropicApiKey",    "sk-ant-testkey-1234567890abcdef")]
    [InlineData("GeminiApiKey",       "AIzaSy-gemini-testkey-1234567890")]
    [InlineData("OpenRouterApiKey",   "sk-or-testkey-1234567890abcdef")]
    [InlineData("GroqApiKey",         "gsk-groq-testkey-1234567890abcdef")]
    [InlineData("DeepSeekApiKey",     "sk-deepseek-testkey-1234567890")]
    [InlineData("ZaiGlmApiKey",       "zai-glm-testkey-1234567890abcdef")]
    public void SettingApiKey_StoresEncryptedValue_NotPlaintext(string propertyName, string plaintextKey)
    {
        // Arrange
        var settings = new BrainarrSettings();
        var prop = typeof(BrainarrSettings).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on BrainarrSettings.");

        // Act — write plaintext via the public property setter
        prop.SetValue(settings, plaintextKey);

        // Read back the internal encrypted backing field via GetRawEncryptedApiKey
        var rawStored = settings.GetRawEncryptedApiKey(propertyName);

        // Assert
        // 1. The raw stored value must be encrypted (prefixed with the protector marker).
        Assert.NotNull(rawStored);
        Assert.StartsWith("lpc:ps:v1:", rawStored, StringComparison.Ordinal);

        // 2. The plaintext must NOT appear verbatim in the stored blob.
        Assert.DoesNotContain(plaintextKey, rawStored, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Test 2 (parameterised): Round-trip recovers byte-equal plaintext.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("PerplexityApiKey",   "pplx-roundtrip-abcdef1234567890")]
    [InlineData("OpenAIApiKey",       "sk-openai-roundtrip-abcdef1234567890")]
    [InlineData("AnthropicApiKey",    "sk-ant-roundtrip-abcdef1234567890")]
    [InlineData("GeminiApiKey",       "AIzaSy-gemini-roundtrip-abcdef1234")]
    [InlineData("OpenRouterApiKey",   "sk-or-roundtrip-abcdef1234567890")]
    [InlineData("GroqApiKey",         "gsk-groq-roundtrip-abcdef1234567890")]
    [InlineData("DeepSeekApiKey",     "sk-deepseek-roundtrip-abcdef1234")]
    [InlineData("ZaiGlmApiKey",       "zai-glm-roundtrip-abcdef1234567890")]
    public void RoundTrip_GetterReturnsOriginalPlaintext(string propertyName, string plaintextKey)
    {
        // Arrange
        var settings = new BrainarrSettings();
        var prop = typeof(BrainarrSettings).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on BrainarrSettings.");

        // Act — set plaintext; getter must decrypt and return original value.
        prop.SetValue(settings, plaintextKey);
        var retrieved = (string?)prop.GetValue(settings);

        // Assert
        Assert.Equal(plaintextKey, retrieved);
    }

    // -----------------------------------------------------------------------
    // Test 3: Legacy plaintext fallback — loading an unencrypted value emits a
    // deprecation warning and still returns the value to the caller.
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadingLegacyPlaintextApiKey_EmitsDeprecationWarning_AndReturnsValue()
    {
        // BrainarrSettings.LoadRawApiKey simulates the Lidarr ORM deserialisation path:
        // it writes the raw on-disk value into the backing field, which may be plaintext
        // (legacy format, pre-BRN-001) or already encrypted.
        const string legacyKey = "sk-legacy-plaintext-key-1234567890";

        // Arrange — capture warnings via a custom ILogger mock
        var warnings = new List<string>();
        var loggerMock = new Mock<ILogger>();
        loggerMock
            .Setup(l => l.Log(
                It.Is<LogLevel>(ll => ll == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((_, _, state, _, formatter) =>
            {
                warnings.Add(formatter.DynamicInvoke(state, null) as string ?? state?.ToString() ?? "");
            });
        loggerMock.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);

        var settings = new BrainarrSettings();

        // Act — inject the raw legacy value into the backing store as if Lidarr ORM wrote it
        settings.LoadRawApiKey("OpenAIApiKey", legacyKey, loggerMock.Object);

        // Retrieve via public property (should fall back to plaintext)
        var prop = typeof(BrainarrSettings).GetProperty("OpenAIApiKey")!;
        var retrieved = (string?)prop.GetValue(settings);

        // Assert
        // 1. Back-compat: value returned correctly even though it was plaintext
        Assert.Equal(legacyKey, retrieved);

        // 2. At least one warning must mention plaintext/legacy/unencrypted
        Assert.Contains(warnings, w =>
            w.Contains("plaintext", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("legacy", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("unencrypted", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Test 4: Idempotency — setting the same key twice does not produce a new
    // encrypted blob (important for non-deterministic protectors like DPAPI).
    // -----------------------------------------------------------------------

    [Fact]
    public void SetApiKey_Twice_WithSameValue_DoesNotChangeCiphertext()
    {
        // Arrange
        var settings = new BrainarrSettings();
        const string plaintextKey = "sk-idem-testkey-abcdef1234567890";

        // First set — produces initial ciphertext
        settings.OpenAIApiKey = plaintextKey;
        var firstCiphertext = settings.GetRawEncryptedApiKey("OpenAIApiKey");

        // Act — set again with the same plaintext value
        settings.OpenAIApiKey = plaintextKey;
        var secondCiphertext = settings.GetRawEncryptedApiKey("OpenAIApiKey");

        // Assert — the ciphertext blob must be identical (no spurious re-encrypt)
        Assert.Equal(firstCiphertext, secondCiphertext);
    }
}
