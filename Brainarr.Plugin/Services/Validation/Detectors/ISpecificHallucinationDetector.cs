using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Detectors
{
    /// <summary>
    /// Base interface for specific hallucination detection strategies
    /// </summary>
    public interface ISpecificHallucinationDetector
    {
        /// <summary>
        /// The type of pattern this detector identifies
        /// </summary>
        HallucinationPatternType PatternType { get; }

        /// <summary>
        /// Detects specific hallucination patterns in a recommendation
        /// </summary>
        Task<HallucinationPattern> DetectAsync(Recommendation recommendation);

        /// <summary>
        /// Priority of this detector (higher = runs first)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Whether this detector is enabled
        /// </summary>
        bool IsEnabled { get; }
    }

    /// <summary>
    /// Result from a specific hallucination detector
    /// </summary>
    public class HallucinationPattern
    {
        public HallucinationPatternType PatternType { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string Evidence { get; set; } = string.Empty;
        public bool IsConfirmedHallucination { get; set; }
    }

    /// <summary>
    /// Types of hallucination patterns
    /// </summary>
    public enum HallucinationPatternType
    {
        NonExistentArtist,
        NonExistentAlbum,
        ImpossibleReleaseDate,
        NamePatternAnomaly,
        RepetitiveElements,
        SuspiciousCombination,
        TemporalInconsistency,
        FormatAnomaly,
        LanguagePattern,
        GenreInconsistency,
        MetadataConflict
    }
}