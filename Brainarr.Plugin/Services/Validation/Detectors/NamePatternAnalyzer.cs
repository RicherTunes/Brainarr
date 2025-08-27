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
    /// Analyzes naming patterns for anomalies that indicate AI hallucination
    /// </summary>
    public class NamePatternAnalyzer : ISpecificHallucinationDetector
    {
        private readonly Logger _logger;
        private readonly Regex _excessivePunctuationRegex;
        private readonly Regex _mixedCaseAnomalyRegex;
        private readonly Regex _repeatingPatternRegex;
        private readonly HashSet<string> _commonWords;

        public HallucinationPatternType PatternType => HallucinationPatternType.NamePatternAnomaly;
        public int Priority => 70;
        public bool IsEnabled => true;

        public NamePatternAnalyzer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _excessivePunctuationRegex = new Regex(@"[!@#$%^&*()_+=\[\]{}|\\;:'"",.<>?/]{3,}", RegexOptions.Compiled);
            _mixedCaseAnomalyRegex = new Regex(@"([a-z][A-Z]){3,}|([A-Z][a-z]){5,}", RegexOptions.Compiled);
            _repeatingPatternRegex = new Regex(@"(.{2,})\1{2,}", RegexOptions.Compiled);
            
            _commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "and", "of", "in", "on", "at", "to", "for", "with", "by",
                "vol", "volume", "part", "chapter", "disc", "disk", "side", "track"
            };
        }

        public async Task<HallucinationPattern> DetectAsync(Recommendation recommendation)
        {
            if (recommendation == null)
            {
                return new HallucinationPattern
                {
                    PatternType = PatternType,
                    Description = "No recommendation provided",
                    Confidence = 0.0,
                    IsConfirmedHallucination = false
                };
            }

            var confidence = 0.0;
            var evidence = new List<string>();

            // Analyze artist name patterns
            if (!string.IsNullOrWhiteSpace(recommendation.Artist))
            {
                var artistConfidence = AnalyzeNamePattern(recommendation.Artist, "Artist");
                confidence = Math.Max(confidence, artistConfidence.confidence);
                evidence.AddRange(artistConfidence.issues);
            }

            // Analyze album name patterns
            if (!string.IsNullOrWhiteSpace(recommendation.Album))
            {
                var albumConfidence = AnalyzeNamePattern(recommendation.Album, "Album");
                confidence = Math.Max(confidence, albumConfidence.confidence);
                evidence.AddRange(albumConfidence.issues);
            }

            // Check for matching artist and album names (common hallucination)
            if (!string.IsNullOrWhiteSpace(recommendation.Artist) && 
                !string.IsNullOrWhiteSpace(recommendation.Album))
            {
                if (recommendation.Artist.Equals(recommendation.Album, StringComparison.OrdinalIgnoreCase))
                {
                    confidence = Math.Max(confidence, 0.7);
                    evidence.Add("Artist and album names are identical");
                }
                else if (LevenshteinDistance(recommendation.Artist, recommendation.Album) < 3)
                {
                    confidence = Math.Max(confidence, 0.5);
                    evidence.Add("Artist and album names are suspiciously similar");
                }
            }

            await Task.CompletedTask; // Async for consistency

            return new HallucinationPattern
            {
                PatternType = PatternType,
                Description = evidence.Count > 0 ? string.Join("; ", evidence) : "No naming anomalies detected",
                Confidence = confidence,
                Evidence = $"{recommendation.Artist} - {recommendation.Album}",
                IsConfirmedHallucination = confidence > 0.8
            };
        }

        private (double confidence, List<string> issues) AnalyzeNamePattern(string name, string fieldType)
        {
            var confidence = 0.0;
            var issues = new List<string>();

            // Check for excessive punctuation
            if (_excessivePunctuationRegex.IsMatch(name))
            {
                confidence += 0.6;
                issues.Add($"{fieldType} contains excessive punctuation");
            }

            // Check for unusual mixed case patterns
            if (_mixedCaseAnomalyRegex.IsMatch(name))
            {
                confidence += 0.4;
                issues.Add($"{fieldType} has unusual capitalization");
            }

            // Check for repeating patterns
            if (_repeatingPatternRegex.IsMatch(name))
            {
                confidence += 0.5;
                issues.Add($"{fieldType} contains repeating patterns");
            }

            // Check for all uppercase or all lowercase (except short names)
            if (name.Length > 3)
            {
                if (name == name.ToUpper())
                {
                    confidence += 0.3;
                    issues.Add($"{fieldType} is all uppercase");
                }
                else if (name == name.ToLower())
                {
                    confidence += 0.3;
                    issues.Add($"{fieldType} is all lowercase");
                }
            }

            // Check for nonsensical character combinations
            if (HasNonsensicalPatterns(name))
            {
                confidence += 0.7;
                issues.Add($"{fieldType} contains nonsensical patterns");
            }

            // Check for excessive length
            if (name.Length > 100)
            {
                confidence += 0.5;
                issues.Add($"{fieldType} is unusually long ({name.Length} chars)");
            }
            else if (name.Length < 2)
            {
                confidence += 0.6;
                issues.Add($"{fieldType} is too short");
            }

            // Check for too many common words (filler content)
            var words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var commonWordCount = words.Count(w => _commonWords.Contains(w));
            if (words.Length > 0 && commonWordCount > words.Length * 0.6)
            {
                confidence += 0.4;
                issues.Add($"{fieldType} contains too many filler words");
            }

            return (Math.Min(confidence, 1.0), issues);
        }

        private bool HasNonsensicalPatterns(string name)
        {
            // Check for impossible consonant clusters
            var impossibleClusters = new[] { "bvgk", "qxz", "jqxz", "vkxz", "wqxz" };
            var lowerName = name.ToLower();
            
            if (impossibleClusters.Any(cluster => lowerName.Contains(cluster)))
                return true;

            // Check for too many consonants in a row
            if (Regex.IsMatch(lowerName, @"[bcdfghjklmnpqrstvwxyz]{6,}"))
                return true;

            // Check for too many vowels in a row (except legitimate cases like "queue")
            if (Regex.IsMatch(lowerName, @"[aeiou]{5,}"))
                return true;

            // Check for random alphanumeric sequences
            if (Regex.IsMatch(name, @"[a-zA-Z0-9]{20,}") && !name.Contains(" "))
                return true;

            return false;
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                d[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                d[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s1.Length, s2.Length];
        }
    }
}