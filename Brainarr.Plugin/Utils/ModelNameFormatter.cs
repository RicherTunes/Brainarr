using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Utils
{
    /// <summary>
    /// Provides consistent formatting for provider model identifiers so they appear human readable in the UI.
    /// </summary>
    public static class ModelNameFormatter
    {
        private static readonly string[] KnownVariants =
        {
            "Gemini 25", "Gemini 2.5",
            "Gemini 20", "Gemini 2.0",
            "Gemini 15", "Gemini 1.5",
            "Gemini25", "Gemini 2.5",
            "Gemini20", "Gemini 2.0",
            "Gemini15", "Gemini 1.5",
            "GPT41", "GPT-4.1",
            "GPT4o", "GPT-4o",
            "ClaudeSonnet4", "Claude Sonnet 4",
            "Claude37", "Claude 3.7",
            "Claude35", "Claude 3.5",
            "Claude3", "Claude 3",
            "Llama33", "Llama 3.3",
            "Llama32", "Llama 3.2",
            "Llama31", "Llama 3.1"
        };

        public static string FormatEnumName(string enumValue)
        {
            if (string.IsNullOrEmpty(enumValue))
            {
                return enumValue;
            }

            return ApplyKnownReplacements(enumValue.Replace("_", " "));
        }

        public static string FormatModelName(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return "Unknown Model";
            }

            var cleaned = CleanModelName(modelId);

            if (cleaned.Contains(':'))
            {
                var parts = cleaned.Split(':', 2);
                var name = FormatBaseName(parts[0]);
                var tag = parts.Length > 1 ? parts[1] : string.Empty;

                if (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
                {
                    var size = ExtractModelSize(tag);
                    if (!string.IsNullOrEmpty(size))
                    {
                        return $"{name} ({size})";
                    }

                    return $"{name} ({tag})";
                }

                return name;
            }

            return FormatBaseName(cleaned);
        }

        public static string CleanModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var normalised = name.Replace('-', ' ').Replace('_', ' ');
            normalised = ApplyKnownReplacements(normalised);
            normalised = Regex.Replace(normalised, @"\s+", " ").Trim();

            normalised = Regex.Replace(normalised, @"\bqwen\b", "Qwen", RegexOptions.IgnoreCase);
            normalised = Regex.Replace(normalised, @"\bllama\b", "Llama", RegexOptions.IgnoreCase);
            normalised = Regex.Replace(normalised, @"\bmistral\b", "Mistral", RegexOptions.IgnoreCase);
            normalised = Regex.Replace(normalised, @"\bgemma\b", "Gemma", RegexOptions.IgnoreCase);
            normalised = Regex.Replace(normalised, @"\bphi\b", "Phi", RegexOptions.IgnoreCase);
            normalised = Regex.Replace(normalised, @"\bcoder\b", "Coder", RegexOptions.IgnoreCase);
            normalised = Regex.Replace(normalised, @"\binstruct\b", "Instruct", RegexOptions.IgnoreCase);

            return normalised;
        }

        private static string FormatBaseName(string value)
        {
            var title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value);
            return ApplyKnownReplacements(title);
        }

        private static string ExtractModelSize(string tag)
        {
            var match = Regex.Match(tag, @"(\d+\.?\d*)\s*(b)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return $"{match.Groups[1].Value.ToUpperInvariant()}{match.Groups[2].Value.ToUpperInvariant()}";
            }

            var commonSizes = new[] { "7b", "13b", "30b", "70b" };
            var size = commonSizes.FirstOrDefault(s => tag.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            return size?.ToUpperInvariant();
        }

        private static string ApplyKnownReplacements(string value)
        {
            for (var i = 0; i < KnownVariants.Length; i += 2)
            {
                var source = KnownVariants[i];
                var replacement = KnownVariants[i + 1];
                value = value.Replace(source, replacement, StringComparison.OrdinalIgnoreCase);
            }

            return value;
        }
    }
}
