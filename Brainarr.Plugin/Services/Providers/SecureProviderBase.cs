using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Security-hardened base class for AI providers with comprehensive protection
    /// </summary>
    public abstract class SecureProviderBase : IAIProvider
    {
        protected readonly Logger _logger;
        protected readonly IRateLimiter _rateLimiter;
        protected readonly IRecommendationSanitizer _sanitizer;
        private readonly HashSet<string> _sensitivePatterns;
        private readonly SemaphoreSlim _concurrencyLimiter;
        
        // Security patterns
        private static readonly Regex ApiKeyPattern = new(@"\b[A-Za-z0-9]{32,}\b", RegexOptions.Compiled);
        private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IpAddressPattern = new(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.Compiled);
        private static readonly Regex CreditCardPattern = new(@"\b(?:\d[ -]*?){13,16}\b", RegexOptions.Compiled);
        
        public abstract string ProviderName { get; }
        public abstract bool RequiresApiKey { get; }
        public abstract bool SupportsStreaming { get; }
        public abstract int MaxRecommendations { get; }
        
        protected SecureProviderBase(
            Logger logger,
            IRateLimiter? rateLimiter = null,
            IRecommendationSanitizer? sanitizer = null,
            int maxConcurrency = 5)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiter = rateLimiter ?? new RateLimiter(logger);
            _sanitizer = sanitizer ?? new RecommendationSanitizer(logger);
            _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            
            _sensitivePatterns = new HashSet<string>
            {
                "password", "secret", "token", "key", "auth",
                "credential", "private", "ssn", "social security"
            };
        }

        public virtual async Task<List<Recommendation>> GetRecommendationsAsync(
            LibraryProfile profile,
            int maxRecommendations,
            CancellationToken cancellationToken = default)
        {
            ValidateInput(profile, maxRecommendations);
            
            await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Apply rate limiting
                return await _rateLimiter.ExecuteAsync(
                    ProviderName,
                    async () =>
                    {
                        var recommendations = await GetRecommendationsInternalAsync(
                            profile, 
                            maxRecommendations, 
                            cancellationToken).ConfigureAwait(false);
                        
                        // Sanitize all recommendations
                        return SanitizeRecommendations(recommendations);
                    }).ConfigureAwait(false);
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }

        protected abstract Task<List<Recommendation>> GetRecommendationsInternalAsync(
            LibraryProfile profile,
            int maxRecommendations,
            CancellationToken cancellationToken);

        public virtual async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await TestConnectionInternalAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Connection test failed for {ProviderName}");
                return false;
            }
        }

        protected abstract Task<bool> TestConnectionInternalAsync(CancellationToken cancellationToken);

        protected virtual void ValidateInput(LibraryProfile profile, int maxRecommendations)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
                
            if (maxRecommendations <= 0 || maxRecommendations > MaxRecommendations)
                throw new ArgumentException(
                    $"Max recommendations must be between 1 and {MaxRecommendations}", 
                    nameof(maxRecommendations));

            // Validate profile data doesn't contain injection attempts
            ValidateProfileSecurity(profile);
        }

        private void ValidateProfileSecurity(LibraryProfile profile)
        {
            // Check for SQL injection patterns in genre preferences
            foreach (var genre in profile.TopGenres?.Keys ?? Enumerable.Empty<string>())
            {
                if (ContainsSqlInjection(genre))
                {
                    throw new SecurityException($"Invalid genre data detected: potential SQL injection");
                }
            }

            // Check for script injection in artist names
            foreach (var artist in profile.TopArtists ?? Enumerable.Empty<string>())
            {
                if (ContainsScriptInjection(artist))
                {
                    throw new SecurityException($"Invalid artist data detected: potential script injection");
                }
            }
        }

        private bool ContainsSqlInjection(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;
                
            var sqlPatterns = new[]
            {
                @"(\b(DELETE|DROP|EXEC(UTE)?|INSERT|SELECT|UNION|UPDATE|OR|AND)\b)",
                @"(--)|(/\*)|(\*/)|(')",
                @"(;|\||&&)",
                @"(""|=|<|>)",  // Quotes and comparison operators
                @"(\bOR\b.*=)",  // OR with equals
                @"(1\s*=\s*1)"   // Classic injection pattern
            };

            return sqlPatterns.Any(pattern => 
                Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        private bool ContainsScriptInjection(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var scriptPatterns = new[]
            {
                @"<script[^>]*>.*?</script>",
                @"javascript:",
                @"on\w+\s*=",
                @"<iframe",
                @"<img[^>]*onerror",
                @"<svg[^>]*onload",
                @"eval\s*\(",
                @"expression\s*\(",
                @"alert\s*\(",
                @"<[^>]*>"  // Any HTML tags
            };

            return scriptPatterns.Any(pattern => 
                Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        protected List<Recommendation> SanitizeRecommendations(List<Recommendation> recommendations)
        {
            if (recommendations == null || !recommendations.Any())
                return new List<Recommendation>();

            var sanitized = new List<Recommendation>();
            
            foreach (var rec in recommendations)
            {
                try
                {
                    var clean = new Recommendation
                    {
                        Artist = SanitizeString(rec.Artist, 500),
                        Album = SanitizeString(rec.Album, 500),
                        Genre = SanitizeString(rec.Genre, 100),
                        Reason = SanitizeString(rec.Reason, 1000),
                        Confidence = Math.Max(0.0, Math.Min(1.0, rec.Confidence)),
                        MusicBrainzId = SanitizeGuid(rec.MusicBrainzId),
                        ReleaseYear = ValidateYear(rec.ReleaseYear),
                        SpotifyId = SanitizeString(rec.SpotifyId, 50),
                        Provider = ProviderName
                    };
                    
                    sanitized.Add(clean);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to sanitize recommendation: {ex.Message}");
                }
            }

            return sanitized;
        }

        protected string SanitizeString(string input, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Remove control characters
            input = Regex.Replace(input, @"[\x00-\x1F\x7F]", "", RegexOptions.Compiled);
            
            // Remove script tags and their content (including any attributes and nested content)
            input = Regex.Replace(input, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            
            // Alternative script tag pattern for simpler cases
            input = Regex.Replace(input, @"<script>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
            
            // Remove any remaining HTML tags
            input = Regex.Replace(input, @"<[^>]*>", "", RegexOptions.Compiled);
            
            // Remove javascript: protocol
            input = Regex.Replace(input, @"javascript:", "", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            
            // Remove any alert() calls that might remain
            input = Regex.Replace(input, @"alert\s*\([^)]*\)", "", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            
            // Truncate to max length
            if (input.Length > maxLength)
            {
                input = input.Substring(0, maxLength);
            }

            return input.Trim();
        }

        protected string SanitizeGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return null;

            if (Guid.TryParse(guid, out var parsed))
                return parsed.ToString();

            return null;
        }

        protected int? ValidateYear(int? year)
        {
            if (!year.HasValue)
                return null;

            var currentYear = DateTime.UtcNow.Year;
            if (year < 1900 || year > currentYear + 1)
                return null;

            return year;
        }

        /// <summary>
        /// Sanitize log output to prevent sensitive data leakage
        /// </summary>
        protected string SanitizeForLogging(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            // Remove API keys
            message = ApiKeyPattern.Replace(message, "[REDACTED-KEY]");
            
            // Remove email addresses
            message = EmailPattern.Replace(message, "[REDACTED-EMAIL]");
            
            // Remove IP addresses
            message = IpAddressPattern.Replace(message, "[REDACTED-IP]");
            
            // Remove credit card numbers
            message = CreditCardPattern.Replace(message, "[REDACTED-CC]");
            
            // Remove other sensitive patterns
            foreach (var pattern in _sensitivePatterns)
            {
                if (message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Match pattern followed by colon and value (e.g., "Password: value123")
                    var regex = new Regex($@"\b{Regex.Escape(pattern)}\b\s*:?\s*\S+", 
                        RegexOptions.IgnoreCase);
                    message = regex.Replace(message, $"[REDACTED-{pattern.ToUpper()}]");
                }
            }

            return message;
        }

        /// <summary>
        /// Create secure HTTP request with proper headers
        /// </summary>
        protected HttpRequestMessage CreateSecureRequest(
            HttpMethod method,
            string uri,
            string? content = null,
            string? apiKey = null)
        {
            var request = new HttpRequestMessage(method, uri);
            
            // Security headers
            request.Headers.Add("X-Request-ID", Guid.NewGuid().ToString());
            request.Headers.Add("User-Agent", $"Brainarr/{GetType().Assembly.GetName().Version}");
            
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                request.Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json");
                
                // Add content hash for integrity
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    var hashString = Convert.ToBase64String(hash);
                    request.Headers.Add("X-Content-Hash", hashString);
                }
            }

            return request;
        }

        public virtual void Dispose()
        {
            _concurrencyLimiter?.Dispose();
        }

        // Abstract methods that concrete implementations must provide
        public abstract Task<List<Recommendation>> GetRecommendationsAsync(string prompt);
        public abstract Task<bool> TestConnectionAsync();
        public abstract void UpdateModel(string modelName);
    }

    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
        public SecurityException(string message, Exception innerException) 
            : base(message, innerException) { }
    }
}