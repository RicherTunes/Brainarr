namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support;

internal static class MetricsNames
{
    public const string PromptPlanCacheHit = "prompt.plan_cache_hit";
    public const string PromptPlanCacheMiss = "prompt.plan_cache_miss";
    public const string CacheHit = "cache.hit";
    public const string CacheMiss = "cache.miss";
    public const string CacheEviction = "cache.eviction";
    public const string PromptPlanCacheEvict = "prompt.plan_cache_evict";
    public const string PromptPlanCacheSize = "prompt.plan_cache_size";
    public const string PromptActualTokens = "prompt.actual_tokens";
    public const string PromptTokensPre = "prompt.tokens_pre";
    public const string PromptTokensPost = "prompt.tokens_post";
    public const string PromptCompressionRatio = "prompt.compression_ratio";
    public const string PromptHeadroomViolation = "prompt.headroom_violation";
    public const string TokenizerFallback = "tokenizer.fallback";
}
