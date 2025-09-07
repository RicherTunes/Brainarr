using System;
using System.Globalization;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Utils
{
    public static class ModelNameFormatter
    {
        public static string FormatEnumName(string enumValue)
        {
            if (string.IsNullOrEmpty(enumValue)) return enumValue;
            return enumValue
                .Replace("_", " ")
                .Replace("GPT4o", "GPT-4o")
                .Replace("Claude35", "Claude 3.5")
                .Replace("Claude3", "Claude 3")
                .Replace("Llama33", "Llama 3.3")
                .Replace("Llama32", "Llama 3.2")
                .Replace("Llama31", "Llama 3.1")
                .Replace("Gemini15", "Gemini 1.5")
                .Replace("Gemini20", "Gemini 2.0");
        }

        public static string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "Unknown Model";

            var name = CleanModelName(modelId);

            if (name.Contains(':'))
            {
                var parts = name.Split(':');
                var modelName = parts[0];
                var tag = parts.Length > 1 ? parts[1] : string.Empty;

                modelName = CultureInfo.CurrentCulture.TextInfo
                    .ToTitleCase(modelName.Replace("-", " ").Replace("_", " "));

                if (!string.IsNullOrEmpty(tag) && !string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    var size = ExtractModelSize(tag);
                    modelName = !string.IsNullOrEmpty(size) ? $"{modelName} ({size})" : $"{modelName} ({tag})";
                }

                return modelName;
            }

            return CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(name.Replace("-", " ").Replace("_", " "));
        }

        public static string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var cleaned = name
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace(".", " ");

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bqwen\\b", "Qwen", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bllama\\b", "Llama", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bmistral\\b", "Mistral", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bgemma\\b", "Gemma", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bphi\\b", "Phi", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\bcoder\\b", "Coder", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\binstruct\\b", "Instruct", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\s+", " ").Trim();
            return cleaned;
        }

        private static string ExtractModelSize(string tag)
        {
            var sizePattern = @"(\\d+\\.?\\d*[bB])";
            var match = System.Text.RegularExpressions.Regex.Match(tag, sizePattern);
            if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
            if (tag.IndexOf("7b", StringComparison.OrdinalIgnoreCase) >= 0) return "7B";
            if (tag.IndexOf("13b", StringComparison.OrdinalIgnoreCase) >= 0) return "13B";
            if (tag.IndexOf("30b", StringComparison.OrdinalIgnoreCase) >= 0) return "30B";
            if (tag.IndexOf("70b", StringComparison.OrdinalIgnoreCase) >= 0) return "70B";
            return null;
        }
    }
}
