using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Centralized policy for timeouts, rate limits, provider identifiers, and canonical regexes.
    /// Serves as single source of truth while refactor proceeds.
    /// </summary>
    public static class Policy
    {
        // Provider identifiers
        public static class Providers
        {
            public const string MusicBrainz = "musicbrainz";
            public const string OpenAI = "openai";
            public const string Perplexity = "perplexity";
            public const string Anthropic = "anthropic";
            public const string OpenRouter = "openrouter";
            public const string DeepSeek = "deepseek";
            public const string Gemini = "gemini";
            public const string Groq = "groq";
            public const string Ollama = "ollama";
            public const string LMStudio = "lmstudio";
        }

        // Default operation-level timeouts
        public static class Timeouts
        {
            public static readonly TimeSpan Short = TimeSpan.FromSeconds(10);
            public static readonly TimeSpan Default = TimeSpan.FromSeconds(BrainarrConstants.DefaultAITimeout);
            public static readonly TimeSpan TestConnection = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);
        }

        // Per-origin rate limits (requests per second) and burst sizes
        public static class RateLimits
        {
            // Keep MB at 1 rps globally per spec
            public static readonly (double rps, int burst) MusicBrainz = (1.0, 1);
            public static readonly (double rps, int burst) OpenAI = (2.0, 2);
            public static readonly (double rps, int burst) Perplexity = (2.0, 2);
            public static readonly (double rps, int burst) Anthropic = (2.0, 2);
            public static readonly (double rps, int burst) OpenRouter = (2.0, 2);
            public static readonly (double rps, int burst) DeepSeek = (2.0, 2);
            public static readonly (double rps, int burst) Gemini = (2.0, 2);
            public static readonly (double rps, int burst) Groq = (2.0, 2);
            public static readonly (double rps, int burst) Local = (5.0, 5);
        }

        // Validation toggles and defaults
        public static class Validation
        {
            public const bool RequireMbids = false; // can be enabled later
            public const double MinConfidence = 0.0; // only lower-bound clamp
        }

        // Canonical regexes with timeouts to avoid ReDoS
        public static class Regexes
        {
            private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(100);

            public static readonly Regex SuspiciousFuture = new(
                pattern: @"(anniversary|remaster(ed)?|ai[-\s]?cover)",
                options: RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
                matchTimeout: DefaultTimeout);

            public static readonly Regex StripMarkdownFence = new(
                pattern: @"```+\w*|```",
                options: RegexOptions.Compiled | RegexOptions.CultureInvariant,
                matchTimeout: DefaultTimeout);

            public static readonly Regex NormalizeWhitespace = new(
                pattern: @"\s+",
                options: RegexOptions.Compiled | RegexOptions.CultureInvariant,
                matchTimeout: DefaultTimeout);
        }
    }
}
