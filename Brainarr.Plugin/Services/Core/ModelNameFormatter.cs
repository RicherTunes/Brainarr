using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
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
            
            if (modelId.Contains("/"))
            {
                var parts = modelId.Split('/');
                if (parts.Length >= 2)
                {
                    var org = parts[0];
                    var modelName = parts[1];
                    var cleanName = CleanModelName(modelName);
                    return $"{cleanName} ({org})";
                }
            }
            
            if (modelId.Contains(":"))
            {
                var parts = modelId.Split(':');
                if (parts.Length >= 2)
                {
                    var modelName = CleanModelName(parts[0]);
                    var tag = parts[1];
                    return $"{modelName}:{tag}";
                }
            }
            
            return CleanModelName(modelId);
        }

        private static string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            
            var cleaned = name
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace(".", " ");
            
            cleaned = Regex.Replace(cleaned, @"\bqwen\b", "Qwen", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bllama\b", "Llama", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bmistral\b", "Mistral", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bgemma\b", "Gemma", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bphi\b", "Phi", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bcoder\b", "Coder", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\binstruct\b", "Instruct", RegexOptions.IgnoreCase);
            
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            return cleaned;
        }
    }
}