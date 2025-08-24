using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Comprehensive recommendation validator that filters AI hallucinations and ensures quality.
    /// </summary>
    public class RecommendationValidator : IRecommendationValidator
    {
        private readonly Logger _logger;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IHallucinationDetector _hallucinationDetector;
        private readonly IAdvancedDuplicateDetector _duplicateDetector;
        private readonly IMusicBrainzService _musicBrainzService;
        
        // Validation configuration
        private const double DEFAULT_MIN_SCORE = 0.7;
        private const int CURRENT_YEAR = 2024;
        private const int MIN_REASONABLE_YEAR = 1900;

        public RecommendationValidator(
            Logger logger,
            IArtistService artistService,
            IAlbumService albumService,
            IHallucinationDetector hallucinationDetector,
            IAdvancedDuplicateDetector duplicateDetector,
            IMusicBrainzService? musicBrainzService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _hallucinationDetector = hallucinationDetector ?? throw new ArgumentNullException(nameof(hallucinationDetector));
            _duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
            _musicBrainzService = musicBrainzService; // Optional
        }

        public async Task<ValidationResult> ValidateRecommendationAsync(Recommendation recommendation)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ValidationResult
            {
                Recommendation = recommendation,
                Score = 1.0 // Start with perfect score, deduct for issues
            };

            try
            {
                _logger.Debug($"Validating recommendation: {recommendation.Artist} - {recommendation.Album}");

                // Perform validation checks in order of importance
                await PerformBasicFormatValidation(recommendation, result);
                await PerformDuplicateCheck(recommendation, result);
                await PerformReleaseDateValidation(recommendation, result);
                await PerformHallucinationDetection(recommendation, result);
                await PerformExistenceValidation(recommendation, result);
                await PerformGenreValidation(recommendation, result);
                await PerformQualityAssessment(recommendation, result);

                // Determine final validity
                result.IsValid = result.Score >= DEFAULT_MIN_SCORE && 
                               !result.Findings.Any(f => f.Severity == ValidationSeverity.Critical);

                stopwatch.Stop();
                result.Metadata.ValidationTimeMs = stopwatch.ElapsedMilliseconds;
                result.Metadata.ChecksPerformed.AddRange(GetPerformedChecks(result));

                _logger.Debug($"Validation complete: {recommendation.Artist} - {recommendation.Album} " +
                            $"(Score: {result.Score:F2}, Valid: {result.IsValid}, Time: {stopwatch.ElapsedMilliseconds}ms)");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error validating recommendation: {recommendation.Artist} - {recommendation.Album}");
                
                result.Score = 0.0;
                result.IsValid = false;
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.QualityAssessment,
                    Severity = ValidationSeverity.Critical,
                    Message = $"Validation failed with error: {ex.Message}",
                    ScoreImpact = -1.0
                });

                return result;
            }
        }

        public async Task<List<ValidationResult>> ValidateRecommendationsAsync(List<Recommendation> recommendations)
        {
            var results = new List<ValidationResult>();
            var tasks = new List<Task<ValidationResult>>();

            _logger.Info($"Validating batch of {recommendations.Count} recommendations");

            // Validate recommendations in parallel for performance
            foreach (var recommendation in recommendations)
            {
                tasks.Add(ValidateRecommendationAsync(recommendation));
            }

            results.AddRange(await Task.WhenAll(tasks));

            // Perform batch-level validation
            await PerformBatchValidation(results);

            var validCount = results.Count(r => r.IsValid);
            var avgScore = results.Average(r => r.Score);
            
            _logger.Info($"Batch validation complete: {validCount}/{recommendations.Count} valid " +
                        $"(Average score: {avgScore:F2})");

            return results;
        }

        public async Task<List<Recommendation>> FilterValidRecommendationsAsync(List<Recommendation> recommendations, double minScore = DEFAULT_MIN_SCORE)
        {
            var validationResults = await ValidateRecommendationsAsync(recommendations);
            
            var validRecommendations = validationResults
                .Where(r => r.IsValid && r.Score >= minScore)
                .Select(r => r.Recommendation)
                .ToList();

            var rejectedCount = recommendations.Count - validRecommendations.Count;
            if (rejectedCount > 0)
            {
                _logger.Info($"Filtered out {rejectedCount} invalid recommendations " +
                           $"(kept {validRecommendations.Count}/{recommendations.Count})");
            }

            return validRecommendations;
        }

        public async Task<bool> IsAlreadyInLibraryAsync(Recommendation recommendation)
        {
            return await _duplicateDetector.IsAlreadyInLibraryAsync(recommendation);
        }

        public async Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation)
        {
            return await _hallucinationDetector.DetectHallucinationAsync(recommendation);
        }

        private async Task PerformBasicFormatValidation(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.FormatValidation);

            var issues = new List<string>();

            // Check for required fields
            if (string.IsNullOrWhiteSpace(recommendation.Artist))
                issues.Add("Artist name is empty or null");
                
            if (string.IsNullOrWhiteSpace(recommendation.Album))
                issues.Add("Album name is empty or null");

            // Check for suspicious characters/patterns
            if (HasSuspiciousCharacters(recommendation.Artist))
                issues.Add("Artist name contains suspicious characters");
                
            if (HasSuspiciousCharacters(recommendation.Album))
                issues.Add("Album name contains suspicious characters");

            // Check length constraints
            if (recommendation.Artist?.Length > 200)
                issues.Add("Artist name is unusually long");
                
            if (recommendation.Album?.Length > 300)
                issues.Add("Album name is unusually long");

            foreach (var issue in issues)
            {
                var severity = issue.Contains("empty or null") ? ValidationSeverity.Critical : ValidationSeverity.Warning;
                var impact = severity == ValidationSeverity.Critical ? -1.0 : -0.1;
                
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.FormatValidation,
                    Severity = severity,
                    Message = issue,
                    ScoreImpact = impact
                });
                
                result.Score = Math.Max(0.0, result.Score + impact);
            }
        }

        private async Task PerformDuplicateCheck(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.DuplicateDetection);

            var isDuplicate = await _duplicateDetector.IsAlreadyInLibraryAsync(recommendation);
            
            if (isDuplicate)
            {
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.DuplicateDetection,
                    Severity = ValidationSeverity.Critical,
                    Message = "Album is already in the library",
                    ScoreImpact = -1.0,
                    Context = new Dictionary<string, object>
                    {
                        ["MatchType"] = "ExactDuplicate",
                        ["Artist"] = recommendation.Artist,
                        ["Album"] = recommendation.Album
                    }
                });
                
                result.Score = 0.0; // Automatic rejection for duplicates
            }
        }

        private async Task PerformReleaseDateValidation(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.ReleaseDateValidation);

            if (recommendation.Year.HasValue)
            {
                var year = recommendation.Year.Value;
                var issues = new List<string>();

                // Check for impossible years
                if (year < MIN_REASONABLE_YEAR)
                    issues.Add($"Release year {year} is before recorded music history");
                    
                if (year > CURRENT_YEAR + 2) // Allow 2 years future for upcoming releases
                    issues.Add($"Release year {year} is too far in the future");

                // Check for suspicious patterns
                if (year == 0 || year == 1900 || year == 2000)
                    issues.Add($"Release year {year} appears to be a placeholder");

                foreach (var issue in issues)
                {
                    var severity = issue.Contains("before recorded music") || issue.Contains("too far in the future") 
                        ? ValidationSeverity.Critical : ValidationSeverity.Warning;
                    var impact = severity == ValidationSeverity.Critical ? -0.8 : -0.2;
                    
                    result.Findings.Add(new ValidationFinding
                    {
                        CheckType = ValidationCheckType.ReleaseDateValidation,
                        Severity = severity,
                        Message = issue,
                        ScoreImpact = impact,
                        Context = new Dictionary<string, object> { ["Year"] = year }
                    });
                    
                    result.Score = Math.Max(0.0, result.Score + impact);
                }
            }
        }

        private async Task PerformHallucinationDetection(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.HallucinationDetection);

            var hallucinationResult = await _hallucinationDetector.DetectHallucinationAsync(recommendation);
            
            if (hallucinationResult.IsLikelyHallucination)
            {
                var impact = -0.6 * hallucinationResult.HallucinationConfidence;
                
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.HallucinationDetection,
                    Severity = hallucinationResult.HallucinationConfidence > 0.9 
                        ? ValidationSeverity.Critical : ValidationSeverity.Error,
                    Message = $"Likely AI hallucination detected (confidence: {hallucinationResult.HallucinationConfidence:P0})",
                    ScoreImpact = impact,
                    Context = new Dictionary<string, object>
                    {
                        ["HallucinationConfidence"] = hallucinationResult.HallucinationConfidence,
                        ["DetectedPatterns"] = hallucinationResult.DetectedPatterns.Select(p => p.PatternType.ToString()).ToList()
                    }
                });
                
                result.Score = Math.Max(0.0, result.Score + impact);
            }
        }

        private async Task PerformExistenceValidation(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.ArtistExistence);
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.AlbumExistence);

            // If MusicBrainz service is available, use it for validation
            if (_musicBrainzService != null)
            {
                result.Metadata.UsedExternalSources = true;
                result.Metadata.ApiCallCount++;

                var exists = await _musicBrainzService.ValidateArtistAlbumAsync(
                    recommendation.Artist, recommendation.Album);
                
                if (!exists)
                {
                    result.Findings.Add(new ValidationFinding
                    {
                        CheckType = ValidationCheckType.AlbumExistence,
                        Severity = ValidationSeverity.Error,
                        Message = "Artist/album combination not found in MusicBrainz database",
                        ScoreImpact = -0.5,
                        Context = new Dictionary<string, object>
                        {
                            ["DataSource"] = "MusicBrainz",
                            ["SearchQuery"] = $"{recommendation.Artist} - {recommendation.Album}"
                        }
                    });
                    
                    result.Score = Math.Max(0.0, result.Score - 0.5);
                }
            }
        }

        private async Task PerformGenreValidation(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.GenreAccuracy);

            if (!string.IsNullOrWhiteSpace(recommendation.Genre))
            {
                // Check for valid genre format
                if (IsValidGenre(recommendation.Genre))
                {
                    result.Findings.Add(new ValidationFinding
                    {
                        CheckType = ValidationCheckType.GenreAccuracy,
                        Severity = ValidationSeverity.Info,
                        Message = $"Genre '{recommendation.Genre}' appears valid",
                        ScoreImpact = 0.05 // Small bonus for having valid genre
                    });
                    
                    result.Score = Math.Min(1.0, result.Score + 0.05);
                }
                else
                {
                    result.Findings.Add(new ValidationFinding
                    {
                        CheckType = ValidationCheckType.GenreAccuracy,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Genre '{recommendation.Genre}' appears unusual or invalid",
                        ScoreImpact = -0.1
                    });
                    
                    result.Score = Math.Max(0.0, result.Score - 0.1);
                }
            }
        }

        private async Task PerformQualityAssessment(Recommendation recommendation, ValidationResult result)
        {
            result.Metadata.ChecksPerformed.Add(ValidationCheckType.QualityAssessment);

            var qualityScore = 0.0;
            var qualityFactors = new List<string>();

            // Confidence score assessment
            if (recommendation.Confidence > 0)
            {
                var confidence = recommendation.Confidence;
                if (confidence >= 0.8)
                {
                    qualityScore += 0.1;
                    qualityFactors.Add($"High confidence ({confidence:P0})");
                }
                else if (confidence < 0.5)
                {
                    qualityScore -= 0.1;
                    qualityFactors.Add($"Low confidence ({confidence:P0})");
                }
            }

            // Reason quality assessment
            if (!string.IsNullOrWhiteSpace(recommendation.Reason))
            {
                if (recommendation.Reason.Length > 20 && !HasGenericReason(recommendation.Reason))
                {
                    qualityScore += 0.05;
                    qualityFactors.Add("Detailed reasoning provided");
                }
                else
                {
                    qualityScore -= 0.05;
                    qualityFactors.Add("Generic or minimal reasoning");
                }
            }

            if (qualityFactors.Any())
            {
                result.Findings.Add(new ValidationFinding
                {
                    CheckType = ValidationCheckType.QualityAssessment,
                    Severity = ValidationSeverity.Info,
                    Message = $"Quality factors: {string.Join(", ", qualityFactors)}",
                    ScoreImpact = qualityScore,
                    Context = new Dictionary<string, object>
                    {
                        ["QualityScore"] = qualityScore,
                        ["Factors"] = qualityFactors
                    }
                });

                result.Score = Math.Max(0.0, Math.Min(1.0, result.Score + qualityScore));
            }
        }

        private async Task PerformBatchValidation(List<ValidationResult> results)
        {
            // Check for batch-level issues like duplicate recommendations within the batch
            var recommendations = results.Select(r => r.Recommendation).ToList();
            var duplicateGroups = recommendations
                .GroupBy(r => $"{r.Artist?.ToLower()}|{r.Album?.ToLower()}")
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGroups.Any())
            {
                _logger.Warn($"Found {duplicateGroups.Count} duplicate groups within recommendation batch");
                
                foreach (var group in duplicateGroups)
                {
                    var duplicates = group.Skip(1); // Keep first, mark others as invalid
                    foreach (var duplicate in duplicates)
                    {
                        var result = results.First(r => r.Recommendation == duplicate);
                        result.Score = 0.0;
                        result.IsValid = false;
                        result.Findings.Add(new ValidationFinding
                        {
                            CheckType = ValidationCheckType.DuplicateDetection,
                            Severity = ValidationSeverity.Critical,
                            Message = "Duplicate recommendation within the same batch",
                            ScoreImpact = -1.0
                        });
                    }
                }
            }
        }

        private List<ValidationCheckType> GetPerformedChecks(ValidationResult result)
        {
            return result.Findings.Select(f => f.CheckType).Distinct().ToList();
        }

        private bool HasSuspiciousCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            // Check for patterns that might indicate AI hallucination
            var suspiciousPatterns = new[]
            {
                @"[\x00-\x1F\x7F-\x9F]", // Control characters
                @"[^\w\s\-\(\)\[\]\.,'&]", // Unusual special characters
                @"\b(undefined|null|error|invalid)\b", // Error strings
                @"[A-Z]{5,}", // Too many consecutive capitals
                @"\d{5,}", // Too many consecutive digits
                @"(.)\1{4,}" // Repeated characters
            };

            return suspiciousPatterns.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        private bool IsValidGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre)) return false;
            
            // List of known valid genres (expandable)
            var validGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Rock", "Pop", "Jazz", "Classical", "Electronic", "Hip Hop", "R&B", "Country", 
                "Folk", "Blues", "Reggae", "Metal", "Punk", "Alternative", "Indie", "Funk",
                "Soul", "Dance", "House", "Techno", "Ambient", "Experimental", "World"
            };

            // Check exact matches and partial matches for compound genres
            return validGenres.Contains(genre) || 
                   validGenres.Any(vg => genre.Contains(vg, StringComparison.OrdinalIgnoreCase));
        }

        private bool HasGenericReason(string reason)
        {
            var genericPhrases = new[]
            {
                "similar to", "you might like", "popular", "good music", "recommended",
                "based on", "fits your taste", "great album"
            };

            return genericPhrases.Any(phrase => 
                reason.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        }
    }
}