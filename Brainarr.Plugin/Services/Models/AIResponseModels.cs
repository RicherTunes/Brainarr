using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Models
{
    /// <summary>
    /// Shared response models for AI providers to eliminate duplication
    /// </summary>
    
    /// <summary>
    /// Standard OpenAI-style response format (used by OpenAI, DeepSeek, Groq, etc.)
    /// </summary>
    public class OpenAIStyleResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("object")]
        public string Object { get; set; }
        
        [JsonProperty("created")]
        public long Created { get; set; }
        
        [JsonProperty("model")]
        public string Model { get; set; }
        
        [JsonProperty("choices")]
        public List<Choice> Choices { get; set; }
        
        [JsonProperty("usage")]
        public Usage Usage { get; set; }
    }
    
    public class Choice
    {
        [JsonProperty("index")]
        public int Index { get; set; }
        
        [JsonProperty("message")]
        public Message Message { get; set; }
        
        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; }
    }
    
    public class Message
    {
        [JsonProperty("role")]
        public string Role { get; set; }
        
        [JsonProperty("content")]
        public string Content { get; set; }
    }
    
    public class Usage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }
        
        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }
        
        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }
    
    /// <summary>
    /// Anthropic/Claude response format
    /// </summary>
    public class AnthropicResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("role")]
        public string Role { get; set; }
        
        [JsonProperty("content")]
        public List<ContentBlock> Content { get; set; }
        
        [JsonProperty("model")]
        public string Model { get; set; }
        
        [JsonProperty("usage")]
        public Usage Usage { get; set; }
    }
    
    public class ContentBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("text")]
        public string Text { get; set; }
    }
    
    /// <summary>
    /// Google Gemini response format
    /// </summary>
    public class GeminiResponse
    {
        [JsonProperty("candidates")]
        public List<Candidate> Candidates { get; set; }
        
        [JsonProperty("promptFeedback")]
        public PromptFeedback PromptFeedback { get; set; }
    }
    
    public class Candidate
    {
        [JsonProperty("content")]
        public GeminiContent Content { get; set; }
        
        [JsonProperty("finishReason")]
        public string FinishReason { get; set; }
        
        [JsonProperty("index")]
        public int Index { get; set; }
    }
    
    public class GeminiContent
    {
        [JsonProperty("parts")]
        public List<Part> Parts { get; set; }
        
        [JsonProperty("role")]
        public string Role { get; set; }
    }
    
    public class Part
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
    
    public class PromptFeedback
    {
        [JsonProperty("safetyRatings")]
        public List<SafetyRating> SafetyRatings { get; set; }
    }
    
    public class SafetyRating
    {
        [JsonProperty("category")]
        public string Category { get; set; }
        
        [JsonProperty("probability")]
        public string Probability { get; set; }
    }
    
    /// <summary>
    /// Error response format (common across providers)
    /// </summary>
    public class ErrorResponse
    {
        [JsonProperty("error")]
        public ErrorDetail Error { get; set; }
    }
    
    public class ErrorDetail
    {
        [JsonProperty("message")]
        public string Message { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("code")]
        public string Code { get; set; }
    }
}