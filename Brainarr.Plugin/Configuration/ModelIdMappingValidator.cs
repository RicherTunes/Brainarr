using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Validates enum → raw model id mappings to catch duplicates and obvious mistakes early.
    /// Non-throwing by default to avoid breaking runtime if new enums are added without mapper updates.
    /// </summary>
    public static class ModelIdMappingValidator
    {
        public static void AssertValid(bool throwOnError = false, Logger logger = null)
        {
            logger ??= LogManager.GetCurrentClassLogger();
            var issues = new List<string>();

            void Check<TEnum>(string provider) where TEnum : Enum
            {
                var values = Enum.GetValues(typeof(TEnum)).Cast<TEnum>();
                var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in values)
                {
                    var raw = ModelIdMapper.ToRawId(provider, v.ToString());
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        issues.Add($"[{provider}] {v} maps to empty");
                        continue;
                    }
                    if (mapped.TryGetValue(raw, out var existing))
                    {
                        issues.Add($"[{provider}] duplicate raw id '{raw}' for {existing} and {v}");
                    }
                    else
                    {
                        mapped[raw] = v.ToString();
                    }
                }
            }

            try
            {
                Check<OpenAIModelKind>("openai");
                Check<AnthropicModelKind>("anthropic");
                Check<OpenRouterModelKind>("openrouter");
                Check<DeepSeekModelKind>("deepseek");
                Check<GeminiModelKind>("gemini");
                Check<GroqModelKind>("groq");
                Check<PerplexityModelKind>("perplexity");
            }
            catch (Exception ex)
            {
                issues.Add($"Validator exception: {ex.Message}");
            }

            if (issues.Count == 0)
            {
                logger.Debug("ModelIdMappingValidator: all mappings look sane");
                return;
            }

            var msg = $"ModelIdMappingValidator found {issues.Count} potential issue(s):\n - " + string.Join("\n - ", issues);
            if (throwOnError)
            {
                throw new InvalidOperationException(msg);
            }
            else
            {
                logger.Warn(msg);
            }
        }
    }
}
