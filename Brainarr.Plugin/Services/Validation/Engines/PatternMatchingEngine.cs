using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Patterns;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Engines
{
    /// <summary>
    /// Engine for detecting hallucination patterns in AI-generated recommendations
    /// </summary>
    public interface IPatternMatchingEngine
    {
        Task<PatternMatchResults> AnalyzePatternsAsync(Recommendation recommendation);
    }

    public class PatternMatchingEngine : IPatternMatchingEngine
    {
        private readonly Logger _logger;
        private readonly IHallucinationPatternRepository _patternRepository;

        public PatternMatchingEngine(Logger logger, IHallucinationPatternRepository patternRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _patternRepository = patternRepository ?? throw new ArgumentNullException(nameof(patternRepository));
        }

        public async Task<PatternMatchResults> AnalyzePatternsAsync(Recommendation recommendation)
        {
            var results = new PatternMatchResults();
            
            // Check for non-existent artist patterns
            await CheckArtistPatterns(recommendation, results);
            
            // Check for non-existent album patterns
            await CheckAlbumPatterns(recommendation, results);
            
            // Check for temporal anomalies
            await CheckTemporalPatterns(recommendation, results);
            
            // Check for repetitive elements
            await CheckRepetitivePatterns(recommendation, results);
            
            // Check for suspicious combinations
            await CheckCombinationPatterns(recommendation, results);

            results.TotalPatternsChecked = results.MatchedPatterns.Count + results.PassedPatterns.Count;
            results.MatchScore = CalculateMatchScore(results);

            return results;
        }

        private async Task CheckArtistPatterns(Recommendation recommendation, PatternMatchResults results)
        {
            var artistPatterns = await _patternRepository.GetArtistPatternsAsync();
            
            foreach (var pattern in artistPatterns)
            {
                if (pattern.Matches(recommendation.Artist))
                {
                    results.MatchedPatterns.Add(new PatternMatch
                    {
                        PatternType = "ArtistHallucination",
                        Pattern = pattern.Expression,
                        MatchedValue = recommendation.Artist,
                        Severity = pattern.Severity,
                        Description = pattern.Description
                    });
                }
                else
                {
                    results.PassedPatterns.Add(pattern.Name);
                }
            }
        }

        private async Task CheckAlbumPatterns(Recommendation recommendation, PatternMatchResults results)
        {
            var albumPatterns = await _patternRepository.GetAlbumPatternsAsync();
            
            foreach (var pattern in albumPatterns)
            {
                if (pattern.Matches(recommendation.Album))
                {
                    results.MatchedPatterns.Add(new PatternMatch
                    {
                        PatternType = "AlbumHallucination",
                        Pattern = pattern.Expression,
                        MatchedValue = recommendation.Album,
                        Severity = pattern.Severity,
                        Description = pattern.Description
                    });
                }
                else
                {
                    results.PassedPatterns.Add(pattern.Name);
                }
            }
        }

        private async Task CheckTemporalPatterns(Recommendation recommendation, PatternMatchResults results)
        {
            if (!recommendation.Year.HasValue)
                return;

            var currentYear = DateTime.UtcNow.Year;
            
            // Future releases (more than 1 year ahead)
            if (recommendation.Year > currentYear + 1)
            {
                results.MatchedPatterns.Add(new PatternMatch
                {
                    PatternType = "TemporalAnomaly",
                    Pattern = "FutureRelease",
                    MatchedValue = recommendation.Year.ToString(),
                    Severity = PatternSeverity.High,
                    Description = $"Release year {recommendation.Year} is too far in the future"
                });
            }
            
            // Impossible dates (before recorded music)
            if (recommendation.Year < 1877) // Edison's phonograph
            {
                results.MatchedPatterns.Add(new PatternMatch
                {
                    PatternType = "TemporalAnomaly",
                    Pattern = "ImpossibleDate",
                    MatchedValue = recommendation.Year.ToString(),
                    Severity = PatternSeverity.Critical,
                    Description = $"Release year {recommendation.Year} predates recorded music"
                });
            }
        }

        private async Task CheckRepetitivePatterns(Recommendation recommendation, PatternMatchResults results)
        {
            // Check for repetitive words in artist/album names
            var artistWords = recommendation.Artist?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var albumWords = recommendation.Album?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            if (artistWords.Length > 1 && artistWords.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            {
                results.MatchedPatterns.Add(new PatternMatch
                {
                    PatternType = "RepetitivePattern",
                    Pattern = "RepeatedArtistWords",
                    MatchedValue = recommendation.Artist,
                    Severity = PatternSeverity.Medium,
                    Description = "Artist name contains only repeated words"
                });
            }
            
            if (albumWords.Length > 1 && albumWords.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            {
                results.MatchedPatterns.Add(new PatternMatch
                {
                    PatternType = "RepetitivePattern",
                    Pattern = "RepeatedAlbumWords",
                    MatchedValue = recommendation.Album,
                    Severity = PatternSeverity.Medium,
                    Description = "Album name contains only repeated words"
                });
            }
        }

        private async Task CheckCombinationPatterns(Recommendation recommendation, PatternMatchResults results)
        {
            // Check for suspicious artist/album combinations
            if (string.Equals(recommendation.Artist, recommendation.Album, StringComparison.OrdinalIgnoreCase))
            {
                results.MatchedPatterns.Add(new PatternMatch
                {
                    PatternType = "SuspiciousCombination",
                    Pattern = "IdenticalArtistAlbum",
                    MatchedValue = $"{recommendation.Artist} / {recommendation.Album}",
                    Severity = PatternSeverity.High,
                    Description = "Artist and album names are identical"
                });
            }
            
            // Check for placeholder text
            var placeholders = new[] { "test", "example", "sample", "demo", "placeholder", "unknown" };
            var lowerArtist = recommendation.Artist?.ToLowerInvariant() ?? "";
            var lowerAlbum = recommendation.Album?.ToLowerInvariant() ?? "";
            
            if (placeholders.Any(p => lowerArtist.Contains(p) || lowerAlbum.Contains(p)))
            {
                results.MatchedPatterns.Add(new PatternMatch
                {
                    PatternType = "SuspiciousCombination",
                    Pattern = "PlaceholderText",
                    MatchedValue = $"{recommendation.Artist} / {recommendation.Album}",
                    Severity = PatternSeverity.High,
                    Description = "Contains placeholder or test text"
                });
            }
        }

        private double CalculateMatchScore(PatternMatchResults results)
        {
            if (results.TotalPatternsChecked == 0)
                return 1.0;

            var severityWeights = new Dictionary<PatternSeverity, double>
            {
                { PatternSeverity.Low, 0.1 },
                { PatternSeverity.Medium, 0.3 },
                { PatternSeverity.High, 0.6 },
                { PatternSeverity.Critical, 1.0 }
            };

            var totalWeight = results.MatchedPatterns
                .Sum(p => severityWeights.GetValueOrDefault(p.Severity, 0.1));

            return Math.Max(0, 1.0 - (totalWeight / results.TotalPatternsChecked));
        }
    }

    public class PatternMatchResults
    {
        public List<PatternMatch> MatchedPatterns { get; set; } = new List<PatternMatch>();
        public List<string> PassedPatterns { get; set; } = new List<string>();
        public int TotalPatternsChecked { get; set; }
        public double MatchScore { get; set; }
    }

    public class PatternMatch
    {
        public string PatternType { get; set; }
        public string Pattern { get; set; }
        public string MatchedValue { get; set; }
        public PatternSeverity Severity { get; set; }
        public string Description { get; set; }
    }

    public enum PatternSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}