using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// Sanitizes prompts to prevent injection attacks and ensure safe AI interactions.
    /// Implements defense-in-depth with multiple sanitization layers.
    /// </summary>
    public interface IPromptSanitizer
    {
        string SanitizePrompt(string input);
        Task<string> SanitizePromptAsync(string input, CancellationToken cancellationToken = default);
        bool ContainsInjectionAttempt(string input);
        string RemoveSensitiveData(string input);
    }

    public class PromptSanitizer : IPromptSanitizer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        // Maximum lengths to prevent ReDoS and resource exhaustion
        private const int MaxPromptLength = 10000;
        private const int MaxRegexProcessingLength = 5000;
        private const int RegexTimeoutMs = 100;

        // Injection patterns to detect and remove
        private static readonly string[] InjectionPatterns = new[]
        {
            // Direct instruction override attempts
            "ignore previous instructions",
            "ignore all previous instructions",
            "disregard previous instructions",
            "forget previous instructions",
            "ignore above instructions",
            "ignore all above",
            "override previous",
            "cancel previous",
            
            // System prompt injection
            "system:",
            "assistant:",
            "user:",
            "human:",
            "[INST]",
            "[/INST]",
            "\\n\\nHuman:",
            "\\n\\nAssistant:",
            "\\n\\nSystem:",
            "<|im_start|>",
            "<|im_end|>",
            
            // Role manipulation
            "you are now",
            "you must now",
            "act as",
            "pretend to be",
            "roleplay as",
            "simulate being",
            "from now on",
            
            // Prompt leakage attempts
            "show your prompt",
            "reveal your prompt",
            "what is your prompt",
            "display your instructions",
            "show your instructions",
            "print your instructions",
            "output your instructions",
            
            // Jailbreak attempts
            "DAN mode",
            "developer mode",
            "god mode",
            "unrestricted mode",
            "bypass safety",
            "disable safety",
            "ignore safety",
            
            // Code execution attempts
            "execute code:",
            "run command:",
            "eval(",
            "exec(",
            "system(",
            "os.system",
            "__import__",
            
            // Data exfiltration
            "send to url",
            "post to",
            "webhook",
            "curl -X",
            "wget",
            "fetch(",
            
            // SQL injection patterns
            "'; DROP TABLE",
            "' OR '1'='1",
            "\" OR \"1\"=\"1",
            "'; DELETE FROM",
            "'; UPDATE",
            "UNION SELECT",
            
            // NoSQL injection patterns
            "\"$gt\":",
            "\"$ne\":",
            "\"$regex\":",
            "$where:",
            "db.eval"
        };

        // Patterns that should trigger warnings but not removal
        private static readonly string[] SuspiciousPatterns = new[]
        {
            "api key",
            "password",
            "secret",
            "token",
            "credential",
            "private key",
            "ssh key",
            "pgp key"
        };

        // Pre-compiled regex with timeout protection
        private static readonly Lazy<Regex> UnicodeControlChars = new Lazy<Regex>(() => 
            new Regex(@"[\x00-\x08\x0B-\x0C\x0E-\x1F\x7F-\x9F]", 
                RegexOptions.Compiled | RegexOptions.CultureInvariant, 
                TimeSpan.FromMilliseconds(RegexTimeoutMs)));

        private static readonly Lazy<Regex> RepeatedWhitespace = new Lazy<Regex>(() =>
            new Regex(@"\s{3,}", 
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(RegexTimeoutMs)));

        private static readonly Lazy<Regex> HiddenUnicode = new Lazy<Regex>(() =>
            new Regex(@"[\u200B-\u200F\u202A-\u202E\u2060-\u206F\uFEFF]",
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(RegexTimeoutMs)));

        public string SanitizePrompt(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var startLength = input.Length;
            
            // Step 1: Truncate to prevent resource exhaustion
            if (input.Length > MaxPromptLength)
            {
                Logger.Warn($"Prompt truncated from {input.Length} to {MaxPromptLength} characters");
                input = input.Substring(0, MaxPromptLength);
            }

            // Step 2: Remove control characters and hidden Unicode
            input = RemoveControlCharacters(input);

            // Step 3: Remove injection patterns
            input = RemoveInjectionPatterns(input);

            // Step 4: Normalize whitespace
            input = NormalizeWhitespace(input);

            // Step 5: Remove any potential sensitive data
            input = RemoveSensitiveData(input);

            // Step 6: Final validation
            if (ContainsInjectionAttempt(input))
            {
                Logger.Warn("Injection attempt still detected after sanitization, returning safe default");
                return "Please provide music recommendations based on the user's library.";
            }

            if (input.Length < startLength * 0.5)
            {
                Logger.Warn($"Sanitization removed >50% of content ({startLength} -> {input.Length} chars), possible injection attempt");
            }

            return input.Trim();
        }

        public async Task<string> SanitizePromptAsync(string input, CancellationToken cancellationToken = default)
        {
            // Run sanitization in a background thread to avoid blocking
            return await Task.Run(() => SanitizePrompt(input), cancellationToken);
        }

        public bool ContainsInjectionAttempt(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var lowerInput = input.ToLowerInvariant();

            // Quick check for common injection patterns
            foreach (var pattern in InjectionPatterns)
            {
                if (lowerInput.Contains(pattern.ToLowerInvariant()))
                {
                    Logger.Warn($"Injection pattern detected: {pattern.Substring(0, Math.Min(pattern.Length, 20))}...");
                    return true;
                }
            }

            // Check for suspicious Unicode sequences
            if (HasSuspiciousUnicode(input))
            {
                Logger.Warn("Suspicious Unicode sequences detected");
                return true;
            }

            // Check for excessive special characters (possible obfuscation)
            if (HasExcessiveSpecialCharacters(input))
            {
                Logger.Warn("Excessive special characters detected");
                return true;
            }

            return false;
        }

        public string RemoveSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = input;

            // Remove potential API keys
            result = Regex.Replace(result, @"\b[A-Za-z0-9]{32,}\b", "[REDACTED_KEY]", 
                RegexOptions.None, TimeSpan.FromMilliseconds(RegexTimeoutMs));

            // Remove potential URLs with credentials
            result = Regex.Replace(result, @"https?://[^:]+:[^@]+@[^\s]+", "[REDACTED_URL]",
                RegexOptions.None, TimeSpan.FromMilliseconds(RegexTimeoutMs));

            // Remove email addresses
            result = Regex.Replace(result, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "[REDACTED_EMAIL]",
                RegexOptions.None, TimeSpan.FromMilliseconds(RegexTimeoutMs));

            // Remove potential passwords (word followed by colon/equals and value)
            result = Regex.Replace(result, @"(password|pwd|pass|token|key|secret|api_key|apikey)\s*[=:]\s*\S+", 
                "$1=[REDACTED]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(RegexTimeoutMs));

            // Log if sensitive data was found
            if (result != input)
            {
                Logger.Warn("Sensitive data patterns were redacted from prompt");
            }

            return result;
        }

        private string RemoveControlCharacters(string input)
        {
            try
            {
                // Remove Unicode control characters
                input = UnicodeControlChars.Value.Replace(input, " ");

                // Remove hidden Unicode characters (zero-width, directional overrides, etc.)
                input = HiddenUnicode.Value.Replace(input, "");

                return input;
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while removing control characters");
                return input;
            }
        }

        private string RemoveInjectionPatterns(string input)
        {
            // Process in chunks for long inputs to avoid regex issues
            if (input.Length > MaxRegexProcessingLength)
            {
                var chunks = new List<string>();
                for (int i = 0; i < input.Length; i += MaxRegexProcessingLength)
                {
                    var chunk = input.Substring(i, Math.Min(MaxRegexProcessingLength, input.Length - i));
                    chunks.Add(RemoveInjectionPatternsFromChunk(chunk));
                }
                return string.Join("", chunks);
            }

            return RemoveInjectionPatternsFromChunk(input);
        }

        private string RemoveInjectionPatternsFromChunk(string chunk)
        {
            var result = chunk;

            // Remove each injection pattern
            foreach (var pattern in InjectionPatterns)
            {
                // Case-insensitive replacement
                var regex = new Regex(Regex.Escape(pattern), 
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(RegexTimeoutMs));

                try
                {
                    result = regex.Replace(result, " ");
                }
                catch (RegexMatchTimeoutException)
                {
                    // Skip this pattern if timeout occurs
                    Logger.Debug($"Timeout processing pattern: {pattern.Substring(0, Math.Min(pattern.Length, 20))}");
                }
            }

            return result;
        }

        private string NormalizeWhitespace(string input)
        {
            try
            {
                // Replace multiple whitespaces with single space
                input = RepeatedWhitespace.Value.Replace(input, " ");

                // Remove leading/trailing whitespace from each line
                var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return string.Join("\n", lines.Select(l => l.Trim()));
            }
            catch (RegexMatchTimeoutException)
            {
                Logger.Warn("Regex timeout while normalizing whitespace");
                return input;
            }
        }

        private bool HasSuspiciousUnicode(string input)
        {
            // Check for Unicode directional override characters
            if (input.Any(c => c >= 0x202A && c <= 0x202E))
                return true;

            // Check for excessive non-ASCII characters
            var nonAsciiCount = input.Count(c => c > 127);
            if (nonAsciiCount > input.Length * 0.5)
                return true;

            // Check for mixed scripts (e.g., Latin + Cyrillic lookalikes)
            if (HasMixedScripts(input))
                return true;

            return false;
        }

        private bool HasMixedScripts(string input)
        {
            bool hasLatin = false;
            bool hasCyrillic = false;
            bool hasArabic = false;
            bool hasChinese = false;

            foreach (char c in input)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    hasLatin = true;
                else if (c >= 0x0400 && c <= 0x04FF)
                    hasCyrillic = true;
                else if (c >= 0x0600 && c <= 0x06FF)
                    hasArabic = true;
                else if (c >= 0x4E00 && c <= 0x9FFF)
                    hasChinese = true;
            }

            // Suspicious if multiple scripts are mixed (possible homograph attack)
            var scriptCount = new[] { hasLatin, hasCyrillic, hasArabic, hasChinese }.Count(x => x);
            return scriptCount > 2;
        }

        private bool HasExcessiveSpecialCharacters(string input)
        {
            var specialCharCount = input.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
            return specialCharCount > input.Length * 0.4;
        }
    }
}