using System;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Engines;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Rules;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Core
{
    /// <summary>
    /// Core hallucination detector that orchestrates various detection engines.
    /// Follows Single Responsibility Principle - only coordinates detection.
    /// </summary>
    public interface IHallucinationDetector
    {
        Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation);
    }

    public class HallucinationDetector : IHallucinationDetector
    {
        private readonly Logger _logger;
        private readonly IPatternMatchingEngine _patternEngine;
        private readonly IConfidenceCalculator _confidenceCalculator;
        private readonly IValidationRuleSet _ruleSet;

        public HallucinationDetector(
            Logger logger,
            IPatternMatchingEngine patternEngine,
            IConfidenceCalculator confidenceCalculator,
            IValidationRuleSet ruleSet)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _patternEngine = patternEngine ?? throw new ArgumentNullException(nameof(patternEngine));
            _confidenceCalculator = confidenceCalculator ?? throw new ArgumentNullException(nameof(confidenceCalculator));
            _ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        }

        public async Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation)
        {
            if (recommendation == null)
            {
                throw new ArgumentNullException(nameof(recommendation));
            }

            _logger.Debug($"Analyzing recommendation: {recommendation.Artist} - {recommendation.Album}");

            var result = new HallucinationDetectionResult
            {
                Recommendation = recommendation,
                AnalyzedAt = DateTime.UtcNow
            };

            try
            {
                // Execute pattern matching
                var patternResults = await _patternEngine.AnalyzePatternsAsync(recommendation);
                result.PatternMatches = patternResults;

                // Apply validation rules
                var ruleResults = await _ruleSet.ValidateAsync(recommendation);
                result.RuleViolations = ruleResults;

                // Calculate confidence score
                var confidence = await _confidenceCalculator.CalculateConfidenceAsync(
                    recommendation,
                    patternResults,
                    ruleResults);
                
                result.ConfidenceScore = confidence.Score;
                result.IsHallucination = confidence.Score < 0.5;
                result.HallucinationProbability = 1.0 - confidence.Score;

                _logger.Info($"Hallucination detection complete: {recommendation.Artist} - {recommendation.Album}, " +
                           $"Confidence: {confidence.Score:P}, IsHallucination: {result.IsHallucination}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error detecting hallucination for {recommendation.Artist} - {recommendation.Album}");
                result.DetectionError = ex.Message;
                result.IsHallucination = true; // Conservative approach on error
                return result;
            }
        }
    }

    /// <summary>
    /// Result of hallucination detection analysis
    /// </summary>
    public class HallucinationDetectionResult
    {
        public Recommendation Recommendation { get; set; }
        public bool IsHallucination { get; set; }
        public double HallucinationProbability { get; set; }
        public double ConfidenceScore { get; set; }
        public PatternMatchResults PatternMatches { get; set; }
        public ValidationRuleResults RuleViolations { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public string DetectionError { get; set; }
    }
}