using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Lidarr.Plugin.Common.Diagnostics;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization
{
    public interface ITokenizer
    {
        int CountTokens(string text);
    }

    public interface ITokenizerRegistry
    {
        ITokenizer Get(string? modelKey);
    }

    internal sealed class BasicTokenizer : ITokenizer
    {
        private static readonly Regex TokenRegex = new Regex("\\w+|\\S", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return TokenRegex.Matches(text).Count;
        }
    }

    public sealed class ModelTokenizerRegistry : ITokenizerRegistry
    {
        private readonly ConcurrentDictionary<string, ITokenizer> _tokenizers;
        private readonly ITokenizer _defaultTokenizer;
        private readonly Logger? _logger;
        // STATIC (process/ALC-wide) on purpose: Lidarr re-instantiates BrainarrImportList — and thus a
        // fresh DI ServiceProvider + a fresh ModelTokenizerRegistry — per operation, so an instance-scoped
        // gate re-fired the "no tokenizer registered" WARN every run. WarnOnce is explicitly documented for
        // this `private static readonly` usage; a shared gate dedupes the WARN across instances/runs.
        private static readonly WarnOnce _fallbackWarn = new WarnOnce(StringComparer.OrdinalIgnoreCase);
        private readonly IMetrics _metrics;

        public ModelTokenizerRegistry(IDictionary<string, ITokenizer>? overrides = null, Logger? logger = null, IMetrics? metrics = null)
        {
            _defaultTokenizer = new BasicTokenizer();
            _tokenizers = new ConcurrentDictionary<string, ITokenizer>(StringComparer.OrdinalIgnoreCase);
            _logger = logger;
            _metrics = metrics ?? new NoOpMetrics();

            if (overrides != null)
            {
                foreach (var kvp in overrides)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        continue;
                    }

                    _tokenizers[kvp.Key] = kvp.Value ?? _defaultTokenizer;
                }
            }
        }

        public ITokenizer Get(string? modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey))
            {
                LogFallback("<default>", "empty-model-key");
                return _defaultTokenizer;
            }

            if (_tokenizers.TryGetValue(modelKey, out var tokenizer))
            {
                return tokenizer;
            }

            var providerKey = modelKey.Split(':')[0];
            if (_tokenizers.TryGetValue(providerKey, out tokenizer))
            {
                LogFallback(modelKey, "provider-prefix");
                return tokenizer;
            }

            LogFallback(modelKey, "default-fallback");
            return _defaultTokenizer;
        }

        private void LogFallback(string key, string reason)
        {
            var logger = _logger;
            if (logger == null)
            {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(key) ? "<default>" : key;
            var cacheKey = $"{reason}:{normalized}";

            _fallbackWarn.TryWarn(
                cacheKey,
                () =>
                {
                    logger.Warn("Tokenizer fallback: no tokenizer registered for {Key}; using basic estimator (+/-20% drift). Configure the model registry to supply an accurate tokenizer.", normalized);
                    _metrics.Record(MetricsNames.TokenizerFallback, 1, new Dictionary<string, string>
                    {
                        ["model"] = normalized,
                        ["reason"] = reason
                    });
                },
                () => logger.Debug("Tokenizer fallback (repeated): using basic estimator for {Key}.", normalized));
        }

        // Test-only: the fallback warn gate is process-wide static, so tests that assert the WARN
        // fires must reset it for isolation (mirrors LoggerExtensions.ClearWarnOnceKeysForTests).
        // Never call in production — it would cause suppressed warnings to re-fire.
        internal static void ResetFallbackWarnStateForTests() => _fallbackWarn.Reset();
    }
}
