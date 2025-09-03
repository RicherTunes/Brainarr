using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Detectors
{
    /// <summary>
    /// Validates release dates for impossibilities and temporal anomalies
    /// </summary>
    public class ReleaseDateValidator : ISpecificHallucinationDetector
    {
        private readonly Logger _logger;
        private readonly Dictionary<string, (int MinYear, int MaxYear)> _genreTimelines;

        public HallucinationPatternType PatternType => HallucinationPatternType.ImpossibleReleaseDate;
        public int Priority => 90;
        public bool IsEnabled => true;

        public ReleaseDateValidator(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _genreTimelines = InitializeGenreTimelines();
        }

        public async Task<HallucinationPattern> DetectAsync(Recommendation recommendation)
        {
            if (recommendation == null || !recommendation.Year.HasValue)
            {
                return new HallucinationPattern
                {
                    PatternType = PatternType,
                    Description = "No release date provided",
                    Confidence = 0.0,
                    IsConfirmedHallucination = false
                };
            }

            var year = recommendation.Year.Value;
            var currentYear = DateTime.UtcNow.Year;
            var confidence = 0.0;
            var evidence = new List<string>();

            // Check for impossible dates
            if (year < 1877) // Before recorded music
            {
                confidence = 1.0;
                evidence.Add($"Year {year} is before recorded music history (1877)");
            }
            else if (year > currentYear + 2) // More than 2 years in future
            {
                confidence = 0.95;
                evidence.Add($"Year {year} is too far in the future");
            }
            else if (year > currentYear)
            {
                confidence = 0.3; // Near future releases are possible
                evidence.Add($"Year {year} is in the future");
            }

            // Check genre-specific timelines
            if (!string.IsNullOrWhiteSpace(recommendation.Genre))
            {
                var genreConfidence = ValidateGenreTimeline(recommendation.Genre, year);
                if (genreConfidence > 0)
                {
                    confidence = Math.Max(confidence, genreConfidence);
                    evidence.Add($"Year {year} is unlikely for genre '{recommendation.Genre}'");
                }
            }

            // Check artist-album temporal consistency
            if (!string.IsNullOrWhiteSpace(recommendation.Artist))
            {
                var artistConfidence = ValidateArtistTimeline(recommendation.Artist, year);
                if (artistConfidence > 0)
                {
                    confidence = Math.Max(confidence, artistConfidence);
                    evidence.Add($"Year {year} conflicts with artist '{recommendation.Artist}' timeline");
                }
            }

            // Check for suspicious patterns
            if (IsSuspiciousYear(year))
            {
                confidence = Math.Max(confidence, 0.4);
                evidence.Add($"Year {year} follows suspicious pattern");
            }

            await Task.CompletedTask; // Async for consistency

            return new HallucinationPattern
            {
                PatternType = PatternType,
                Description = evidence.Count > 0 ? string.Join("; ", evidence) : "Release date appears valid",
                Confidence = confidence,
                Evidence = year.ToString(),
                IsConfirmedHallucination = confidence > 0.9
            };
        }

        private Dictionary<string, (int MinYear, int MaxYear)> InitializeGenreTimelines()
        {
            return new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Classical", (1600, DateTime.UtcNow.Year) },
                { "Jazz", (1910, DateTime.UtcNow.Year) },
                { "Blues", (1900, DateTime.UtcNow.Year) },
                { "Country", (1920, DateTime.UtcNow.Year) },
                { "Rock", (1950, DateTime.UtcNow.Year) },
                { "Rock and Roll", (1950, DateTime.UtcNow.Year) },
                { "Electronic", (1970, DateTime.UtcNow.Year) },
                { "Disco", (1970, 1985) },
                { "Hip Hop", (1973, DateTime.UtcNow.Year) },
                { "Rap", (1973, DateTime.UtcNow.Year) },
                { "House", (1980, DateTime.UtcNow.Year) },
                { "Techno", (1985, DateTime.UtcNow.Year) },
                { "Grunge", (1985, 2000) },
                { "Dubstep", (1998, DateTime.UtcNow.Year) },
                { "Trap", (2003, DateTime.UtcNow.Year) },
                { "Vaporwave", (2010, DateTime.UtcNow.Year) },
                { "Lo-fi", (2013, DateTime.UtcNow.Year) }
            };
        }

        private double ValidateGenreTimeline(string genre, int year)
        {
            foreach (var kvp in _genreTimelines)
            {
                if (genre.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var (minYear, maxYear) = kvp.Value;
                    if (year < minYear)
                        return 0.8; // High confidence it's wrong
                    if (year > maxYear)
                        return 0.6; // Medium confidence it's wrong
                    break;
                }
            }
            return 0.0;
        }

        private double ValidateArtistTimeline(string artist, int year)
        {
            // Check for anachronistic artist-year combinations
            var knownArtistPeriods = new Dictionary<string, (int Start, int End)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Beatles", (1960, 1970) },
                { "Led Zeppelin", (1968, 1980) },
                { "Nirvana", (1987, 1994) },
                { "Elvis", (1954, 1977) },
                { "Jimi Hendrix", (1963, 1970) },
                { "Queen", (1970, 1995) }, // Until Freddie's death
                { "The Doors", (1965, 1973) },
                { "Pink Floyd", (1965, 2014) }
            };

            foreach (var kvp in knownArtistPeriods)
            {
                if (artist.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var (start, end) = kvp.Value;
                    if (year < start - 5 || year > end + 5) // Allow some margin
                        return 0.7;
                    break;
                }
            }

            return 0.0;
        }

        private bool IsSuspiciousYear(int year)
        {
            // Check for commonly hallucinated years
            var suspiciousYears = new[] { 1111, 2222, 1234, 9999, 1000, 2000 };
            if (suspiciousYears.Contains(year))
                return true;

            // Check for repeating digits
            var yearStr = year.ToString();
            if (yearStr.Length >= 4 && yearStr.All(c => c == yearStr[0]))
                return true;

            return false;
        }
    }
}