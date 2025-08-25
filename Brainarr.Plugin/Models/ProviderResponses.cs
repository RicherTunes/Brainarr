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
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("object")]
            public string Object { get; set; } = string.Empty;

            [JsonPropertyName("created")]
            public long Created { get; set; }

            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("choices")]
            public List<OpenAIChoice> Choices { get; set; } = new List<OpenAIChoice>();

            [JsonPropertyName("usage")]
            public OpenAIUsage Usage { get; set; } = new OpenAIUsage();
        }

        public class OpenAIChoice
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("message")]
            public OpenAIMessage Message { get; set; } = new OpenAIMessage();

            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; } = string.Empty;
        }

        public class OpenAIMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
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
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public List<AnthropicContent> Content { get; set; } = new List<AnthropicContent>();

            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("stop_reason")]
            public string StopReason { get; set; } = string.Empty;

            [JsonPropertyName("stop_sequence")]
            public string StopSequence { get; set; } = string.Empty;

            [JsonPropertyName("usage")]
            public AnthropicUsage Usage { get; set; } = new AnthropicUsage();
        }

        public class AnthropicContent
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
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
            public List<GeminiCandidate> Candidates { get; set; } = new List<GeminiCandidate>();

            [JsonPropertyName("promptFeedback")]
            public GeminiPromptFeedback PromptFeedback { get; set; } = new GeminiPromptFeedback();
        }

        public class GeminiCandidate
        {
            [JsonPropertyName("content")]
            public GeminiContent Content { get; set; } = new GeminiContent();

            [JsonPropertyName("finishReason")]
            public string FinishReason { get; set; } = string.Empty;

            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("safetyRatings")]
            public List<GeminiSafetyRating> SafetyRatings { get; set; } = new List<GeminiSafetyRating>();
        }

        public class GeminiContent
        {
            [JsonPropertyName("parts")]
            public List<GeminiPart> Parts { get; set; } = new List<GeminiPart>();

            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;
        }

        public class GeminiPart
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }

        public class GeminiSafetyRating
        {
            [JsonPropertyName("category")]
            public string Category { get; set; } = string.Empty;

            [JsonPropertyName("probability")]
            public string Probability { get; set; } = string.Empty;
        }

        public class GeminiPromptFeedback
        {
            [JsonPropertyName("safetyRatings")]
            public List<GeminiSafetyRating> SafetyRatings { get; set; } = new List<GeminiSafetyRating>();
        }

        /// <summary>
        /// Ollama response format
        /// </summary>
        public class OllamaResponse
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("created_at")]
            public string CreatedAt { get; set; } = string.Empty;

            [JsonPropertyName("response")]
            public string Response { get; set; } = string.Empty;

            [JsonPropertyName("done")]
            public bool Done { get; set; }

            [JsonPropertyName("context")]
            public List<int> Context { get; set; } = new List<int>();

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
            public string SystemFingerprint { get; set; } = string.Empty;
        }

        /// <summary>
        /// Cohere response format
        /// </summary>
        public class CohereResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;

            [JsonPropertyName("generation_id")]
            public string GenerationId { get; set; } = string.Empty;

            [JsonPropertyName("chat_history")]
            public List<CohereChatMessage> ChatHistory { get; set; } = new List<CohereChatMessage>();

            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; } = string.Empty;

            [JsonPropertyName("meta")]
            public CohereMeta Meta { get; set; } = new CohereMeta();
        }

        public class CohereChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;
        }

        public class CohereMeta
        {
            [JsonPropertyName("api_version")]
            public CohereApiVersion ApiVersion { get; set; } = new CohereApiVersion();

            [JsonPropertyName("billed_units")]
            public CohereBilledUnits BilledUnits { get; set; } = new CohereBilledUnits();

            [JsonPropertyName("tokens")]
            public CohereTokens Tokens { get; set; } = new CohereTokens();
        }

        public class CohereApiVersion
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;
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
            public string GeneratedText { get; set; } = string.Empty;

            // Alternative format for some models
            [JsonPropertyName("summary_text")]
            public string SummaryText { get; set; } = string.Empty;

            // For models that return arrays
            [JsonPropertyName("outputs")]
            public List<string> Outputs { get; set; } = new List<string>();
        }

        /// <summary>
        /// Groq response format
        /// </summary>
        public class GroqResponse : OpenAIResponse
        {
            // Groq uses OpenAI-compatible format
            [JsonPropertyName("x_groq")]
            public GroqMetadata XGroq { get; set; } = new GroqMetadata();
        }

        public class GroqMetadata
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("usage")]
            public GroqUsage Usage { get; set; } = new GroqUsage();
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
            public string Created { get; set; } = string.Empty;

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("offset")]
            public int Offset { get; set; }

            [JsonPropertyName("artists")]
            public List<MusicBrainzArtist> Artists { get; set; } = new List<MusicBrainzArtist>();

            [JsonPropertyName("recordings")]
            public List<MusicBrainzRecording> Recordings { get; set; } = new List<MusicBrainzRecording>();

            [JsonPropertyName("releases")]
            public List<MusicBrainzRelease> Releases { get; set; } = new List<MusicBrainzRelease>();
        }

        public class MusicBrainzArtist
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("score")]
            public int Score { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("sort-name")]
            public string SortName { get; set; } = string.Empty;

            [JsonPropertyName("country")]
            public string Country { get; set; } = string.Empty;

            [JsonPropertyName("area")]
            public MusicBrainzArea Area { get; set; } = new MusicBrainzArea();

            [JsonPropertyName("begin-area")]
            public MusicBrainzArea BeginArea { get; set; } = new MusicBrainzArea();

            [JsonPropertyName("disambiguation")]
            public string Disambiguation { get; set; } = string.Empty;

            [JsonPropertyName("life-span")]
            public MusicBrainzLifeSpan LifeSpan { get; set; } = new MusicBrainzLifeSpan();
        }

        public class MusicBrainzArea
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("sort-name")]
            public string SortName { get; set; } = string.Empty;
        }

        public class MusicBrainzLifeSpan
        {
            [JsonPropertyName("begin")]
            public string Begin { get; set; } = string.Empty;

            [JsonPropertyName("end")]
            public string End { get; set; } = string.Empty;

            [JsonPropertyName("ended")]
            public bool Ended { get; set; }
        }

        public class MusicBrainzRecording
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("score")]
            public int Score { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;

            [JsonPropertyName("length")]
            public int? Length { get; set; }

            [JsonPropertyName("video")]
            public bool Video { get; set; }

            [JsonPropertyName("artist-credit")]
            public List<MusicBrainzArtistCredit> ArtistCredit { get; set; } = new List<MusicBrainzArtistCredit>();

            [JsonPropertyName("releases")]
            public List<MusicBrainzRelease> Releases { get; set; } = new List<MusicBrainzRelease>();
        }

        public class MusicBrainzArtistCredit
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("artist")]
            public MusicBrainzArtist Artist { get; set; } = new MusicBrainzArtist();
        }

        public class MusicBrainzRelease
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("score")]
            public int? Score { get; set; }

            [JsonPropertyName("status-id")]
            public string StatusId { get; set; } = string.Empty;

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;

            [JsonPropertyName("status")]
            public string Status { get; set; } = string.Empty;

            [JsonPropertyName("artist-credit")]
            public List<MusicBrainzArtistCredit> ArtistCredit { get; set; } = new List<MusicBrainzArtistCredit>();

            [JsonPropertyName("release-group")]
            public MusicBrainzReleaseGroup ReleaseGroup { get; set; } = new MusicBrainzReleaseGroup();

            [JsonPropertyName("date")]
            public string Date { get; set; } = string.Empty;

            [JsonPropertyName("country")]
            public string Country { get; set; } = string.Empty;

            [JsonPropertyName("release-events")]
            public List<MusicBrainzReleaseEvent> ReleaseEvents { get; set; } = new List<MusicBrainzReleaseEvent>();

            [JsonPropertyName("barcode")]
            public string Barcode { get; set; } = string.Empty;

            [JsonPropertyName("track-count")]
            public int TrackCount { get; set; }

            [JsonPropertyName("media")]
            public List<MusicBrainzMedia> Media { get; set; } = new List<MusicBrainzMedia>();
        }

        public class MusicBrainzReleaseGroup
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("type-id")]
            public string TypeId { get; set; } = string.Empty;

            [JsonPropertyName("primary-type-id")]
            public string PrimaryTypeId { get; set; } = string.Empty;

            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;

            [JsonPropertyName("primary-type")]
            public string PrimaryType { get; set; } = string.Empty;
        }

        public class MusicBrainzReleaseEvent
        {
            [JsonPropertyName("date")]
            public string Date { get; set; } = string.Empty;

            [JsonPropertyName("area")]
            public MusicBrainzArea Area { get; set; } = new MusicBrainzArea();
        }

        public class MusicBrainzMedia
        {
            [JsonPropertyName("position")]
            public int Position { get; set; }

            [JsonPropertyName("format-id")]
            public string FormatId { get; set; } = string.Empty;

            [JsonPropertyName("format")]
            public string Format { get; set; } = string.Empty;

            [JsonPropertyName("track-count")]
            public int TrackCount { get; set; }

            [JsonPropertyName("track-offset")]
            public int TrackOffset { get; set; }
        }
    }
}