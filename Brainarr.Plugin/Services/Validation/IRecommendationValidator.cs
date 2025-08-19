using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Interface for validating AI-generated music recommendations to prevent hallucinations and ensure quality.
    /// </summary>
    public interface IRecommendationValidator
    {
        /// <summary>
        /// Validates a single recommendation for existence, accuracy, and quality.
        /// </summary>
        /// <param name="recommendation">The recommendation to validate</param>
        /// <returns>Validation result with score and details</returns>
        Task<ValidationResult> ValidateRecommendationAsync(Recommendation recommendation);

        /// <summary>
        /// Validates a batch of recommendations, applying cross-recommendation checks.
        /// </summary>
        /// <param name="recommendations">List of recommendations to validate</param>
        /// <returns>List of validation results with batch-level insights</returns>
        Task<List<ValidationResult>> ValidateRecommendationsAsync(List<Recommendation> recommendations);

        /// <summary>
        /// Filters a list of recommendations, keeping only those that pass validation.
        /// </summary>
        /// <param name="recommendations">Raw recommendations from AI</param>
        /// <param name="minScore">Minimum validation score (0.0-1.0)</param>
        /// <returns>Filtered list of valid recommendations</returns>
        Task<List<Recommendation>> FilterValidRecommendationsAsync(List<Recommendation> recommendations, double minScore = 0.7);

        /// <summary>
        /// Checks if a recommendation already exists in the user's library.
        /// </summary>
        /// <param name="recommendation">Recommendation to check</param>
        /// <returns>True if already in library</returns>
        Task<bool> IsAlreadyInLibraryAsync(Recommendation recommendation);

        /// <summary>
        /// Detects potential AI hallucinations using pattern recognition.
        /// </summary>
        /// <param name="recommendation">Recommendation to analyze</param>
        /// <returns>Hallucination detection result</returns>
        Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation);
    }

    /// <summary>
    /// Result of recommendation validation with detailed scoring and reasoning.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Overall validation score (0.0 = invalid, 1.0 = perfect)
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Whether the recommendation passed validation
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Detailed validation findings
        /// </summary>
        public List<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();

        /// <summary>
        /// The validated recommendation (may be modified during validation)
        /// </summary>
        public Recommendation Recommendation { get; set; }

        /// <summary>
        /// Validation metadata for debugging and metrics
        /// </summary>
        public ValidationMetadata Metadata { get; set; } = new ValidationMetadata();
    }

    /// <summary>
    /// Individual validation finding with severity and details.
    /// </summary>
    public class ValidationFinding
    {
        /// <summary>
        /// Type of validation check that produced this finding
        /// </summary>
        public ValidationCheckType CheckType { get; set; }

        /// <summary>
        /// Severity of the finding
        /// </summary>
        public ValidationSeverity Severity { get; set; }

        /// <summary>
        /// Human-readable description of the finding
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Impact on overall validation score (-1.0 to +1.0)
        /// </summary>
        public double ScoreImpact { get; set; }

        /// <summary>
        /// Additional context data
        /// </summary>
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Result of AI hallucination detection.
    /// </summary>
    public class HallucinationDetectionResult
    {
        /// <summary>
        /// Confidence that this is a hallucination (0.0-1.0)
        /// </summary>
        public double HallucinationConfidence { get; set; }

        /// <summary>
        /// Detected hallucination patterns
        /// </summary>
        public List<HallucinationPattern> DetectedPatterns { get; set; } = new List<HallucinationPattern>();

        /// <summary>
        /// Whether this is likely a hallucination
        /// </summary>
        public bool IsLikelyHallucination => HallucinationConfidence > 0.7;
    }

    /// <summary>
    /// Specific hallucination pattern detected.
    /// </summary>
    public class HallucinationPattern
    {
        /// <summary>
        /// Type of hallucination pattern
        /// </summary>
        public HallucinationPatternType PatternType { get; set; }

        /// <summary>
        /// Description of the pattern
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Confidence in this pattern detection
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Evidence supporting this pattern
        /// </summary>
        public List<string> Evidence { get; set; } = new List<string>();
    }

    /// <summary>
    /// Metadata collected during validation process.
    /// </summary>
    public class ValidationMetadata
    {
        /// <summary>
        /// Time taken to validate (milliseconds)
        /// </summary>
        public long ValidationTimeMs { get; set; }

        /// <summary>
        /// External API calls made during validation
        /// </summary>
        public int ApiCallCount { get; set; }

        /// <summary>
        /// Validation checks performed
        /// </summary>
        public List<ValidationCheckType> ChecksPerformed { get; set; } = new List<ValidationCheckType>();

        /// <summary>
        /// Whether external data sources were consulted
        /// </summary>
        public bool UsedExternalSources { get; set; }

        /// <summary>
        /// Additional debug information
        /// </summary>
        public Dictionary<string, object> DebugInfo { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Types of validation checks performed.
    /// </summary>
    public enum ValidationCheckType
    {
        ArtistExistence,
        AlbumExistence,
        ReleaseDateValidation,
        GenreAccuracy,
        DuplicateDetection,
        FormatValidation,
        HallucinationDetection,
        QualityAssessment,
        RelevanceCheck,
        CrossReference
    }

    /// <summary>
    /// Severity levels for validation findings.
    /// </summary>
    public enum ValidationSeverity
    {
        Info,      // Informational, doesn't affect validity
        Warning,   // Minor issue, reduces score slightly
        Error,     // Significant issue, major score reduction
        Critical   // Fatal issue, recommendation should be rejected
    }

    /// <summary>
    /// Types of AI hallucination patterns.
    /// </summary>
    public enum HallucinationPatternType
    {
        NonExistentArtist,
        NonExistentAlbum,
        ImpossibleReleaseDate,
        GenreMismatch,
        NamePatternAnomalies,
        RepetitiveElements,
        SuspiciousCombinations,
        TemporalInconsistencies,
        FormatAnomalies,
        LanguagePatterns
    }
}