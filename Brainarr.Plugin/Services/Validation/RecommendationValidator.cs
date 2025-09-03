using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Orchestrates validation of AI-generated recommendations using basic format checks,
    /// duplicate detection, hallucination detection, and optional MusicBrainz verification.
    /// Aligns with the async IRecommendationValidator interface expected by tests.
    /// </summary>
    public class RecommendationValidator : IRecommendationValidator
    {
        private readonly Logger _logger;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IHallucinationDetector _hallucinationDetector;
        private readonly IAdvancedDuplicateDetector _duplicateDetector;
        private readonly IMusicBrainzService _musicBrainzService;

        private const int CURRENT_YEAR = 2024;
        private const int MIN_REASONABLE_YEAR = 1900;

        public RecommendationValidator(
            Logger logger,
            IArtistService artistService,
            IAlbumService albumService,
            IHallucinationDetector hallucinationDetector,
            IAdvancedDuplicateDetector duplicateDetector,
            IMusicBrainzService musicBrainzService)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _artistService = artistService;
            _albumService = albumService;
            _hallucinationDetector = hallucinationDetector;
            _duplicateDetector = duplicateDetector;
            _musicBrainzService = musicBrainzService;
        }

        public async Task<ValidationResult> ValidateRecommendationAsync(Recommendation recommendation)
        {
            if (recommendation == null) throw new ArgumentNullException(nameof(recommendation));

            var result = new ValidationResult
            {
                Recommendation = recommendation,
                Score = 1.0,
            };

            // 1) Basic format checks
            ValidateBasicFormat(recommendation, result);

            // Early exit if critically invalid
            if (HasCritical(result))
            {
                result.IsValid = false;
                result.Score = Math.Min(result.Score, 0.1);
                return result;
            }

            // 2) Release date sanity
            ValidateReleaseDate(recommendation, result);

            // 3) Hallucination detection (advanced)
            if (_hallucinationDetector != null)
            {
                var hallucination = await _hallucinationDetector.DetectHallucinationAsync(recommendation);
                if (hallucination != null && hallucination.HallucinationConfidence > 0.0)
                {
                    result.Findings.Add(new ValidationFinding
                    {
                        CheckType = ValidationCheckType.HallucinationDetection,
                        Severity = hallucination.HallucinationConfidence >= 0.7 ? ValidationSeverity.Error : ValidationSeverity.Warning,
                        Message = "Hallucination detector found suspicious patterns",
                        ScoreImpact = -Math.Min(0.6, hallucination.HallucinationConfidence)
                    });
                    result.Score = Math.Max(0.0, result.Score + result.Findings.Last().ScoreImpact);
                }
            }

            // 4) Duplicate detection (advanced)
            if (_duplicateDetector != null)
            {
                var isDup = await _duplicateDetector.IsAlreadyInLibraryAsync(recommendation);
                if (isDup)
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

            // 5) Optional MusicBrainz validation to increase confidence (non-fatal)
            try
            {
                if (_musicBrainzService != null &&
                    !string.IsNullOrWhiteSpace(recommendation.Artist) &&
                    !string.IsNullOrWhiteSpace(recommendation.Album))
                {
                    var ok = await _musicBrainzService.ValidateArtistAlbumAsync(recommendation.Artist, recommendation.Album);
                    if (!ok)
                    {
                        // Soft penalty; hallucination detector already handles heavy deductions
                        result.Findings.Add(new ValidationFinding
                        {
                            CheckType = ValidationCheckType.CrossReference,
                            Severity = ValidationSeverity.Warning,
                            Message = "Not verified on MusicBrainz",
                            ScoreImpact = -0.1
                        });
                        result.Score = Math.Max(0.0, result.Score - 0.1);
                    }
                }
            }
            catch
            {
                // Non-fatal
            }

            // Final validity
            result.IsValid = result.Score >= 0.7 && !HasCritical(result);
            return result;
        }

        public async Task<List<ValidationResult>> ValidateRecommendationsAsync(List<Recommendation> recommendations)
        {
            var list = new List<ValidationResult>();
            if (recommendations == null) return list;
            foreach (var r in recommendations)
            {
                list.Add(await ValidateRecommendationAsync(r));
            }
            return list;
        }

        public async Task<List<Recommendation>> FilterValidRecommendationsAsync(List<Recommendation> recommendations, double minScore = 0.7)
        {
            var results = await ValidateRecommendationsAsync(recommendations ?? new List<Recommendation>());
            return results.Where(r => r.IsValid && r.Score >= minScore).Select(r => r.Recommendation).ToList();
        }

        public async Task<bool> IsAlreadyInLibraryAsync(Recommendation recommendation)
        {
            if (_duplicateDetector == null) return false;
            return await _duplicateDetector.IsAlreadyInLibraryAsync(recommendation);
        }

        public async Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation)
        {
            if (_hallucinationDetector == null)
            {
                return new HallucinationDetectionResult { HallucinationConfidence = 0.0 };
            }
            return await _hallucinationDetector.DetectHallucinationAsync(recommendation);
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
            if (!recommendation.Year.HasValue) return;
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

        private static bool HasCritical(ValidationResult result)
        {
            return result.Findings.Any(f => f.Severity == ValidationSeverity.Critical);
        }
    }
}
