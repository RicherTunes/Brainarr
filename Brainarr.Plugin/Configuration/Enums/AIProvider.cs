namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Enums
{
    /// <summary>
    /// Available AI providers for music recommendation generation
    /// </summary>
    public enum AIProvider
    {
        // Local providers first (privacy-focused)
        Ollama = 0,       // Local, 100% private
        LMStudio = 1,     // Local with GUI
        
        // Gateway for flexibility
        OpenRouter = 5,   // Access 200+ models
        
        // Cost-effective options
        DeepSeek = 6,     // 10-20x cheaper
        Gemini = 7,       // Free tier available
        Groq = 8,         // Ultra-fast inference
        
        // Premium cloud options
        Perplexity = 2,   // Web-enhanced
        OpenAI = 3,       // GPT-4 quality
        Anthropic = 4     // Best reasoning
    }
}