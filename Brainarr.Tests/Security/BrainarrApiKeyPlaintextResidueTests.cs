using System.Linq;
using System.Reflection;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace Brainarr.Tests.Security;

/// <summary>
/// F-03: BrainarrSettings must not retain the plaintext API key in any long-lived field. The encrypted
/// backing field holds ciphertext; the idempotency bookkeeping must compare a one-way hash, not the
/// plaintext itself, so a heap dump of a live settings instance cannot recover the key.
/// </summary>
public class BrainarrApiKeyPlaintextResidueTests
{
    public static IEnumerable<object[]> ApiKeyProperties() => new[]
    {
        new object[] { nameof(BrainarrSettings.OpenAIApiKey) },
        new object[] { nameof(BrainarrSettings.PerplexityApiKey) },
        new object[] { nameof(BrainarrSettings.AnthropicApiKey) },
        new object[] { nameof(BrainarrSettings.OpenRouterApiKey) },
        new object[] { nameof(BrainarrSettings.DeepSeekApiKey) },
        new object[] { nameof(BrainarrSettings.GeminiApiKey) },
        new object[] { nameof(BrainarrSettings.GroqApiKey) },
        new object[] { nameof(BrainarrSettings.ZaiGlmApiKey) },
    };

    [Theory]
    [MemberData(nameof(ApiKeyProperties))]
    public void SettingApiKey_DoesNotRetainPlaintextInAnyStringField(string propertyName)
    {
        var settings = new BrainarrSettings();
        const string secret = "sk-supersecret-plaintext-key-abc123XYZ";

        var prop = typeof(BrainarrSettings).GetProperty(propertyName)!;
        prop.SetValue(settings, secret);

        // No private string field may hold the plaintext (the backing field holds ciphertext).
        var stringFieldValues = typeof(BrainarrSettings)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(settings))
            .Where(v => v != null)
            .ToList();

        Assert.DoesNotContain(secret, stringFieldValues);

        // ...and the value still round-trips through the property.
        Assert.Equal(secret, (string?)prop.GetValue(settings));
    }
}
