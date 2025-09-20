using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
        private static readonly Regex TokenRegex = new Regex(@"\w+|\S", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        public ModelTokenizerRegistry(IDictionary<string, ITokenizer>? overrides = null)
        {
            _defaultTokenizer = new BasicTokenizer();
            _tokenizers = new ConcurrentDictionary<string, ITokenizer>(StringComparer.OrdinalIgnoreCase);

            if (overrides != null)
            {
                foreach (var kvp in overrides)
                {
                    _tokenizers[kvp.Key] = kvp.Value ?? _defaultTokenizer;
                }
            }
        }

        public ITokenizer Get(string? modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey))
            {
                return _defaultTokenizer;
            }

            if (_tokenizers.TryGetValue(modelKey, out var tokenizer))
            {
                return tokenizer;
            }

            var providerKey = modelKey.Split(':')[0];
            if (_tokenizers.TryGetValue(providerKey, out tokenizer))
            {
                return tokenizer;
            }

            return _defaultTokenizer;
        }
    }
}
