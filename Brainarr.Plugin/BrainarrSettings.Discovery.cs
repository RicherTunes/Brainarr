using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public partial class BrainarrSettings
    {
        // Auto-detect model (show for all providers)
        [FieldDefinition(4, Label = "Auto-Detect Model", Type = FieldType.Checkbox, HelpText = "Automatically detect and select best available model", HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#auto-detect-model")]
        public bool AutoDetectModel { get; set; }

        // Discovery Settings
        [FieldDefinition(5, Label = "Recommendations", Type = FieldType.Number,
            HelpText = "Number of items to return each run (1-50). Brainarr treats this as your target and tops-up iteratively.\nTip: Start with 5-10 and increase if you like the results", HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#recommendations")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(6, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode),
            HelpText = "How adventurous should recommendations be?\n- Similar: Stay close to current taste\n- Adjacent: Explore related genres\n- Exploratory: Discover new genres",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#discovery-mode")]
        public DiscoveryMode DiscoveryMode { get; set; }

        [FieldDefinition(7, Label = "Library Sampling", Type = FieldType.Select, SelectOptions = typeof(SamplingStrategy),
            HelpText = "How much of your library to include in AI prompts\n- Minimal: Fast, less context (good for local models)\n- Balanced: Default, optimal balance\n- Comprehensive: Maximum context (best for GPT-4/Claude)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#library-sampling")]
        public SamplingStrategy SamplingStrategy { get; set; }

        [FieldDefinition(8, Label = "Recommendation Type", Type = FieldType.Select, SelectOptions = typeof(RecommendationMode),
            HelpText = "Control what gets recommended:\n- Specific Albums: Recommend individual albums to import\n- Artists: Recommend artists (Lidarr will import ALL their albums)\n\nTip: Choose 'Artists' for comprehensive library building, 'Specific Albums' for targeted additions",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#recommendation-type")]
        public RecommendationMode RecommendationMode { get; set; }

        [FieldDefinition(9, Label = "Backfill Strategy (Default: Aggressive)", Type = FieldType.Select, SelectOptions = typeof(BackfillStrategy),
            HelpText = "One simple setting to control top-up behavior when under target.\n- Off: No top-up passes (first batch only)\n- Standard: A few passes + initial oversampling\n- Aggressive (Default): More passes, relaxed gating, tries to guarantee target\nNote: Advanced top-up controls are hidden in v1.2.1; strategy is sufficient for most users.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#backfill-strategy")]
        public BackfillStrategy BackfillStrategy { get; set; }

        // Internal: optional hard cap for top-up iterations (no UI field yet)
        public int MaxTopUpIterations { get; set; } = 0;

        // Lidarr Integration (Hidden from UI, set by Lidarr)
        public string BaseUrl
        {
            get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
            set { /* Handled by provider-specific URLs */ }
        }

        // Model Detection Results (populated during test)
        public List<string> DetectedModels { get; set; } = new List<string>();

        // Back-compat: proxy to AutoDetectModel (remove duplicate behavior)
        public bool EnableAutoDetection
        {
            get => AutoDetectModel;
            set => AutoDetectModel = value;
        }

        // Additional missing properties
        public bool EnableFallbackModel { get; set; } = true;
        public string FallbackModel { get; set; } = "qwen2.5:latest";
        public bool EnableLibraryAnalysis { get; set; } = true;
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(BrainarrConstants.MinRefreshIntervalHours);
        [FieldDefinition(17, Label = "Top-Up When Under Target", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "If under target, request additional recommendations with feedback to fill the gap.\nFor local providers (Ollama/LM Studio) this runs by default.",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#iterative-top-up")]
        public bool EnableIterativeRefinement { get; set; } = false;

        // Iteration Hysteresis (Advanced)
        [FieldDefinition(18, Label = "Top-Up Max Iterations", Type = FieldType.Number, Advanced = true,
            HelpText = "Maximum top-up iterations before stopping.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeMaxIterations { get; set; } = 3;

        [FieldDefinition(19, Label = "Zero‑Success Stop After", Type = FieldType.Number, Advanced = true,
            HelpText = "Stop iterative top‑up after this many zero‑unique iterations.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeZeroSuccessStopThreshold { get; set; } = 1;

        [FieldDefinition(20, Label = "Low‑Success Stop After", Type = FieldType.Number, Advanced = true,
            HelpText = "Stop iterative top‑up after this many low‑success iterations (mode‑adjusted).",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeLowSuccessStopThreshold { get; set; } = 2;

        [FieldDefinition(21, Label = "Top‑Up Cooldown (ms)", Type = FieldType.Number, Advanced = true,
            HelpText = "Cooldown (milliseconds) on early stop to reduce churn (local providers).",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeCooldownMs { get; set; } = 1000;

        // Simplified control replacing individual stop thresholds
        [FieldDefinition(23, Label = "Top-Up Stop Sensitivity", Type = FieldType.Select, SelectOptions = typeof(StopSensitivity), Advanced = true,
            HelpText = "Controls how quickly top-up attempts stop: Strict (stop early), Normal, Lenient (default, allow more attempts). Threshold fields apply as minimums.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#iterative-top-up")]
        [JsonConverter(typeof(StopSensitivityJsonConverter))]
        public StopSensitivity TopUpStopSensitivity { get; set; } = StopSensitivity.Lenient;

        // Request timeout for AI provider calls (seconds)
        [FieldDefinition(26, Label = "AI Request Timeout (s)", Type = FieldType.Number,
            HelpText = "Timeout for provider requests in seconds. Increase for slow local LLMs.\nNote: For Ollama/LM Studio, Brainarr uses 360s if this is set near default (≤30s).",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#timeouts")]
        public int AIRequestTimeoutSeconds { get; set; } = BrainarrConstants.DefaultAITimeout;

        // OpenAI-compatible providers
        [FieldDefinition(30, Label = "Prefer Structured JSON (schema)", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Use JSON Schema structured responses when supported (OpenAI/OpenRouter/Groq/DeepSeek/Perplexity). Disable if your gateway has issues with structured outputs.")]
        public bool PreferStructuredJsonForChat { get; set; } = true;

        // LM Studio tuning (advanced)
        [FieldDefinition(28, Label = "LM Studio Temperature", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Sampling temperature for LM Studio (0.0-2.0). Lower is more deterministic; 0.3–0.7 recommended for curation.")]
        public double LMStudioTemperature { get; set; } = 0.5;

        [FieldDefinition(29, Label = "LM Studio Max Tokens", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Maximum tokens to generate for LM Studio responses. Increase if responses get cut off.")]
        public int LMStudioMaxTokens { get; set; } = 2000;

        // Advanced Validation Settings
        [FieldDefinition(24, Label = "Custom Filter Patterns", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Additional patterns to filter out AI hallucinations (comma-separated)\nExample: '(alternate take), (radio mix), (demo version)'\nNote: Be careful not to filter legitimate albums!")]
        public string CustomFilterPatterns { get; set; } = string.Empty;

        [FieldDefinition(10, Label = "Enable Strict Validation", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Apply stricter validation rules to reduce false positives\n- Filters more aggressively\n- May block some legitimate albums")]
        public bool EnableStrictValidation { get; set; }

        [FieldDefinition(11, Label = "Enable Debug Logging", Type = FieldType.Checkbox, Advanced = true,
                    HelpText = "Enable detailed logging for troubleshooting\nWarning: May include sensitive prompt/request/response snippets. Do not enable in production.\nNote: Creates verbose logs")]
        public bool EnableDebugLogging { get; set; }

        [FieldDefinition(25, Label = "Log Per-Item Decisions", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Log each accepted/rejected recommendation.\n- Rejections: Always Info (with reason)\n- Accepted: Info only when Debug Logging is enabled\nDisable to reduce log noise — aggregate summaries remain.\nLearn more: see Troubleshooting guide (link below)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Troubleshooting#reading-brainarr-logs")]
        public bool LogPerItemDecisions { get; set; } = true;

        // Advanced: Concurrency (per-model) overrides for limiter (hidden)
        [FieldDefinition(27, Label = "Max Concurrent Per Model (Cloud)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Override default concurrency per model for cloud providers (OpenAI/Anthropic/OpenRouter/Groq/Perplexity/DeepSeek/Gemini). Leave blank to use defaults.")]
        public int? MaxConcurrentPerModelCloud { get; set; }

        [FieldDefinition(27, Label = "Max Concurrent Per Model (Local)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Override default concurrency per model for local providers (Ollama/LM Studio). Leave blank to use defaults.")]
        public int? MaxConcurrentPerModelLocal { get; set; }

        // Advanced: Adaptive throttling (429) controls (hidden)
        [FieldDefinition(27, Label = "Enable Adaptive Throttling", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Automatically reduce per-model concurrency for a short time when 429 (TooManyRequests) is observed.")]
        public bool EnableAdaptiveThrottling { get; set; } = false;

        [FieldDefinition(27, Label = "Adaptive Throttle Seconds", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "How long to keep reduced concurrency after 429 (seconds). Default 60.")]
        public int AdaptiveThrottleSeconds { get; set; } = 60;

        [FieldDefinition(27, Label = "Adaptive Throttle Cap (Cloud)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Temporary per-model concurrency cap for cloud providers after 429. Default 2.")]
        public int? AdaptiveThrottleCloudCap { get; set; }

        // ===== Music Styles (dynamic TagSelect) =====
        [FieldDefinition(34, Label = "Music Styles", Type = FieldType.TagSelect,
            HelpText = "Select one or more styles (aliases supported). Leave empty to use your library profile.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Styles",
            SelectOptionsProviderAction = "styles/getoptions")]
        public IEnumerable<string> StyleFilters { get; set; } = Array.Empty<string>();

        // Hidden/advanced knobs related to styles & token budgets
        [FieldDefinition(35, Label = "Max Selected Styles", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Soft cap for number of selected styles applied in prompts (default 10). Exceeding selections are trimmed with a log warning.")]
        public int MaxSelectedStyles { get; set; } = 10;

        [FieldDefinition(36, Label = "Comprehensive Token Budget Override", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Optional override for Comprehensive prompt token budget. Leave blank to auto-detect from model.")]
        public int? ComprehensiveTokenBudgetOverride { get; set; }

        [FieldDefinition(37, Label = "Relax Style Matching", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "When enabled, allow parent/adjacent styles as fallback. Default OFF for strict matching.")]
        public bool RelaxStyleMatching { get; set; } = false;

        [FieldDefinition(27, Label = "Adaptive Throttle Cap (Local)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Temporary per-model concurrency cap for local providers after 429. Default 8.")]
        public int? AdaptiveThrottleLocalCap { get; set; }

        // Safety Gates
        [FieldDefinition(12, Label = "Minimum Confidence", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Drop or queue items below this confidence (0.0-1.0)",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public double MinConfidence { get; set; } = 0.7;

        [FieldDefinition(13, Label = "Require MusicBrainz IDs", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Require MBIDs before adding. Items without MBIDs are sent to Review Queue",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public bool RequireMbids { get; set; } = true;

        // Aggressive completion options (Advanced)
        [FieldDefinition(22, Label = "Guarantee Exact Target", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Try harder to return exactly the requested number of items.\nIf still under target after normal top-up, Brainarr will iterate more aggressively and, in Artist mode, may promote name-only artists to fill the gap.",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#guarantee-exact-target")]
        public bool GuaranteeExactTarget { get; set; } = false;

        [FieldDefinition(14, Label = "Queue Borderline Items", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Send low-confidence or missing-MBID items to the Review Queue instead of dropping them",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates",
                    Section = "Review Queue")]
        public bool QueueBorderlineItems { get; set; } = true;

        // Review Queue UI integration
        // Use TagSelect (multi-select with chips) to show options from the provider
        [FieldDefinition(15, Label = "Approve Suggestions", Type = FieldType.TagSelect, Hidden = HiddenType.Hidden,
                    HelpText = "Pick items from your Review Queue to approve.\n• Save to apply on the next sync (or use the 'review/apply' action to apply immediately).\n• After a successful apply, selections auto‑clear and are persisted when supported by your host.",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Review-Queue",
                    Placeholder = "Search or select pending items…",
                    Section = "Review Queue",
                    SelectOptionsProviderAction = "review/getoptions")]
        public IEnumerable<string> ReviewApproveKeys { get; set; } = Array.Empty<string>();

        [FieldDefinition(16, Label = "Review Summary", Type = FieldType.TagSelect, Hidden = HiddenType.Hidden,
                    HelpText = "Quick overview of queue counts (informational)",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Review-Queue",
                    Placeholder = "Pending / Accepted / Rejected / Never…",
                    Section = "Review Queue",
                    SelectOptionsProviderAction = "review/getsummaryoptions")]
        public IEnumerable<string> ReviewSummary { get; set; } = Array.Empty<string>();

        // Observability (hidden preview)
        [FieldDefinition(16, Label = "Observability (Preview)", Type = FieldType.TagSelect, Advanced = true,
                    HelpText = "Compact preview of provider/model latency, errors and throttles.",
                    HelpLink = "observability/html",
                    Placeholder = "provider:model — p95, errors, 429 (last 15m)",
                    Section = "Observability",
                    SelectOptionsProviderAction = "observability/getoptions")]
        public IEnumerable<string> ObservabilityPreview { get; set; } = Array.Empty<string>();

        // Default filters for preview (hidden)
        [FieldDefinition(32, Label = "Observability Provider Filter", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Optional default provider filter for Observability preview (e.g., 'openai', 'ollama').")]
        public string ObservabilityProviderFilter { get; set; }

        [FieldDefinition(33, Label = "Observability Model Filter", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Optional default model filter for Observability preview (e.g., 'gpt-4o-mini').")]
        public string ObservabilityModelFilter { get; set; }

        // Feature flag: quickly disable Observability UI without code changes (hidden)
        [FieldDefinition(31, Label = "Enable Observability Preview", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Toggle the Observability (Preview) UI and endpoints without redeploying.")]
        public bool EnableObservabilityPreview { get; set; } = true;
    }
}
