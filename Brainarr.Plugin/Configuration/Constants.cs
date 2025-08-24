namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class BrainarrConstants
    {
        // Default URLs - Using localhost for better deployment flexibility
        // Users can override these in the UI settings
        public const string DefaultOllamaUrl = "http://localhost:11434";
        public const string DefaultLMStudioUrl = "http://localhost:1234";
        
        // Default models
        public const string DefaultOllamaModel = "qwen2.5:latest";
        public const string DefaultLMStudioModel = "local-model";
        
        // Limits
        public const int MinRecommendations = 1;
        public const int MaxRecommendations = 50;
        public const int DefaultRecommendations = 20;
        
        // Timeouts (in seconds)
        public const int DefaultAITimeout = 30;
        public const int MaxAITimeout = 120;
        public const int ModelDetectionTimeout = 10;
        public const int TestConnectionTimeout = 10;
        
        // Retry Policy
        public const int MaxRetryAttempts = 3;
        public const int InitialRetryDelayMs = 1000;
        public const int MaxRetryDelayMs = 30000;
        
        // Rate Limiting (per provider)
        public const int RequestsPerMinute = 10;
        public const int BurstSize = 5;
        
        // Health Monitoring
        public const int HealthCheckTimeoutMs = 5000;
        public const double UnhealthyThreshold = 0.5; // 50% failure rate
        public const int HealthCheckWindowMinutes = 5;
        
        // Caching
        public const int CacheDurationMinutes = 60;
        public const int MaxCacheEntries = 100;
        
        // Async Operations
        public const int DefaultAsyncTimeoutMs = 120000; // 2 minutes
        
        // Import List Settings
        public const int MinRefreshIntervalHours = 6;
        
        // Common genres (fallback when real data unavailable)
        public static readonly string[] FallbackGenres = new[]
        {
            "Rock", "Electronic", "Pop", "Jazz", "Classical", 
            "Hip Hop", "R&B", "Country", "Folk", "Metal"
        };
    }
}