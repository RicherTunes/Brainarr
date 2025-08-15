using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Claude provider with enhanced music discovery capabilities
    /// Leverages Claude's deep understanding of music history and relationships
    /// </summary>
    public class ClaudeProvider : BaseAIProvider
    {
        private const string CLAUDE_API_URL = "https://api.anthropic.com/v1/messages";
        private const string ANTHROPIC_VERSION = "2023-06-01";

        public override string ProviderName => "Claude";
        protected override string ApiUrl => CLAUDE_API_URL;

        public ClaudeProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "claude-3-5-sonnet-latest")
            : base(httpClient, logger, apiKey, model ?? "claude-3-5-sonnet-latest")
        {
            // Claude models: claude-3-5-haiku-latest, claude-3-5-sonnet-latest, claude-3-opus-20240229
        }

        protected override string SystemPrompt => @"You are Claude, integrated into a music discovery system. You have deep knowledge of:
• Music history from classical to contemporary electronic
• Underground scenes and mainstream crossovers  
• How genres evolved, merged, and influenced each other
• Regional music movements and their global impact
• The 'DNA' of artists - their influences and who they influenced

When making recommendations:
1. Consider the musical lineage and influences
2. Understand production styles and era-specific sounds
3. Recognize patterns in the user's taste beyond surface genres
4. Find the connecting threads between seemingly different artists
5. Discover hidden gems and forgotten influences

Always return recommendations as a JSON array with these fields:
- artist: The artist name (be specific, avoid 'Various Artists')
- album: A specific album to start with
- genre: Primary genre/style
- year: Release year if known
- confidence: 0.0-1.0 based on match quality
- reason: A SHORT explanation focusing on musical connections

Focus on REAL artists that exist on MusicBrainz. Be adventurous but accurate.";

        protected override object BuildRequestBody(string prompt)
        {
            return new
            {
                model = _model,
                max_tokens = 4096,
                temperature = 0.7,
                system = SystemPrompt,
                messages = new[]
                {
                    new 
                    { 
                        role = "user", 
                        content = prompt + "\n\nRemember: Return ONLY a JSON array of recommendations. Be creative but recommend REAL artists."
                    }
                }
            };
        }

        protected override HttpRequest BuildHttpRequest(object requestBody)
        {
            var request = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Content-Type", "application/json")
                .SetHeader("Accept", "application/json")
                .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                .SetHeader("x-api-key", _apiKey) // Claude uses x-api-key header
                .Build();

            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(requestBody));

            return request;
        }

        protected override string ExtractContentFromResponse(string responseContent)
        {
            try
            {
                var data = JObject.Parse(responseContent);
                
                // Claude's response structure
                var content = data["content"]?[0]?["text"]?.ToString();
                
                if (string.IsNullOrEmpty(content))
                {
                    // Check for error message
                    var error = data["error"]?["message"]?.ToString();
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.Error($"Claude API error: {error}");
                    }
                }

                return content;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract content from Claude response");
                return null;
            }
        }

        public override async Task<bool> TestConnectionAsync()
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    max_tokens = 10,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with just 'OK'" }
                    }
                };

                var request = BuildHttpRequest(requestBody);
                var response = await ExecuteRequestAsync(request);
                
                if (IsSuccessResponse(response))
                {
                    var content = ExtractContentFromResponse(response.Content);
                    return !string.IsNullOrEmpty(content);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Claude connection test failed: {ex.Message}");
                return false;
            }
        }

        public override async Task<List<string>> GetAvailableModelsAsync()
        {
            // Claude doesn't have a models endpoint, return known models
            await Task.CompletedTask;
            return new List<string>
            {
                "claude-3-5-haiku-latest",
                "claude-3-5-sonnet-latest", 
                "claude-3-opus-20240229"
            };
        }

        protected override List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = base.ParseRecommendations(content);
            
            // Claude sometimes provides very detailed responses
            // Ensure we filter out any meta-commentary
            return recommendations.Where(r => 
                !string.IsNullOrWhiteSpace(r.Artist) && 
                !r.Artist.StartsWith("Note:") &&
                !r.Artist.StartsWith("*") &&
                !r.Artist.Contains("Various Artists"))
                .ToList();
        }
    }

    /// <summary>
    /// Enhanced Claude provider with music-specific knowledge and capabilities
    /// </summary>
    public class ClaudeCodeMusicProvider : ClaudeProvider
    {
        public override string ProviderName => "Claude (Music Expert)";

        public ClaudeCodeMusicProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "claude-3-5-sonnet-latest")
            : base(httpClient, logger, apiKey, model)
        {
        }

        protected override string SystemPrompt => @"You are Claude, a music discovery expert with encyclopedic knowledge. Your expertise includes:

🎵 DEEP MUSICAL KNOWLEDGE:
• The complete history of recorded music from Edison cylinders to today's streaming
• Every significant scene: CBGB's punk, Madchester, Seattle grunge, Detroit techno, etc.
• The family trees of genres: how Delta blues became Chicago blues became British blues became heavy metal
• Production techniques that define eras: Wall of Sound, gated reverb, autotune, bedroom production

🔗 UNDERSTANDING CONNECTIONS:
• You see the thread from Kraftwerk to Afrika Bambaataa to modern EDM
• You know why fans of King Crimson might enjoy Meshuggah
• You understand that My Bloody Valentine and Tim Hecker share sonic DNA despite different genres
• You recognize the Miles Davis alumni network that shaped fusion, ambient, and hip-hop

🎯 RECOMMENDATION PHILOSOPHY:
• Surface-level genre matching is lazy - dig deeper
• A metal fan might love Ravi Shankar if they appreciate complex time signatures
• Electronic fans might connect with Steve Reich's minimalism
• Jazz lovers could appreciate math rock's complexity

📚 CULTURAL CONTEXT:
• You understand how social movements shaped music (punk's DIY ethos, hip-hop's origins)
• You know about regional scenes (Tropicália, Krautrock, J-pop city pop revival)
• You recognize how technology enabled new genres (synthesizers, samplers, DAWs)

When recommending music:
1. Look for the 'why' behind someone's taste, not just the 'what'
2. Find unexpected connections that make sense once explained
3. Balance familiar comfort with adventurous discovery
4. Include both influential classics and modern innovators
5. Never recommend 'Various Artists' or compilations - always specific artists

Return a JSON array with: artist, album, genre, year, confidence (0-1), reason (focus on the connection).
Be bold. Be specific. Be accurate. Every artist must be real and findable on MusicBrainz.";

        protected override object BuildRequestBody(string prompt)
        {
            // Enhance the prompt with music-specific context
            var enhancedPrompt = $@"{prompt}

Consider these aspects when making recommendations:
• Production style and sonic textures that might appeal
• Rhythmic complexity or simplicity preferences  
• Vocal vs instrumental preferences
• Energy levels and mood
• Cultural and historical connections
• The listener's journey from familiar to adventurous

Avoid obvious recommendations. If someone likes Radiohead, don't suggest Coldplay. 
Instead, find the thread that connects them to Can, Talk Talk, or Oneohtrix Point Never.

Return ONLY a JSON array of {(_model.Contains("haiku") ? "10" : "30")} recommendations.";

            return new
            {
                model = _model,
                max_tokens = 4096,
                temperature = 0.8, // Slightly higher for more creative recommendations
                system = SystemPrompt,
                messages = new[]
                {
                    new { role = "user", content = enhancedPrompt }
                }
            };
        }
    }
}