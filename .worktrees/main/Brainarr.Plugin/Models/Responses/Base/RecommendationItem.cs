using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses
{
    /// <summary>
    /// Base recommendation model used by all providers.
    /// Represents a single music recommendation with validation.
    /// </summary>
    public class RecommendationItem
    {
        [JsonPropertyName("artist")]
        public string Artist { get; set; } = string.Empty;

        [JsonPropertyName("album")]
        public string Album { get; set; } = string.Empty;

        [JsonPropertyName("genre")]
        public string Genre { get; set; } = string.Empty;

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        /// <summary>
        /// Validates that the recommendation has minimum required fields
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Artist) &&
                   !string.IsNullOrWhiteSpace(Album);
        }

        /// <summary>
        /// Normalizes confidence score to 0-1 range
        /// </summary>
        public double GetNormalizedConfidence()
        {
            if (!Confidence.HasValue)
                return 0.5; // Default confidence

            if (Confidence.Value <= 0)
                return 0;

            if (Confidence.Value >= 1)
                return 1;

            return Confidence.Value;
        }

        /// <summary>
        /// Validates year is within reasonable bounds
        /// </summary>
        public bool HasValidYear()
        {
            return Year.HasValue && Year.Value >= 1900 && Year.Value <= 2100;
        }
    }
}
