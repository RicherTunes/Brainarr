using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Responses.Base
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

    /// <summary>
    /// Base response interface for all AI providers
    /// </summary>
    public interface IProviderResponse
    {
        string GetContent();
        bool IsSuccessful();
        int? GetTokenUsage();
    }

    /// <summary>
    /// Common error response structure
    /// </summary>
    public class ErrorResponse
    {
        [JsonPropertyName("error")]
        public ErrorDetail Error { get; set; }

        public class ErrorDetail
        {
            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("code")]
            public string Code { get; set; }
        }
    }
}