using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models
{
    /// <summary>
    /// Strongly typed models for AI provider responses to prevent deserialization attacks
    /// </summary>
    public static class ProviderResponses
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

            // Validation method
            public bool IsValid()
            {
                return !string.IsNullOrWhiteSpace(Artist) && 
                       !string.IsNullOrWhiteSpace(Album);
            }
        }

        /// <summary>
        /// OpenAI/GPT response format
        /// </summary>
        public class OpenAIResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("object")]
            public string Object { get; set; }

            [JsonPropertyName("created")]
            public long Created { get; set; }

            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("choices")]
            public List<OpenAIChoice> Choices { get; set; }

            [JsonPropertyName("usage")]
            public OpenAIUsage Usage { get; set; }
        }

        public class OpenAIChoice
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("message")]
            public OpenAIMessage Message { get; set; }

            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; }
        }

        public class OpenAIMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        public class OpenAIUsage
        {
            [JsonPropertyName("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonPropertyName("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }
        }

        /// <summary>
        /// Anthropic Claude response format
        /// </summary>
        public class AnthropicResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public List<AnthropicContent> Content { get; set; }

            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("stop_reason")]
            public string StopReason { get; set; }

            [JsonPropertyName("stop_sequence")]
            public string StopSequence { get; set; }

            [JsonPropertyName("usage")]
            public AnthropicUsage Usage { get; set; }
        }

        public class AnthropicContent
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        public class AnthropicUsage
        {
            [JsonPropertyName("input_tokens")]
            public int InputTokens { get; set; }

            [JsonPropertyName("output_tokens")]
            public int OutputTokens { get; set; }
        }

        /// <summary>
        /// Google Gemini response format
        /// </summary>
        public class GeminiResponse
        {
            [JsonPropertyName("candidates")]
            public List<GeminiCandidate> Candidates { get; set; }

            [JsonPropertyName("promptFeedback")]
            public GeminiPromptFeedback PromptFeedback { get; set; }
        }

        public class GeminiCandidate
        {
            [JsonPropertyName("content")]
            public GeminiContent Content { get; set; }

            [JsonPropertyName("finishReason")]
            public string FinishReason { get; set; }

            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("safetyRatings")]
            public List<GeminiSafetyRating> SafetyRatings { get; set; }
        }

        public class GeminiContent
        {
            [JsonPropertyName("parts")]
            public List<GeminiPart> Parts { get; set; }

            [JsonPropertyName("role")]
            public string Role { get; set; }
        }

        public class GeminiPart
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        public class GeminiSafetyRating
        {
            [JsonPropertyName("category")]
            public string Category { get; set; }

            [JsonPropertyName("probability")]
            public string Probability { get; set; }
        }

        public class GeminiPromptFeedback
        {
            [JsonPropertyName("safetyRatings")]
            public List<GeminiSafetyRating> SafetyRatings { get; set; }
        }

        /// <summary>
        /// Ollama response format
        /// </summary>
        public class OllamaResponse
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("created_at")]
            public string CreatedAt { get; set; }

            [JsonPropertyName("response")]
            public string Response { get; set; }

            [JsonPropertyName("done")]
            public bool Done { get; set; }

            [JsonPropertyName("context")]
            public List<int> Context { get; set; }

            [JsonPropertyName("total_duration")]
            public long TotalDuration { get; set; }

            [JsonPropertyName("load_duration")]
            public long LoadDuration { get; set; }

            [JsonPropertyName("prompt_eval_count")]
            public int PromptEvalCount { get; set; }

            [JsonPropertyName("eval_count")]
            public int EvalCount { get; set; }

            [JsonPropertyName("eval_duration")]
            public long EvalDuration { get; set; }
        }

        /// <summary>
        /// LM Studio response format
        /// </summary>
        public class LMStudioResponse : OpenAIResponse
        {
            // LM Studio uses OpenAI-compatible format
        }

        /// <summary>
        /// Azure OpenAI response format
        /// </summary>
        public class AzureOpenAIResponse : OpenAIResponse
        {
            // Azure OpenAI uses OpenAI-compatible format with additional fields
            [JsonPropertyName("system_fingerprint")]
            public string SystemFingerprint { get; set; }
        }

        /// <summary>
        /// Cohere response format
        /// </summary>
        public class CohereResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; }

            [JsonPropertyName("generation_id")]
            public string GenerationId { get; set; }

            [JsonPropertyName("chat_history")]
            public List<CohereChatMessage> ChatHistory { get; set; }

            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; }

            [JsonPropertyName("meta")]
            public CohereMeta Meta { get; set; }
        }

        public class CohereChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }
        }

        public class CohereMeta
        {
            [JsonPropertyName("api_version")]
            public CohereApiVersion ApiVersion { get; set; }

            [JsonPropertyName("billed_units")]
            public CohereBilledUnits BilledUnits { get; set; }

            [JsonPropertyName("tokens")]
            public CohereTokens Tokens { get; set; }
        }

        public class CohereApiVersion
        {
            [JsonPropertyName("version")]
            public string Version { get; set; }
        }

        public class CohereBilledUnits
        {
            [JsonPropertyName("input_tokens")]
            public int InputTokens { get; set; }

            [JsonPropertyName("output_tokens")]
            public int OutputTokens { get; set; }
        }

        public class CohereTokens
        {
            [JsonPropertyName("input_tokens")]
            public int InputTokens { get; set; }

            [JsonPropertyName("output_tokens")]
            public int OutputTokens { get; set; }
        }

        /// <summary>
        /// Hugging Face response format
        /// </summary>
        public class HuggingFaceResponse
        {
            [JsonPropertyName("generated_text")]
            public string GeneratedText { get; set; }

            // Alternative format for some models
            [JsonPropertyName("summary_text")]
            public string SummaryText { get; set; }

            // For models that return arrays
            [JsonPropertyName("outputs")]
            public List<string> Outputs { get; set; }
        }

        /// <summary>
        /// Groq response format
        /// </summary>
        public class GroqResponse : OpenAIResponse
        {
            // Groq uses OpenAI-compatible format
            [JsonPropertyName("x_groq")]
            public GroqMetadata XGroq { get; set; }
        }

        public class GroqMetadata
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("usage")]
            public GroqUsage Usage { get; set; }
        }

        public class GroqUsage
        {
            [JsonPropertyName("queue_time")]
            public double QueueTime { get; set; }

            [JsonPropertyName("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonPropertyName("prompt_time")]
            public double PromptTime { get; set; }

            [JsonPropertyName("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonPropertyName("completion_time")]
            public double CompletionTime { get; set; }

            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }

            [JsonPropertyName("total_time")]
            public double TotalTime { get; set; }
        }

        /// <summary>
        /// MusicBrainz API response format
        /// </summary>
        public class MusicBrainzResponse
        {
            [JsonPropertyName("created")]
            public string Created { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("offset")]
            public int Offset { get; set; }

            [JsonPropertyName("artists")]
            public List<MusicBrainzArtist> Artists { get; set; }

            [JsonPropertyName("recordings")]
            public List<MusicBrainzRecording> Recordings { get; set; }

            [JsonPropertyName("releases")]
            public List<MusicBrainzRelease> Releases { get; set; }
        }

        public class MusicBrainzArtist
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("score")]
            public int Score { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("sort-name")]
            public string SortName { get; set; }

            [JsonPropertyName("country")]
            public string Country { get; set; }

            [JsonPropertyName("area")]
            public MusicBrainzArea Area { get; set; }

            [JsonPropertyName("begin-area")]
            public MusicBrainzArea BeginArea { get; set; }

            [JsonPropertyName("disambiguation")]
            public string Disambiguation { get; set; }

            [JsonPropertyName("life-span")]
            public MusicBrainzLifeSpan LifeSpan { get; set; }
        }

        public class MusicBrainzArea
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("sort-name")]
            public string SortName { get; set; }
        }

        public class MusicBrainzLifeSpan
        {
            [JsonPropertyName("begin")]
            public string Begin { get; set; }

            [JsonPropertyName("end")]
            public string End { get; set; }

            [JsonPropertyName("ended")]
            public bool Ended { get; set; }
        }

        public class MusicBrainzRecording
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("score")]
            public int Score { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("length")]
            public int? Length { get; set; }

            [JsonPropertyName("video")]
            public bool Video { get; set; }

            [JsonPropertyName("artist-credit")]
            public List<MusicBrainzArtistCredit> ArtistCredit { get; set; }

            [JsonPropertyName("releases")]
            public List<MusicBrainzRelease> Releases { get; set; }
        }

        public class MusicBrainzArtistCredit
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("artist")]
            public MusicBrainzArtist Artist { get; set; }
        }

        public class MusicBrainzRelease
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("score")]
            public int? Score { get; set; }

            [JsonPropertyName("status-id")]
            public string StatusId { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("artist-credit")]
            public List<MusicBrainzArtistCredit> ArtistCredit { get; set; }

            [JsonPropertyName("release-group")]
            public MusicBrainzReleaseGroup ReleaseGroup { get; set; }

            [JsonPropertyName("date")]
            public string Date { get; set; }

            [JsonPropertyName("country")]
            public string Country { get; set; }

            [JsonPropertyName("release-events")]
            public List<MusicBrainzReleaseEvent> ReleaseEvents { get; set; }

            [JsonPropertyName("barcode")]
            public string Barcode { get; set; }

            [JsonPropertyName("track-count")]
            public int TrackCount { get; set; }

            [JsonPropertyName("media")]
            public List<MusicBrainzMedia> Media { get; set; }
        }

        public class MusicBrainzReleaseGroup
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("type-id")]
            public string TypeId { get; set; }

            [JsonPropertyName("primary-type-id")]
            public string PrimaryTypeId { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("primary-type")]
            public string PrimaryType { get; set; }
        }

        public class MusicBrainzReleaseEvent
        {
            [JsonPropertyName("date")]
            public string Date { get; set; }

            [JsonPropertyName("area")]
            public MusicBrainzArea Area { get; set; }
        }

        public class MusicBrainzMedia
        {
            [JsonPropertyName("position")]
            public int Position { get; set; }

            [JsonPropertyName("format-id")]
            public string FormatId { get; set; }

            [JsonPropertyName("format")]
            public string Format { get; set; }

            [JsonPropertyName("track-count")]
            public int TrackCount { get; set; }

            [JsonPropertyName("track-offset")]
            public int TrackOffset { get; set; }
        }
    }
}