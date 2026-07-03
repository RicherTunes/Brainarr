using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Simplified recommendation validator for immediate integration.
    /// Focuses on the most critical validation checks to eliminate AI hallucinations.
    /// </summary>
    public class SimpleRecommendationValidator : IRecommendationValidator
    {
        private readonly Logger _logger;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;

        private const int CURRENT_YEAR = 2024;
        private const int MIN_REASONABLE_YEAR = 1900;

        public SimpleRecommendationValidator(Logger logger, IArtistService artistService, IAlbumService albumService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
        }

        public async Task<ValidationResult> ValidateRecommendationAsync(Recommendation recommendation)
        {
            var result = new ValidationResult
            {
                Recommendation = recommendation,
                Score = 1.0
            };

            // Basic format validation
            ValidateBasicFormat(recommendation, result);

            // Release date validation
            ValidateReleaseDate(recommendation, result);

            // Simple hallucination detection
            await DetectSimpleHallucinations(recommendation, result).ConfigureAwait(false);

            // Duplicate detection
            await CheckForDuplicates(recommendation, result).ConfigureAwait(false);

            // Determine final validity
            result.IsValid = result.Score >= 0.7 &&
                           !result.Findings.Any(f => f.Severity == ValidationSeverity.Critical);

            return result;
        }

        public async Task<List<ValidationResult>> ValidateRecommendationsAsync(List<Recommendation> recommendations)
        {
            var results = new List<ValidationResult>();

            foreach (var recommendation in recommendations)
            {
                var result = await ValidateRecommendationAsync(recommendation).ConfigureAwait(false);
                results.Add(result);
            }

            return results;
        }

        public async Task<List<Recommendation>> FilterValidRecommendationsAsync(List<Recommendation> recommendations, double minScore = 0.7)
        {
            var validationResults = await ValidateRecommendationsAsync(recommendations).ConfigureAwait(false);

            return validationResults
                .Where(r => r.IsValid && r.Score >= minScore)
                .Select(r => r.Recommendation)
                .ToList();
        }

        public async Task<bool> IsAlreadyInLibraryAsync(Recommendation recommendation)
        {
            return await CheckForDuplicatesInternal(recommendation).ConfigureAwait(false);
        }

        public async Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation)
        {
            var result = new HallucinationDetectionResult();

            // Simple hallucination patterns
            var confidence = 0.0;
            var patterns = new List<HallucinationPattern>();

            // Check for obvious AI patterns
            if (HasAIHallucinationPatterns(recommendation.Album))
            {
                confidence += 0.8;
                patterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.NonExistentAlbum,
                    Description = "Album contains AI hallucination indicators",
                    Confidence = 0.8
                });
            }

            // Check for impossible dates
            if (recommendation.Year.HasValue &&
                (recommendation.Year.Value < MIN_REASONABLE_YEAR || recommendation.Year.Value > CURRENT_YEAR + 3))
            {
                confidence += 0.9;
                patterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.ImpossibleReleaseDate,
                    Description = "Impossible release date",
                    Confidence = 0.9
                });
            }

            result.HallucinationConfidence = Math.Min(1.0, confidence);
            result.DetectedPatterns = patterns;

            return result;
        }

        private void ValidateBasicFormat(Recommendation recommendation, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(recommendation.Artist))
            {
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.FormatValidation,
                    Severity = ValidationSeverity.Critical,
                    Message = "Artist name is empty",
                    ScoreImpact = -1.0
                });
                result.Score = 0.0;
            }

            if (string.IsNullOrWhiteSpace(recommendation.Album))
            {
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.FormatValidation,
                    Severity = ValidationSeverity.Critical,
                    Message = "Album name is empty",
                    ScoreImpact = -1.0
                });
                result.Score = 0.0;
            }
        }

        private void ValidateReleaseDate(Recommendation recommendation, ValidationResult result)
        {
            if (recommendation.Year.HasValue)
            {
                var year = recommendation.Year.Value;

                if (year < MIN_REASONABLE_YEAR || year > CURRENT_YEAR + 3)
                {
                    result.Findings.Add(new ValidationFinding
                    {
                        CheckType = ValidationCheckType.ReleaseDateValidation,
                        Severity = ValidationSeverity.Critical,
                        Message = $"Impossible release year: {year}",
                        ScoreImpact = -0.8
                    });
                    result.Score = Math.Max(0.0, result.Score - 0.8);
                }
            }
        }

        private async Task DetectSimpleHallucinations(Recommendation recommendation, ValidationResult result)
        {
            var hallucinationResult = await DetectHallucinationAsync(recommendation).ConfigureAwait(false);

            if (hallucinationResult.IsLikelyHallucination)
            {
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.HallucinationDetection,
                    Severity = ValidationSeverity.Error,
                    Message = "Likely AI hallucination detected",
                    ScoreImpact = -0.6
                });
                result.Score = Math.Max(0.0, result.Score - 0.6);
            }
        }

        private async Task CheckForDuplicates(Recommendation recommendation, ValidationResult result)
        {
            var isDuplicate = await CheckForDuplicatesInternal(recommendation).ConfigureAwait(false);

            if (isDuplicate)
            {
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.DuplicateDetection,
                    Severity = ValidationSeverity.Critical,
                    Message = "Album already exists in library",
                    ScoreImpact = -1.0
                });
                result.Score = 0.0;
            }
        }

        private async Task<bool> CheckForDuplicatesInternal(Recommendation recommendation)
        {
            try
            {
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                // Group albums by ArtistMetadataId (a plain int column) ONCE instead of re-scanning all
                // albums per matching artist via Album.ArtistId. Album.ArtistId dereferences a
                // LazyLoaded<Artist> that GetAllAlbums() leaves unloaded, so the old
                // .Where(a => a.ArtistId == artist.Id) fired a per-row DB round trip for every album on
                // every matching artist (N+1 -> OOM at large-library scale). album.ArtistId == the Id of
                // the artist whose ArtistMetadataId == album.ArtistMetadataId, so this is equivalent.
                var albumsByMetadataId = albums
                    .Where(a => a != null)
                    .GroupBy(a => a.ArtistMetadataId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<Album>)g.ToList());

                var normalizedRecArtist = NormalizeName(recommendation.Artist);
                var normalizedRecAlbum = NormalizeName(recommendation.Album);

                foreach (var artist in artists)
                {
                    var normalizedArtist = NormalizeName(artist.Name);
                    if (CalculateSimilarity(normalizedRecArtist, normalizedArtist) > 0.8)
                    {
                        var artistAlbums = albumsByMetadataId.TryGetValue(artist.ArtistMetadataId, out var byMetadata)
                            ? byMetadata
                            : (IReadOnlyList<Album>)Array.Empty<Album>();
                        foreach (var album in artistAlbums)
                        {
                            var normalizedAlbum = NormalizeName(album.Title);
                            if (CalculateSimilarity(normalizedRecAlbum, normalizedAlbum) > 0.8)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error checking for duplicates");
                return false;
            }
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var normalized = name.ToLowerInvariant();

            // Remove "The" prefix
            normalized = Regex.Replace(normalized, @"^the\s+", "");

            // Remove common punctuation
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

            // Remove edition/remaster suffixes
            normalized = Regex.Replace(normalized, @"\s*(deluxe|remaster|edition|special).*$", "");

            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        private double CalculateSimilarity(string str1, string str2)
        {
            if (str1 == str2) return 1.0;
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2)) return 0.0;

            // Simple Levenshtein similarity
            var distance = LevenshteinDistance(str1, str2);
            var maxLength = Math.Max(str1.Length, str2.Length);

            return 1.0 - (double)distance / maxLength;
        }

        private int LevenshteinDistance(string str1, string str2)
            // LOOP-010: delegate to Common's canonical edit-distance (byte-identical algorithm).
            => global::Lidarr.Plugin.Common.Utilities.StringSimilarity.LevenshteinDistance(str1, str2);

        private bool HasAIHallucinationPatterns(string album)
        {
            if (string.IsNullOrEmpty(album)) return false;

            var patterns = new[]
            {
                @"\b(multiverse|alternate|what if|imaginary|fictional)\b",
                @"\b(remastered\s+remastered|edition\s+edition)\b",
                @"\b(100th|200th|500th|1000th)\s+anniversary\b",
                @"\b(live)\b.*\b(studio recording)\b",
                @"\b(demo)\b.*\b(deluxe edition)\b",
                @"\b(silent|quiet)\b.*\b(noise|loud)\b",
                @"\b\d{4}\s+remaster\b.*\b\d{4}\b" // Multiple years
            };

            return patterns.Any(pattern => Regex.IsMatch(album, pattern, RegexOptions.IgnoreCase));
        }
    }
}
