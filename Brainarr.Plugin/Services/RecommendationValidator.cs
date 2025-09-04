using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Service responsible for validating AI-generated music recommendations to filter out
    /// hallucinated or non-existent albums while preserving legitimate releases.
    /// </summary>
    public interface IRecommendationValidator
    {
        /// <summary>
        /// Validates whether a recommendation appears to be a real album.
        /// </summary>
        /// <param name="recommendation">The recommendation to validate</param>
        /// <param name="allowArtistOnly">Whether to allow artist-only recommendations (no album required)</param>
        /// <returns>True if the recommendation appears valid; false if it's likely fictional</returns>
        bool ValidateRecommendation(Recommendation recommendation, bool allowArtistOnly = false);

        /// <summary>
        /// Validates a batch of recommendations and returns filtering statistics.
        /// </summary>
        /// <param name="recommendations">The recommendations to validate</param>
        /// <param name="allowArtistOnly">Whether to allow artist-only recommendations (no album required)</param>
        /// <returns>Validated recommendations with statistics</returns>
        ValidationResult ValidateBatch(List<Recommendation> recommendations, bool allowArtistOnly = false);
    }

    /// <summary>
    /// Result of batch validation including statistics for monitoring.
    /// Converted to record type for immutability and value semantics.
    /// </summary>
    public record ValidationResult
    {
        public List<Recommendation> ValidRecommendations { get; init; } = new();
        public List<Recommendation> FilteredRecommendations { get; init; } = new();
        public int TotalCount { get; init; }
        public int ValidCount { get; init; }
        public int FilteredCount { get; init; }
        public double PassRate => TotalCount > 0 ? (100.0 * ValidCount / TotalCount) : 0;
        public Dictionary<string, int> FilterReasons { get; init; } = new Dictionary<string, int>();
        // Optional per-item reasons for easier debug (key: "Artist - Album")
        public Dictionary<string, string> FilterDetails { get; init; } = new Dictionary<string, string>();
    }

    public class RecommendationValidator : IRecommendationValidator
    {
        private readonly Logger _logger;
        private readonly string[] _customPatterns;
        private readonly bool _strictMode;
        
        // Patterns that are almost certainly AI hallucinations
        // These are terms that AI models commonly append to real album names
        private static readonly string[] DefinitelyFictionalPatterns = new[]
        {
            "(reimagined)",           // AI loves to "reimagine" albums
            "(re-imagined)",         
            "(ai imagined",           // AI imagined version
            "(8-hour",                // Extended versions that don't exist
            "(10-hour",
            "(12-hour",
            "(24-hour",
            "(ai version)",           // Obviously AI-generated
            "(generated)",
            "(hypothetical)",
            "(alternate universe)",
            "(what if",               // Covers "(What If)" and "(What If Version)"
            "(fan made)",
            "(fan-curated",           // Fan-curated editions
            "(unofficial)",
            "(bootleg version)",      // When not actually a bootleg
            "(director's cut)",       // Film term, not music
            "(extended universe)",    // Fictional term
            "(multiverse",            // Covers "(Multiverse)" and "(Multiverse Edition)"
            "(redux redux)",          // Double redux doesn't exist
            "(super deluxe ultra)",   // Over-the-top descriptions
            "& electric version)",    // Suspicious acoustic & electric combinations
        };

        // Patterns that might be legitimate but need additional validation
        // These require context to determine if they're real
        private static readonly string[] PossiblyLegitimatePatterns = new[]
        {
            "(live at",               // Many legitimate live albums exist
            "(remastered)",          // Common for older albums
            "(deluxe)",              // Often real
            "(special edition)",     // Often real
            "(expanded)",            // Often real for reissues
            "anniversary",           // Common for milestone reissues
            "(bonus",                // Bonus tracks are common
            "(acoustic)",            // Legitimate acoustic versions
            "(instrumental)",        // Legitimate instrumental versions
            "(remix)",               // Legitimate remixes
            "(radio edit)",          // Legitimate radio edits
        };

        // Combinations that are suspicious (unlikely to be real)
        private static readonly (string, string)[] SuspiciousCombinations = new[]
        {
            ("(live", "(remastered)"),        // Live + Remastered is unusual
            ("demo", "deluxe"),                // Demos aren't usually deluxe
            ("(acoustic)", "(instrumental)"),  // Can't be both
            ("(8-hour", "(radio edit)"),      // Contradictory
            ("(live", "(studio"),              // Contradictory - covers "(Live) (Studio Recording)"
            ("live)", "studio"),               // Another variation
        };

        // Year patterns that indicate AI confusion
        private static readonly Regex InvalidYearPattern = new Regex(
            @"\b(19[0-4]\d|20[3-9]\d|21[0-9]\d)\b",  // Years before 1950 or after 2030
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        
        // Future anniversary pattern (anniversary dates more than 3 years in the future)
        private static readonly Regex FutureAnniversaryPattern = new Regex(
            @"\((\d{4})[^)]*anniversary[^)]*\)",  // Captures year in anniversary parentheses
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        // Multiple parenthetical expressions (often a sign of AI over-description)
        private static readonly Regex ExcessiveParenthesesPattern = new Regex(
            @"\([^)]+\).*\([^)]+\).*\([^)]+\)",  // Three or more (...) expressions
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public RecommendationValidator(Logger? logger = null, string? customPatterns = null, bool strictMode = false)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _strictMode = strictMode;
            
            // Parse custom patterns if provided
            if (!string.IsNullOrWhiteSpace(customPatterns))
            {
                _customPatterns = customPatterns
                    .Split(',')
                    .Select(p => p.Trim().ToLowerInvariant())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToArray();
                
                _logger.Info($"Loaded {_customPatterns.Length} custom filter patterns");
            }
            else
            {
                _customPatterns = Array.Empty<string>();
            }
        }

        public bool ValidateRecommendation(Recommendation recommendation, bool allowArtistOnly = false)
        {
            try
            {
                // Step 1: Basic validation - must have artist, and album if not in artist-only mode
                if (string.IsNullOrWhiteSpace(recommendation.Artist))
                {
                    _logger.Debug($"Validation failed: Missing artist");
                    return false;
                }
                
                // In album mode, require album; in artist mode, album is optional
                if (!allowArtistOnly && string.IsNullOrWhiteSpace(recommendation.Album))
                {
                    _logger.Debug($"Validation failed: Missing album (album mode)");
                    return false;
                }

                // Step 2: Check for obviously fictional terms (only if album is provided)
                var albumLower = recommendation.Album?.ToLowerInvariant() ?? "";
                foreach (var pattern in DefinitelyFictionalPatterns)
                {
                    if (albumLower.Contains(pattern))
                    {
                        _logger.Debug($"Filtered AI hallucination: {recommendation.Artist} - {recommendation.Album} (matched '{pattern}')");
                        return false;
                    }
                }
                
                // Step 2b: Check custom patterns
                foreach (var pattern in _customPatterns)
                {
                    if (albumLower.Contains(pattern))
                    {
                        _logger.Debug($"Filtered by custom pattern: {recommendation.Artist} - {recommendation.Album} (matched '{pattern}')");
                        return false;
                    }
                }

                // Step 3: Check for suspicious combinations
                foreach (var (pattern1, pattern2) in SuspiciousCombinations)
                {
                    if (albumLower.Contains(pattern1) && albumLower.Contains(pattern2))
                    {
                        _logger.Debug($"Filtered suspicious combination: {recommendation.Artist} - {recommendation.Album} ('{pattern1}' + '{pattern2}')");
                        return false;
                    }
                }

                // Step 4: Check for invalid year patterns
                if (InvalidYearPattern.IsMatch(recommendation.Album))
                {
                    _logger.Debug($"Filtered invalid year: {recommendation.Artist} - {recommendation.Album}");
                    return false;
                }
                
                // Step 4b: Check for future anniversary dates (more than 3 years in future)
                var futureMatch = FutureAnniversaryPattern.Match(recommendation.Album);
                if (futureMatch.Success && int.TryParse(futureMatch.Groups[1].Value, out int anniversaryYear))
                {
                    var currentYear = DateTime.UtcNow.Year;
                    if (anniversaryYear > currentYear + 3)
                    {
                        _logger.Debug($"Filtered future anniversary: {recommendation.Artist} - {recommendation.Album} (year {anniversaryYear} is {anniversaryYear - currentYear} years in future)");
                        return false;
                    }
                }

                // Step 5: Check for excessive parenthetical expressions
                if (ExcessiveParenthesesPattern.IsMatch(recommendation.Album))
                {
                    _logger.Debug($"Filtered excessive descriptions: {recommendation.Artist} - {recommendation.Album}");
                    return false;
                }

                // Step 6: Additional heuristics for AI-generated content
                if (IsLikelyAIGenerated(recommendation))
                {
                    _logger.Debug($"Filtered likely AI-generated: {recommendation.Artist} - {recommendation.Album}");
                    return false;
                }

                // Step 7: Special handling for potentially legitimate patterns
                // If it contains a possibly legitimate pattern, apply stricter validation
                var containsPossiblyLegit = PossiblyLegitimatePatterns.Any(p => albumLower.Contains(p));
                if (containsPossiblyLegit)
                {
                    // In strict mode, be more aggressive about filtering
                    if (_strictMode)
                    {
                        // Filter out all possibly legitimate patterns unless they pass strict validation
                        return ValidatePossiblyLegitimateStrict(recommendation);
                    }
                    else
                    {
                        // Apply normal validation for these edge cases
                        return ValidatePossiblyLegitimate(recommendation);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Error validating recommendation: {recommendation.Artist} - {recommendation.Album}");
                // On error, err on the side of inclusion
                return true;
            }
        }

        /// <summary>
        /// Additional validation for albums that might be legitimate but need extra checking.
        /// </summary>
        private bool ValidatePossiblyLegitimate(Recommendation recommendation)
        {
            var album = recommendation.Album;
            var albumLower = album.ToLowerInvariant();

            // Live albums: Check if the venue seems realistic
            if (albumLower.Contains("live"))
            {
                // Check for obviously fake venues
                var fakeVenues = new[] { "the universe", "mars", "the moon", "imagination", "nowhere", "everywhere" };
                if (fakeVenues.Any(venue => albumLower.Contains(venue)))
                {
                    return false;
                }

                // Check for year in the future (anywhere in the album name)
                var currentYear = DateTime.Now.Year;
                var yearMatch = Regex.Match(album, @"\b(19\d{2}|20\d{2}|21\d{2})\b");
                if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year))
                {
                    if (year > currentYear + 1) // Allow for upcoming releases
                    {
                        return false;
                    }
                }
            }
            
            // Check for future years in any context (not just live albums)
            var futureYearMatch = Regex.Match(album, @"\b(20[3-9]\d|21\d{2})\b");
            if (futureYearMatch.Success && int.TryParse(futureYearMatch.Groups[1].Value, out var futureYear))
            {
                if (futureYear > DateTime.Now.Year + 1)
                {
                    return false;
                }
            }

            // Remastered: Check if it makes sense
            if (albumLower.Contains("remaster"))
            {
                // Multiple remasters are suspicious
                var remasterCount = Regex.Matches(albumLower, @"remaster").Count;
                if (remasterCount > 1)
                {
                    return false;
                }

                // Recent albums shouldn't be remastered
                if (recommendation.Year.HasValue && recommendation.Year.Value > DateTime.Now.Year - 5)
                {
                    return false; // Albums less than 5 years old rarely get remastered
                }
            }

            // Anniversary Edition Validation Algorithm
            // Validates if anniversary claims are mathematically plausible
            // Example: "Abbey Road 50th Anniversary" in 2019 (original: 1969) ✓
            // Example: "Thriller 173rd Anniversary" in 2024 (original: 1982) ✗
            if (Regex.IsMatch(albumLower, @"\d+(st|nd|rd|th) anniversary"))
            {
                var match = Regex.Match(albumLower, @"(\d+)(st|nd|rd|th) anniversary");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var years))
                {
                    // Two-tier validation: Common vs Uncommon anniversaries
                    // Common (10,20,25,30,40,50,etc): More lenient (±5 years)
                    // Uncommon (37,83,etc): Strict validation (±2 years)
                    var commonAnniversaries = new[] { 10, 20, 25, 30, 40, 50, 60, 70, 75, 100 };
                    if (commonAnniversaries.Contains(years))
                    {
                        // Common Anniversary Math Check
                        // Formula: original_year + anniversary_years ≈ current_year
                        // Tolerance: ±5 years (accounts for release delays)
                        if (recommendation.Year.HasValue)
                        {
                            var expectedYear = recommendation.Year.Value + years;
                            var currentYear = DateTime.Now.Year;
                            // Example: 1969 + 50 = 2019, current=2024, diff=5 ✓
                            if (Math.Abs(expectedYear - currentYear) > 5)
                            {
                                return false;
                            }
                        }
                        // No year? Trust common anniversaries (likely legitimate)
                        return true;
                    }
                    
                    // Uncommon Anniversary Strict Validation
                    // AI often generates odd numbers like 37th, 83rd anniversary
                    if (recommendation.Year.HasValue)
                    {
                        var expectedYear = recommendation.Year.Value + years;
                        var currentYear = DateTime.Now.Year;
                        // Strict: Must be within 2 years (current releases only)
                        if (Math.Abs(expectedYear - currentYear) > 2)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // No year + unusual number = likely AI hallucination
                        // Only accept multiples of 5 or 1st anniversary
                        if (years % 5 != 0 && years != 1)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Stricter validation for albums in strict mode.
        /// </summary>
        private bool ValidatePossiblyLegitimateStrict(Recommendation recommendation)
        {
            var album = recommendation.Album;
            var albumLower = album.ToLowerInvariant();

            // In strict mode, reject most parenthetical additions
            if (albumLower.Contains("(") && albumLower.Contains(")"))
            {
                // Only allow very specific patterns that are almost certainly real
                var allowedStrictPatterns = new[]
                {
                    "(original soundtrack)",
                    "(ost)",
                    "(ep)",
                    "(single)",
                    "(album)"
                };

                var hasAllowedPattern = allowedStrictPatterns.Any(p => albumLower.Contains(p));
                if (!hasAllowedPattern)
                {
                    _logger.Debug($"Strict mode: Filtered parenthetical album: {recommendation.Artist} - {recommendation.Album}");
                    return false;
                }
            }

            // Apply all normal validation rules as well
            return ValidatePossiblyLegitimate(recommendation);
        }

        /// <summary>
        /// Heuristic checks for AI-generated content patterns.
        /// </summary>
        private bool IsLikelyAIGenerated(Recommendation recommendation)
        {
            var album = recommendation.Album;
            var artist = recommendation.Artist;

            // AI Hallucination Pattern #1: Recursive Artist Names
            // AI models sometimes generate recursive patterns like:
            // "Beatles Play The Beatles Playing The Beatles Greatest Hits"
            // Algorithm: Count artist name occurrences in album title
            // If appears 2+ times with "play" verb, it's likely hallucinated
            var albumLowerForRecursive = album.ToLowerInvariant();
            var artistLower = artist.ToLowerInvariant();
            
            // String search with incremental index to count all occurrences
            int artistCount = 0;
            int index = 0;
            while ((index = albumLowerForRecursive.IndexOf(artistLower, index)) != -1)
            {
                artistCount++;
                index += artistLower.Length;
            }
            
            // Heuristic: 2+ artist mentions + "play" verb = AI recursion pattern
            if (artistCount >= 2 && (albumLowerForRecursive.Contains("play") || albumLowerForRecursive.Contains("playing")))
            {
                // e.g., "Beatles Play The Beatles Playing The Beatles"
                return true;
            }
            
            // AI Hallucination Pattern #2: Doubled Descriptors
            // AI sometimes duplicates edition descriptors:
            // "Dark Side of the Moon Remastered Remastered Edition"
            // Algorithm: Search for duplicate occurrences of common descriptors
            var doublePatterns = new[] { "remastered", "remix", "edition", "version", "mix" };
            var albumLowerForDouble = album.ToLowerInvariant();
            foreach (var pattern in doublePatterns)
            {
                // Two-pass search: find first occurrence, then search for second
                var firstIndex = albumLowerForDouble.IndexOf(pattern);
                if (firstIndex >= 0)
                {
                    var secondIndex = albumLowerForDouble.IndexOf(pattern, firstIndex + pattern.Length);
                    if (secondIndex >= 0)
                    {
                        return true; // Doubled descriptor = AI hallucination
                    }
                }
            }

            // Check for impossibly long titles (AI sometimes generates verbose titles)
            if (album.Length > 100)
            {
                return true;
            }

            // Check for certain philosophical or meta descriptions AI tends to use
            var aiPhilosophicalTerms = new[]
            {
                "journey through", "exploration of", "meditation on", 
                "reimagining the", "deconstructed", "reconstructed",
                "essential essence", "ultimate collection of collections"
            };

            var albumLower = album.ToLowerInvariant();
            if (aiPhilosophicalTerms.Any(term => albumLower.Contains(term)))
            {
                return true;
            }

            // AI Hallucination Pattern #5: Suspicious Confidence Levels
            // AI often assigns unrealistically high confidence to hallucinated content
            // Real obscure/modified albums should have lower confidence
            // Heuristic: >95% confidence + parenthetical = likely fake
            if (recommendation.Confidence > 0.95 && 
                albumLower.Contains("(") && 
                albumLower.Contains(")"))
            {
                // Example: "Abbey Road (Super Deluxe Remastered)" at 99% confidence
                return true;
            }

            return false;
        }

        public ValidationResult ValidateBatch(List<Recommendation> recommendations, bool allowArtistOnly = false)
        {
            var validRecommendations = new List<Recommendation>();
            var filteredRecommendations = new List<Recommendation>();
            var filterReasons = new Dictionary<string, int>();
            var filterDetails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rec in recommendations)
            {
                if (ValidateRecommendation(rec, allowArtistOnly))
                {
                    validRecommendations.Add(rec);
                }
                else
                {
                    filteredRecommendations.Add(rec);
                    
                    // Track why it was filtered for metrics
                    var reason = DetermineFilterReason(rec);
                    if (!filterReasons.ContainsKey(reason))
                    {
                        filterReasons[reason] = 0;
                    }
                    filterReasons[reason]++;
                    var key = string.IsNullOrWhiteSpace(rec.Album) ? rec.Artist : $"{rec.Artist} - {rec.Album}";
                    if (!filterDetails.ContainsKey(key))
                    {
                        filterDetails[key] = reason;
                    }
                }
            }

            var result = new ValidationResult
            {
                TotalCount = recommendations.Count,
                ValidCount = validRecommendations.Count,
                FilteredCount = filteredRecommendations.Count,
                ValidRecommendations = validRecommendations,
                FilteredRecommendations = filteredRecommendations,
                FilterReasons = filterReasons,
                FilterDetails = filterDetails
            };

            _logger.Info($"Validation complete: {result.ValidCount}/{result.TotalCount} passed ({result.PassRate:F1}%)");
            if (result.FilterReasons.Any())
            {
                _logger.Debug($"Filter reasons: {string.Join(", ", result.FilterReasons.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
            }

            return result;
        }

        private string DetermineFilterReason(Recommendation rec)
        {
            var albumLower = rec.Album?.ToLowerInvariant() ?? "";
            
            if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                return "missing_data";
            
            foreach (var pattern in DefinitelyFictionalPatterns)
            {
                if (albumLower.Contains(pattern))
                    return $"fictional_pattern:{pattern}";
            }
            
            if (ExcessiveParenthesesPattern.IsMatch(rec.Album))
                return "excessive_descriptions";
            
            if (InvalidYearPattern.IsMatch(rec.Album))
                return "invalid_year";
            
            if (IsLikelyAIGenerated(rec))
                return "ai_generated_pattern";
            
            return "unknown";
        }
    }
}
