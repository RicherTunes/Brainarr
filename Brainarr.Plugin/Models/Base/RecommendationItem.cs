using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Base
{
    /// <summary>
    /// Base recommendation model used by all AI providers
    /// </summary>
    public class RecommendationItem
    {
        [JsonPropertyName("artist")]
        public string Artist { get; set; }

        [JsonPropertyName("album")]
        public string Album { get; set; }

        [JsonPropertyName("genre")]
        public string Genre { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }

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
            if (!Confidence.HasValue) return 0.5;
            return Math.Max(0, Math.Min(1, Confidence.Value));
        }
    }
}