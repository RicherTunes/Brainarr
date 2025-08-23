using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Common
{
    /// <summary>
    /// Base recommendation model used by all providers
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

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Artist) && 
                   !string.IsNullOrWhiteSpace(Album);
        }
    }
}