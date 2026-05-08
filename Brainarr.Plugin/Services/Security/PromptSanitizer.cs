using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Security.Llm;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// Brainarr-local plugin contract for prompt sanitization.
    /// Mechanics live in <see cref="LlmPromptSanitizer"/> in Lidarr.Plugin.Common; this
    /// interface keeps the seam local so the plugin can supply its own DI binding,
    /// safe-default fallback, and policy decisions independently of the shared library.
    /// </summary>
    public interface IPromptSanitizer
    {
        string SanitizePrompt(string input);
        Task<string> SanitizePromptAsync(string input, CancellationToken cancellationToken = default);
        bool ContainsInjectionAttempt(string input);
        string RemoveSensitiveData(string input);
    }

    /// <summary>
    /// Brainarr-local default implementation that delegates to common's
    /// <see cref="LlmPromptSanitizer"/>. The shared sanitizer covers 60+
    /// injection / jailbreak / data-exfiltration / SQL+NoSQL / homograph
    /// patterns, control + zero-width Unicode stripping, and sensitive-data
    /// redaction (API keys, embedded creds, emails, password=value pairs).
    ///
    /// We supply a music-recommendation safe-default so that if the input is
    /// unrecoverable the LLM still gets a coherent, on-topic prompt.
    /// </summary>
    public class PromptSanitizer : IPromptSanitizer
    {
        private const string MusicRecommendationsFallback =
            "Please provide music recommendations based on the user's library.";

        private readonly LlmPromptSanitizer _inner;

        public PromptSanitizer()
        {
            _inner = new LlmPromptSanitizer
            {
                SafeDefaultPrompt = MusicRecommendationsFallback
            };
        }

        public string SanitizePrompt(string input) => _inner.SanitizePrompt(input);

        public Task<string> SanitizePromptAsync(string input, CancellationToken cancellationToken = default)
            => _inner.SanitizePromptAsync(input, cancellationToken);

        public bool ContainsInjectionAttempt(string input) => _inner.ContainsInjectionAttempt(input);

        public string RemoveSensitiveData(string input) => _inner.RemoveSensitiveData(input);
    }
}
