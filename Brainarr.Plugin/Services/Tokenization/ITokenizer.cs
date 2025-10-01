using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        private readonly ConcurrentDictionary<string, byte> _fallbackWarnings;

        public ModelTokenizerRegistry(IDictionary<string, ITokenizer>? overrides = null, Logger? logger = null)
        {
            _defaultTokenizer = new BasicTokenizer();
            _tokenizers = new ConcurrentDictionary<string, ITokenizer>(StringComparer.OrdinalIgnoreCase);
            _fallbackWarnings = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            _logger = logger;

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

            if (_fallbackWarnings.TryAdd(cacheKey, 0))
            {
                logger.Warn("Tokenizer fallback: no tokenizer registered for {Key}; using basic estimator (+/-20% drift). Configure the model registry to supply an accurate tokenizer.", normalized);
            }
        }
    }
}
