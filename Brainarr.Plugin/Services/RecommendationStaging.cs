using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IRecommendationStaging
    {
        void StageRecommendation(ResolvedRecommendation recommendation);
        List<ResolvedRecommendation> GetStagedRecommendations();
        List<ResolvedRecommendation> ProcessStagedRecommendations(double confidenceThreshold);
        void ClearStagedRecommendations();
        StagingStats GetStagingStats();
    }

    public class RecommendationStaging : IRecommendationStaging
    {
        private readonly Logger _logger;
        private readonly List<ResolvedRecommendation> _stagedRecommendations;
        private readonly object _lockObject = new object();
        
        // Confidence thresholds
        private const double HIGH_CONFIDENCE_THRESHOLD = 0.8;
        private const double MEDIUM_CONFIDENCE_THRESHOLD = 0.6;
        private const double LOW_CONFIDENCE_THRESHOLD = 0.4;

        public RecommendationStaging(Logger logger)
        {
            _logger = logger;
            _stagedRecommendations = new List<ResolvedRecommendation>();
        }

        public void StageRecommendation(ResolvedRecommendation recommendation)
        {
            if (recommendation == null) return;
            
            lock (_lockObject)
            {
                // Only stage recommendations that need review
                if (recommendation.Status == ResolutionStatus.Resolved && 
                    recommendation.Confidence < HIGH_CONFIDENCE_THRESHOLD)
                {
                    _stagedRecommendations.Add(recommendation);
                    _logger.Debug($"Staged recommendation: {recommendation.DisplayArtist} - {recommendation.DisplayAlbum} " +
                                 $"(confidence: {recommendation.Confidence:F2})");
                }
            }
        }

        public List<ResolvedRecommendation> GetStagedRecommendations()
        {
            lock (_lockObject)
            {
                return _stagedRecommendations.ToList();
            }
        }

        public List<ResolvedRecommendation> ProcessStagedRecommendations(double confidenceThreshold)
        {
            lock (_lockObject)
            {
                var toProcess = _stagedRecommendations
                    .Where(r => r.Confidence >= confidenceThreshold)
                    .OrderByDescending(r => r.Confidence)
                    .ToList();
                
                // Remove processed recommendations from staging
                foreach (var rec in toProcess)
                {
                    _stagedRecommendations.Remove(rec);
                }
                
                _logger.Info($"Processing {toProcess.Count} staged recommendations with confidence >= {confidenceThreshold:F2}");
                
                return toProcess;
            }
        }

        public void ClearStagedRecommendations()
        {
            lock (_lockObject)
            {
                var count = _stagedRecommendations.Count;
                _stagedRecommendations.Clear();
                _logger.Info($"Cleared {count} staged recommendations");
            }
        }

        public StagingStats GetStagingStats()
        {
            lock (_lockObject)
            {
                return new StagingStats
                {
                    TotalStaged = _stagedRecommendations.Count,
                    HighConfidence = _stagedRecommendations.Count(r => r.Confidence >= HIGH_CONFIDENCE_THRESHOLD),
                    MediumConfidence = _stagedRecommendations.Count(r => r.Confidence >= MEDIUM_CONFIDENCE_THRESHOLD && r.Confidence < HIGH_CONFIDENCE_THRESHOLD),
                    LowConfidence = _stagedRecommendations.Count(r => r.Confidence < MEDIUM_CONFIDENCE_THRESHOLD),
                    AverageConfidence = _stagedRecommendations.Any() ? _stagedRecommendations.Average(r => r.Confidence) : 0
                };
            }
        }
    }

    public class StagingStats
    {
        public int TotalStaged { get; set; }
        public int HighConfidence { get; set; }
        public int MediumConfidence { get; set; }
        public int LowConfidence { get; set; }
        public double AverageConfidence { get; set; }
    }

    public class RecommendationReview
    {
        private readonly Logger _logger;
        private readonly IRecommendationStaging _staging;
        private readonly IMusicBrainzResolver _resolver;

        public RecommendationReview(
            Logger logger,
            IRecommendationStaging staging,
            IMusicBrainzResolver resolver)
        {
            _logger = logger;
            _staging = staging;
            _resolver = resolver;
        }

        public async Task<List<ResolvedRecommendation>> ReviewAndImproveRecommendations(
            List<Recommendation> recommendations,
            double minConfidenceForImport = 0.7)
        {
            var resolvedRecommendations = new List<ResolvedRecommendation>();
            var toReview = new List<ResolvedRecommendation>();

            // First pass: resolve all recommendations
            foreach (var rec in recommendations)
            {
                var resolved = await _resolver.ResolveRecommendation(rec);
                
                if (resolved.Status == ResolutionStatus.Resolved)
                {
                    if (resolved.Confidence >= minConfidenceForImport)
                    {
                        // High confidence - add directly
                        resolvedRecommendations.Add(resolved);
                        _logger.Info($"Auto-approved: {resolved.DisplayArtist} - {resolved.DisplayAlbum} " +
                                    $"(confidence: {resolved.Confidence:F2})");
                    }
                    else
                    {
                        // Low confidence - stage for review
                        _staging.StageRecommendation(resolved);
                        toReview.Add(resolved);
                    }
                }
                else
                {
                    _logger.Debug($"Skipped: {rec.Artist} - {rec.Album} (status: {resolved.Status})");
                }
            }

            // Second pass: try to improve low-confidence recommendations
            if (toReview.Any())
            {
                _logger.Info($"Reviewing {toReview.Count} low-confidence recommendations");
                
                var improved = await TryImproveRecommendations(toReview);
                resolvedRecommendations.AddRange(improved);
            }

            // Log staging stats
            var stats = _staging.GetStagingStats();
            if (stats.TotalStaged > 0)
            {
                _logger.Info($"Staging stats: {stats.TotalStaged} total, " +
                            $"{stats.HighConfidence} high, {stats.MediumConfidence} medium, " +
                            $"{stats.LowConfidence} low confidence (avg: {stats.AverageConfidence:F2})");
            }

            return resolvedRecommendations;
        }

        private async Task<List<ResolvedRecommendation>> TryImproveRecommendations(
            List<ResolvedRecommendation> lowConfidenceRecs)
        {
            var improved = new List<ResolvedRecommendation>();

            foreach (var rec in lowConfidenceRecs)
            {
                // Try alternative searches or fuzzy matching
                var alternativeSearches = GenerateAlternativeSearches(rec.OriginalRecommendation);
                
                foreach (var altSearch in alternativeSearches)
                {
                    var altResolved = await _resolver.ResolveRecommendation(altSearch);
                    
                    if (altResolved.Status == ResolutionStatus.Resolved && 
                        altResolved.Confidence > rec.Confidence)
                    {
                        _logger.Info($"Improved match: '{rec.OriginalRecommendation.Artist}' -> " +
                                    $"'{altResolved.DisplayArtist}' (confidence: {rec.Confidence:F2} -> {altResolved.Confidence:F2})");
                        improved.Add(altResolved);
                        break;
                    }
                }
            }

            return improved;
        }

        private List<Recommendation> GenerateAlternativeSearches(Recommendation original)
        {
            var alternatives = new List<Recommendation>();

            // Try without "The" prefix
            if (original.Artist.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                alternatives.Add(new Recommendation
                {
                    Artist = original.Artist.Substring(4),
                    Album = original.Album,
                    Genre = original.Genre,
                    Confidence = original.Confidence,
                    Reason = original.Reason
                });
            }

            // Try with "The" prefix
            if (!original.Artist.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                alternatives.Add(new Recommendation
                {
                    Artist = "The " + original.Artist,
                    Album = original.Album,
                    Genre = original.Genre,
                    Confidence = original.Confidence,
                    Reason = original.Reason
                });
            }

            // Try removing special characters
            var cleanedArtist = System.Text.RegularExpressions.Regex.Replace(
                original.Artist, @"[^\w\s]", "");
            
            if (cleanedArtist != original.Artist)
            {
                alternatives.Add(new Recommendation
                {
                    Artist = cleanedArtist,
                    Album = original.Album,
                    Genre = original.Genre,
                    Confidence = original.Confidence,
                    Reason = original.Reason
                });
            }

            return alternatives;
        }
    }
}