using System;

namespace Brainarr.Plugin.Models
{
    /// <summary>
    /// Compatibility shim for legacy code - use provider-specific models instead
    /// </summary>
    [Obsolete("Use provider-specific models in Brainarr.Plugin.Models.Providers namespace")]
    public static class ProviderResponses
    {
        // Re-export common models for backward compatibility
        public class RecommendationItem : Common.RecommendationItem { }
        
        // Re-export provider models for backward compatibility
        public class OpenAIResponse : Providers.OpenAIResponse { }
        public class OpenAIChoice : Providers.OpenAIChoice { }
        public class OpenAIMessage : Providers.OpenAIMessage { }
        public class OpenAIUsage : Providers.OpenAIUsage { }
        
        public class AnthropicResponse : Providers.AnthropicResponse { }
        public class AnthropicContent : Providers.AnthropicContent { }
        public class AnthropicUsage : Providers.AnthropicUsage { }
        
        public class GeminiResponse : Providers.GeminiResponse { }
        public class GeminiCandidate : Providers.GeminiCandidate { }
        public class GeminiContent : Providers.GeminiContent { }
        public class GeminiPart : Providers.GeminiPart { }
        public class GeminiSafetyRating : Providers.GeminiSafetyRating { }
        public class GeminiPromptFeedback : Providers.GeminiPromptFeedback { }
        
        public class OllamaResponse : Providers.OllamaResponse { }
        public class LMStudioResponse : Providers.LMStudioResponse { }
        public class AzureOpenAIResponse : Providers.AzureOpenAIResponse { }
        public class GroqResponse : Providers.GroqResponse { }
        public class GroqMetadata : Providers.GroqMetadata { }
        public class GroqUsage : Providers.GroqUsage { }
        
        // Re-export external models for backward compatibility
        public class MusicBrainzResponse : External.MusicBrainzResponse { }
        public class MusicBrainzArtist : External.MusicBrainzArtist { }
        public class MusicBrainzArea : External.MusicBrainzArea { }
        public class MusicBrainzLifeSpan : External.MusicBrainzLifeSpan { }
        public class MusicBrainzRecording : External.MusicBrainzRecording { }
        public class MusicBrainzArtistCredit : External.MusicBrainzArtistCredit { }
        public class MusicBrainzRelease : External.MusicBrainzRelease { }
        public class MusicBrainzReleaseGroup : External.MusicBrainzReleaseGroup { }
        public class MusicBrainzReleaseEvent : External.MusicBrainzReleaseEvent { }
        public class MusicBrainzMedia : External.MusicBrainzMedia { }
    }
}