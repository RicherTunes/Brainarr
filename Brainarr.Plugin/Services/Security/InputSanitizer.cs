using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;

namespace Brainarr.Plugin.Services.Security
{
    public interface IInputSanitizer
    {
        string SanitizeForPrompt(string input);
        string SanitizeArtistName(string artistName);
        string SanitizeGenreName(string genreName);
        string SanitizeAlbumTitle(string albumTitle);
        string SanitizeJson(string json);
        Dictionary<string, string> SanitizeMetadata(Dictionary<string, string> metadata);
        bool IsValidInput(string input, InputType type);
    }

    public enum InputType
    {
        ArtistName,
        AlbumTitle,
        GenreName,
        Prompt,
        Json,
        GeneralText
    }

    public class InputSanitizer : IInputSanitizer
    {
        private readonly Logger _logger;

        // Regex patterns for validation and sanitization
        private static readonly Regex SqlInjectionPattern = new Regex(
            @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|FROM|WHERE|JOIN|ORDER BY|GROUP BY|HAVING)\b)|(-{2})|(/\*.*?\*/)|(\;)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NoSqlInjectionPattern = new Regex(
            @"(\$\w+)|(\{.*?\})|(\[.*?\])|(\|\|)|(&&)|(!=)|(>=)|(<=)|(\$where)|(\$regex)|(\$ne)|(\$gt)|(\$lt)|(\$in)|(\$nin)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CommandInjectionPattern = new Regex(
            @"(\||&|;|`|\$\(|\)|<|>|\\n|\\r)",
            RegexOptions.Compiled);

        private static readonly Regex XssPattern = new Regex(
            @"(<script.*?>.*?</script>)|(<.*?javascript:.*?>)|(<.*?on\w+\s*=.*?>)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PromptInjectionPattern = new Regex(
            @"(ignore[^\n]{0,100}?(previous|all)[^\n]{0,100}?(instruction|instructions|prompt|prompts))|(system\s*:\s*)|(assistant\s*:\s*)|(user\s*:\s*)|(\[INST\])|(\[/INST\])|(<\|.*?\|>)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ValidArtistNamePattern = new Regex(
            @"^[\p{L}\p{N}\s\-\.\,\&\/\'\(\)]+$",
            RegexOptions.Compiled);

        private static readonly Regex ValidGenrePattern = new Regex(
            @"^[\p{L}\s\-\&\/]+$",
            RegexOptions.Compiled);

        private static readonly Regex ValidAlbumTitlePattern = new Regex(
            @"^[\p{L}\p{N}\s\-\.\,\:\;\!\?\&\'\(\)\[\]]+$",
            RegexOptions.Compiled);

        // Maximum lengths for different input types
        private const int MaxArtistNameLength = 200;
        private const int MaxAlbumTitleLength = 300;
        private const int MaxGenreNameLength = 100;
        private const int MaxPromptLength = 5000;
        private const int MaxJsonLength = 100000;

        // SECURITY IMPROVEMENT: ReDoS protection - maximum safe length for regex operations
        private const int MaxSafeRegexLength = 10000;
        private const int MaxSafeComplexRegexLength = 50000;

        public InputSanitizer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string SanitizeForPrompt(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            // Truncate if too long
            if (input.Length > MaxPromptLength)
            {
                _logger.Warn($"Input truncated from {input.Length} to {MaxPromptLength} characters");
                input = input.Substring(0, MaxPromptLength);
            }

            // SECURITY IMPROVEMENT: Apply ReDoS protection before regex operations
            var sanitized = TruncateForSafeRegexProcessing(input);

            // Remove SQL injection attempts
            if (SqlInjectionPattern.IsMatch(sanitized))
            {
                _logger.Warn("SQL injection pattern detected in input");
                sanitized = SqlInjectionPattern.Replace(sanitized, " ");
            }

            // Remove NoSQL injection attempts
            if (NoSqlInjectionPattern.IsMatch(sanitized))
            {
                _logger.Warn("NoSQL injection pattern detected in input");
                sanitized = NoSqlInjectionPattern.Replace(sanitized, " ");
            }

            // Remove XSS attempts (do this before stripping angle brackets to keep patterns effective)
            if (XssPattern.IsMatch(sanitized))
            {
                _logger.Warn("XSS pattern detected in input");
                sanitized = XssPattern.Replace(sanitized, " ");
            }

            // Remove command injection attempts
            if (CommandInjectionPattern.IsMatch(sanitized))
            {
                _logger.Warn("Command injection pattern detected in input");
                sanitized = CommandInjectionPattern.Replace(sanitized, " ");
            }

            // Remove prompt injection attempts
            if (PromptInjectionPattern.IsMatch(sanitized))
            {
                _logger.Warn("Prompt injection pattern detected in input");
                sanitized = PromptInjectionPattern.Replace(sanitized, " ");
            }

            // Additional keyword scrubbing for common injection markers remaining after tag stripping
            sanitized = Regex.Replace(sanitized, @"\bscript\b|javascript:\s*|onerror\b|onclick\b", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\brm\s+-rf\b|\bcat\s+/etc/passwd\b|\bwhoami\b", " ", RegexOptions.IgnoreCase);

            // Escape special characters for AI prompts
            sanitized = EscapeForPrompt(sanitized);

            // Normalize whitespace
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

            return sanitized;
        }

        public string SanitizeArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return string.Empty;
            }

