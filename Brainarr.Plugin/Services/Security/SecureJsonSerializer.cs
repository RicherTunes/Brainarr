using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Security.Llm;

namespace Brainarr.Plugin.Services.Security
{
    /// <summary>
    /// Brainarr-local compatibility shim that delegates to common's
    /// <see cref="LlmJsonSerializer"/>. Mechanics (depth caps, size cap,
    /// suspicious-pattern guard, relaxed parse mode for legitimate
    /// &lt;script&gt; content) live in the shared library so brainarr,
    /// streaming plugins and any future LLM consumer share one hardened
    /// implementation. Call-site shape is preserved.
    /// </summary>
    public static class SecureJsonSerializer
    {
        public static T Deserialize<T>(string json, bool strict = false) where T : class
            => LlmJsonSerializer.Deserialize<T>(json, strict);

        public static Task<T> DeserializeAsync<T>(Stream stream, bool strict = false) where T : class
            => LlmJsonSerializer.DeserializeAsync<T>(stream, strict);

        public static string Serialize<T>(T obj, bool strict = false) where T : class
            => LlmJsonSerializer.Serialize<T>(obj, strict);

        public static Task SerializeAsync<T>(Stream stream, T obj, bool strict = false) where T : class
            => LlmJsonSerializer.SerializeAsync<T>(stream, obj, strict);

        public static bool TryDeserialize<T>(string json, out T? result, out string? error) where T : class
            => LlmJsonSerializer.TryDeserialize<T>(json, out result, out error);

        public static JsonDocument ParseDocument(string json)
            => LlmJsonSerializer.ParseDocument(json);

        public static JsonDocument ParseDocumentRelaxed(string json)
            => LlmJsonSerializer.ParseDocumentRelaxed(json);

        public static JsonSerializerOptions CreateOptions(
            int maxDepth = 10,
            bool caseInsensitive = true,
            bool writeIndented = false)
            => LlmJsonSerializer.CreateOptions(maxDepth, caseInsensitive, writeIndented);
    }
}
