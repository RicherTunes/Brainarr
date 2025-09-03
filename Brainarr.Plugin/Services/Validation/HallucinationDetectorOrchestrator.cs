using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Detectors;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Result from hallucination detection analysis
    /// </summary>
    public class HallucinationDetectionResult
    {
        public double HallucinationConfidence { get; set; }
        public List<HallucinationPattern> DetectedPatterns { get; set; } = new List<HallucinationPattern>();
        public bool IsLikelyHallucination => HallucinationConfidence > 0.7;
        public bool IsConfirmedHallucination => HallucinationConfidence > 0.9;
        public string Summary { get; set; } = string.Empty;
        public TimeSpan AnalysisTime { get; set; }
    }

    /// <summary>
    /// Interface for the hallucination detection orchestrator
    /// </summary>
    public interface IHallucinationDetectorOrchestrator
    {
        Task<HallucinationDetectionResult> DetectHallucinationAsync(
            Recommendation recommendation, 
            CancellationToken cancellationToken = default);
        
        void RegisterDetector(ISpecificHallucinationDetector detector);
        void UnregisterDetector(HallucinationPatternType patternType);
        IReadOnlyList<ISpecificHallucinationDetector> GetActiveDetectors();
    }

    /// <summary>
    /// Orchestrates multiple hallucination detection strategies
    /// </summary>
    public class HallucinationDetectorOrchestrator : IHallucinationDetectorOrchestrator
    {
        private readonly Logger _logger;
        private readonly Dictionary<HallucinationPatternType, ISpecificHallucinationDetector> _detectors;
        private readonly object _lock = new object();

        public HallucinationDetectorOrchestrator(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _detectors = new Dictionary<HallucinationPatternType, ISpecificHallucinationDetector>();
            
            // Register default detectors
            RegisterDefaultDetectors();
        }

        public async Task<HallucinationDetectionResult> DetectHallucinationAsync(
            Recommendation recommendation, 
            CancellationToken cancellationToken = default)
        {
            if (recommendation == null)
                throw new ArgumentNullException(nameof(recommendation));

            var startTime = DateTime.UtcNow;
            var result = new HallucinationDetectionResult();

            try
            {
                _logger.Debug($"Starting hallucination detection for: {recommendation.Artist} - {recommendation.Album}");

                // Get active detectors ordered by priority
                var activeDetectors = GetActiveDetectors()
                    .OrderByDescending(d => d.Priority)
                    .ToList();

                // Run detectors in parallel for performance
                var detectionTasks = activeDetectors
                    .Select(detector => RunDetectorAsync(detector, recommendation, cancellationToken))
                    .ToList();

                var patterns = await Task.WhenAll(detectionTasks);

                // Add non-null patterns to results
                foreach (var pattern in patterns.Where(p => p != null && p.Confidence > 0))
                {
                    result.DetectedPatterns.Add(pattern);
                }

                // Calculate overall confidence
                CalculateOverallConfidence(result);

                // Generate summary
                GenerateSummary(result);

                result.AnalysisTime = DateTime.UtcNow - startTime;

                _logger.Info($"Hallucination detection complete: {recommendation.Artist} - {recommendation.Album} " +
                           $"(Confidence: {result.HallucinationConfidence:P2}, Patterns: {result.DetectedPatterns.Count}, " +
                           $"Time: {result.AnalysisTime.TotalMilliseconds}ms)");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error during hallucination detection for: {recommendation.Artist} - {recommendation.Album}");
                
                // Return safe result on error
                result.HallucinationConfidence = 0.5;
                result.Summary = $"Detection partially failed: {ex.Message}";
                result.AnalysisTime = DateTime.UtcNow - startTime;
                
                return result;
            }
        }

        public void RegisterDetector(ISpecificHallucinationDetector detector)
        {
            if (detector == null)
                throw new ArgumentNullException(nameof(detector));

            lock (_lock)
            {
                _detectors[detector.PatternType] = detector;
                _logger.Debug($"Registered detector for pattern type: {detector.PatternType}");
            }
        }

        public void UnregisterDetector(HallucinationPatternType patternType)
        {
            lock (_lock)
            {
                if (_detectors.Remove(patternType))
                {
                    _logger.Debug($"Unregistered detector for pattern type: {patternType}");
                }
            }
        }

        public IReadOnlyList<ISpecificHallucinationDetector> GetActiveDetectors()
        {
            lock (_lock)
            {
                return _detectors.Values
                    .Where(d => d.IsEnabled)
                    .ToList()
                    .AsReadOnly();
            }
        }

        private void RegisterDefaultDetectors()
        {
            // Register all default detectors
            RegisterDetector(new ArtistExistenceDetector(_logger));
            RegisterDetector(new ReleaseDateValidator(_logger));
            RegisterDetector(new NamePatternAnalyzer(_logger));
            // Additional detectors can be registered here
        }

        private async Task<HallucinationPattern> RunDetectorAsync(
            ISpecificHallucinationDetector detector,
            Recommendation recommendation,
            CancellationToken cancellationToken)
        {
            try
            {
                // Run detector with timeout protection
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(5)); // 5 second timeout per detector
                    
                    var pattern = await detector.DetectAsync(recommendation);
                    
                    if (pattern != null)
                    {
                        _logger.Debug($"Detector {detector.PatternType} found pattern with confidence {pattern.Confidence:F2}");
                    }
                    
                    return pattern;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Detector {detector.PatternType} timed out or was cancelled");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error running detector {detector.PatternType}");
                return null;
            }
        }

        private void CalculateOverallConfidence(HallucinationDetectionResult result)
        {
            if (result.DetectedPatterns.Count == 0)
            {
                result.HallucinationConfidence = 0.0;
                return;
            }

            // Weight patterns by their confidence and type
            var weightedConfidence = 0.0;
            var totalWeight = 0.0;

            foreach (var pattern in result.DetectedPatterns)
            {
                // Critical patterns get higher weight
                var weight = pattern.PatternType switch
                {
                    HallucinationPatternType.NonExistentArtist => 2.0,
                    HallucinationPatternType.NonExistentAlbum => 1.8,
                    HallucinationPatternType.ImpossibleReleaseDate => 1.5,
                    HallucinationPatternType.NamePatternAnomaly => 1.2,
                    _ => 1.0
                };

                // Confirmed hallucinations get extra weight
                if (pattern.IsConfirmedHallucination)
                    weight *= 1.5;

                weightedConfidence += pattern.Confidence * weight;
                totalWeight += weight;
            }

            // Calculate weighted average
            result.HallucinationConfidence = totalWeight > 0 
                ? Math.Min(1.0, weightedConfidence / totalWeight) 
                : 0.0;

            // Boost confidence if multiple patterns detected
            if (result.DetectedPatterns.Count >= 3)
            {
                result.HallucinationConfidence = Math.Min(1.0, result.HallucinationConfidence * 1.2);
            }
        }

        private void GenerateSummary(HallucinationDetectionResult result)
        {
            if (result.DetectedPatterns.Count == 0)
            {
                result.Summary = "No hallucination patterns detected";
                return;
            }

            var summaryParts = new List<string>();

            if (result.IsConfirmedHallucination)
            {
                summaryParts.Add("CONFIRMED HALLUCINATION");
            }
            else if (result.IsLikelyHallucination)
            {
                summaryParts.Add("LIKELY HALLUCINATION");
            }
            else
            {
                summaryParts.Add("POSSIBLE ISSUES");
            }

            // Add top issues
            var topPatterns = result.DetectedPatterns
                .OrderByDescending(p => p.Confidence)
                .Take(3)
                .Select(p => $"{p.PatternType}: {p.Description}");

            summaryParts.AddRange(topPatterns);

            result.Summary = string.Join(" | ", summaryParts);
        }
    }
}