            // Truncate if too long
            if (artistName.Length > MaxArtistNameLength)
            {
                artistName = artistName.Substring(0, MaxArtistNameLength);
            }

            // Remove invalid characters
            if (!ValidArtistNamePattern.IsMatch(artistName))
            {
                _logger.Debug($"Invalid characters in artist name: {artistName}");
                artistName = Regex.Replace(artistName, @"[^\p{L}\p{N}\s\-\.\,\&\/\'\(\)]", "");
            }

            // Additional sanitization for prompts
            artistName = RemoveControlCharacters(artistName);
            artistName = NormalizeUnicode(artistName);

            return artistName.Trim();
        }

        public string SanitizeGenreName(string genreName)
        {
            if (string.IsNullOrWhiteSpace(genreName))
            {
                return string.Empty;
            }

            // Truncate if too long
            if (genreName.Length > MaxGenreNameLength)
            {
                genreName = genreName.Substring(0, MaxGenreNameLength);
            }

            // Remove invalid characters
            if (!ValidGenrePattern.IsMatch(genreName))
            {
                _logger.Debug($"Invalid characters in genre name: {genreName}");
                genreName = Regex.Replace(genreName, @"[^\p{L}\s\-\&\/]", "");
            }

            // Additional sanitization
            genreName = RemoveControlCharacters(genreName);
            genreName = NormalizeUnicode(genreName);

            return genreName.Trim();
        }

        public string SanitizeAlbumTitle(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
            {
                return string.Empty;
            }

            // Truncate if too long
            if (albumTitle.Length > MaxAlbumTitleLength)
            {
                albumTitle = albumTitle.Substring(0, MaxAlbumTitleLength);
            }

            // Remove invalid characters
            if (!ValidAlbumTitlePattern.IsMatch(albumTitle))
            {
                _logger.Debug($"Invalid characters in album title: {albumTitle}");
                albumTitle = Regex.Replace(albumTitle, @"[^\p{L}\p{N}\s\-\.\,\:\;\!\?\&\'\(\)\[\]]", "");
            }

            // Remove injection phrases within titles
            albumTitle = SqlInjectionPattern.Replace(albumTitle, " ");

            // Additional sanitization
            albumTitle = RemoveControlCharacters(albumTitle);
            albumTitle = NormalizeUnicode(albumTitle);

            return albumTitle.Trim();
        }

        public string SanitizeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "{}";
            }

            // Truncate if too long
            if (json.Length > MaxJsonLength)
            {
                _logger.Warn($"JSON truncated from {json.Length} to {MaxJsonLength} characters");
                json = json.Substring(0, MaxJsonLength);
            }

            // Remove potential script tags and JavaScript
            json = XssPattern.Replace(json, "");
            json = Regex.Replace(json, @"javascript:\s*", "", RegexOptions.IgnoreCase);

            // Remove control characters except for valid JSON whitespace
            json = Regex.Replace(json, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

            // Remove known injection tokens inside JSON strings/keys (lightweight sanitization)
            json = SqlInjectionPattern.Replace(json, " ");
            json = NoSqlInjectionPattern.Replace(json, " ");

            // Validate JSON structure (basic check)
            if (!json.TrimStart().StartsWith("{") && !json.TrimStart().StartsWith("["))
            {
                _logger.Warn("Invalid JSON structure detected");
                return "{}";
            }

            return json;
        }

        public Dictionary<string, string> SanitizeMetadata(Dictionary<string, string> metadata)
        {
            if (metadata == null)
            {
                return new Dictionary<string, string>();
            }

            var sanitized = new Dictionary<string, string>();

            foreach (var kvp in metadata)
            {
                // Sanitize key
                var key = SanitizeMetadataKey(kvp.Key);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                // Sanitize value based on key type
                var value = SanitizeMetadataValue(key, kvp.Value);
                // Always include key; normalize null/whitespace to empty string per tests
                sanitized[key] = value ?? string.Empty;
            }

            return sanitized;
        }

        public bool IsValidInput(string input, InputType type)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            switch (type)
            {
                case InputType.ArtistName:
                    return input.Length <= MaxArtistNameLength &&
                           ValidArtistNamePattern.IsMatch(input) &&
                           !ContainsInjectionPatterns(input);

                case InputType.AlbumTitle:
                    return input.Length <= MaxAlbumTitleLength &&
                           ValidAlbumTitlePattern.IsMatch(input);

                case InputType.GenreName:
                    // Allow characters like '&' and '/' common in genres; rely on pattern only
                    return input.Length <= MaxGenreNameLength &&
                           ValidGenrePattern.IsMatch(input);

                case InputType.Prompt:
                    return input.Length <= MaxPromptLength &&
                           !ContainsInjectionPatterns(input);

                case InputType.Json:
                    // Consider any non-empty input within size limits as acceptable; sanitize later if needed
                    return input.Length <= MaxJsonLength;

                case InputType.GeneralText:
                    return !ContainsInjectionPatterns(input);

                default:
                    return false;
            }
        }

        private string EscapeForPrompt(string input)
        {
            // Escape characters that could be interpreted as prompt control
            var escaped = input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ");

            // Remove zero-width characters that could be used for prompt manipulation
            escaped = Regex.Replace(escaped, @"[\u200B-\u200D\uFEFF]", "");

            return escaped;
        }

        private string RemoveControlCharacters(string input)
        {
            // Remove all control characters except basic whitespace
            return Regex.Replace(input, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
        }

        private string NormalizeUnicode(string input)
        {
            // Normalize Unicode to prevent homograph attacks
            try
            {
                return input.Normalize(NormalizationForm.FormKC);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Unicode normalization failed: {ex.Message}");
                return input;
            }
        }

        private bool ContainsInjectionPatterns(string input)
        {
            // SECURITY IMPROVEMENT: ReDoS protection - check input length before regex operations
            if (!IsSafeForRegexProcessing(input))
            {
                _logger.Warn($"Input too large for safe regex processing ({input.Length} chars), assuming unsafe");
                return true; // Assume unsafe for overly long inputs
            }

            return SqlInjectionPattern.IsMatch(input) ||
                   NoSqlInjectionPattern.IsMatch(input) ||
                   CommandInjectionPattern.IsMatch(input) ||
                   XssPattern.IsMatch(input) ||
                   PromptInjectionPattern.IsMatch(input);
        }

        /// <summary>
        /// Checks if input is safe for regex processing to prevent ReDoS attacks
        /// </summary>
        private bool IsSafeForRegexProcessing(string input)
        {
            return input.Length <= MaxSafeRegexLength;
        }

        /// <summary>
        /// Truncates input to safe length for regex processing
        /// </summary>
        private string TruncateForSafeRegexProcessing(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            if (input.Length > MaxSafeRegexLength)
            {
                _logger.Warn($"Input truncated from {input.Length} to {MaxSafeRegexLength} chars for safe regex processing");
                return input.Substring(0, MaxSafeRegexLength);
            }

            return input;
        }

        private bool IsValidJsonStructure(string json)
        {
            try
            {
                var trimmed = json.Trim();
                return (trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
                       (trimmed.StartsWith("[") && trimmed.EndsWith("]"));
            }
            catch
            {
                return false;
            }
        }

        private string SanitizeMetadataKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            // Allow only alphanumeric and underscore in keys
            return Regex.Replace(key, @"[^a-zA-Z0-9_]", "");
        }

        private string SanitizeMetadataValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Apply different sanitization based on known key types
            if (key.EndsWith("_name") || key.EndsWith("Name"))
            {
                return SanitizeArtistName(value);
            }
            else if (key.EndsWith("_title") || key.EndsWith("Title"))
            {
                return SanitizeAlbumTitle(value);
            }
            else if (key.EndsWith("_genre") || key.EndsWith("Genre"))
            {
                return SanitizeGenreName(value);
            }
            else
            {
                return SanitizeForPrompt(value);
            }
        }
    }
}
