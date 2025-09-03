using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Detectors
{
    /// <summary>
    /// Detects hallucinated artists that don't exist or have suspicious naming patterns
    /// </summary>
    public class ArtistExistenceDetector : ISpecificHallucinationDetector
    {
        private readonly Logger _logger;
        private readonly HashSet<string> _suspiciousPatterns;
        private readonly Regex _numberSequenceRegex;
        private readonly Regex _randomCharRegex;
        private readonly Regex _placeholderRegex;

        public HallucinationPatternType PatternType => HallucinationPatternType.NonExistentArtist;
        public int Priority => 100; // High priority - check artist first
        public bool IsEnabled => true;

        public ArtistExistenceDetector(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _suspiciousPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Unknown Artist", "Various Artists", "Test Artist", "Sample Artist",
                "Demo Artist", "Example Band", "Placeholder", "TBA", "TBD",
                "N/A", "None", "Null", "Default Artist", "Artist Name",
                "Band Name", "Music Group", "The Band", "The Group"
            };

            _numberSequenceRegex = new Regex(@"\d{4,}", RegexOptions.Compiled);
            _randomCharRegex = new Regex(@"^[A-Z]{4,}$|^[a-z]{20,}$", RegexOptions.Compiled);
            _placeholderRegex = new Regex(@"\[.*\]|\{.*\}|<.*>", RegexOptions.Compiled);
        }

        public async Task<HallucinationPattern> DetectAsync(Recommendation recommendation)
        {
            if (recommendation == null || string.IsNullOrWhiteSpace(recommendation.Artist))
            {
                return new HallucinationPattern
                {
                    PatternType = PatternType,
                    Description = "Missing artist name",
                    Confidence = 1.0,
                    IsConfirmedHallucination = true
                };
            }

            var confidence = 0.0;
            var evidence = new List<string>();

            // Check for suspicious patterns
            if (_suspiciousPatterns.Contains(recommendation.Artist))
            {
                confidence += 0.9;
                evidence.Add($"Suspicious artist name: '{recommendation.Artist}'");
            }

            // Check for placeholder patterns
            if (_placeholderRegex.IsMatch(recommendation.Artist))
            {
                confidence += 0.8;
                evidence.Add("Contains placeholder syntax");
            }

            // Check for random character sequences
            if (_randomCharRegex.IsMatch(recommendation.Artist))
            {
                confidence += 0.7;
                evidence.Add("Appears to be random characters");
            }

            // Check for excessive numbers
            if (_numberSequenceRegex.IsMatch(recommendation.Artist))
            {
                confidence += 0.5;
                evidence.Add("Contains suspicious number sequences");
            }

            // Check for AI-generated patterns
            if (ContainsAIGeneratedPatterns(recommendation.Artist))
            {
                confidence += 0.6;
                evidence.Add("Contains AI-generated naming patterns");
            }

            // Check artist name length
            if (recommendation.Artist.Length < 2 || recommendation.Artist.Length > 100)
            {
                confidence += 0.4;
                evidence.Add($"Unusual artist name length: {recommendation.Artist.Length}");
            }

            // Check for repeated words
            if (HasExcessiveRepetition(recommendation.Artist))
            {
                confidence += 0.5;
                evidence.Add("Contains excessive repetition");
            }

            // Normalize confidence to 0-1 range
            confidence = Math.Min(1.0, confidence);

            await Task.CompletedTask; // Async for consistency with interface

            return new HallucinationPattern
            {
                PatternType = PatternType,
                Description = evidence.Count > 0 ? string.Join("; ", evidence) : "No hallucination detected",
                Confidence = confidence,
                Evidence = recommendation.Artist,
                IsConfirmedHallucination = confidence > 0.8
            };
        }

        private bool ContainsAIGeneratedPatterns(string artist)
        {
            // Common patterns in AI-generated fake names
            var patterns = new[]
            {
                @"^The [A-Z][a-z]+ [A-Z][a-z]+$", // "The Adjective Noun" pattern
                @"^[A-Z][a-z]+ & The [A-Z][a-z]+s$", // "Name & The Things" pattern
                @"^DJ [A-Z]{2,4}$", // "DJ XXX" pattern
                @"^\d+[A-Z][a-z]+$", // Number prefix pattern
            };

            return patterns.Any(pattern => Regex.IsMatch(artist, pattern));
        }

        private bool HasExcessiveRepetition(string artist)
        {
            var words = artist.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return false;

            var uniqueWords = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
            return uniqueWords.Count < words.Length / 2; // More than half are duplicates
        }
    }
}