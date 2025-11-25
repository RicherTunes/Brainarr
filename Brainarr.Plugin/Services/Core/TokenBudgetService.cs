using System;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ITokenBudgetService
    {
        int GetLimit(BrainarrSettings settings);
    }

    public class TokenBudgetService : ITokenBudgetService
    {
        private readonly Logger _logger;

        public TokenBudgetService(Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public int GetLimit(BrainarrSettings settings)
        {
            if (settings == null) return 20000;
            if (settings.ComprehensiveTokenBudgetOverride.HasValue)
            {
                return Math.Max(1000, settings.ComprehensiveTokenBudgetOverride.Value);
            }

            var strategy = settings.SamplingStrategy;
            var provider = settings.Provider;
            var model = settings.ModelSelection ?? string.Empty;

            // Base defaults
            var minimal = 3000;
            var balanced = 6000;
            var comprehensive = 20000;

            int MapBase()
            {
                return strategy switch
                {
                    SamplingStrategy.Minimal => minimal,
                    SamplingStrategy.Balanced => balanced,
                    SamplingStrategy.Comprehensive => comprehensive,
                    _ => balanced
                };
            }

            // Local providers get multipliers
            if (provider == AIProvider.Ollama || provider == AIProvider.LMStudio)
            {
                var m = strategy switch
                {
                    SamplingStrategy.Minimal => 1.4,
                    SamplingStrategy.Balanced => 1.6,
                    SamplingStrategy.Comprehensive => 2.0,
                    _ => 1.6
                };
                return (int)(MapBase() * m);
            }

            // Cloud/gateways: model-aware ceilings for Comprehensive
            if (strategy == SamplingStrategy.Comprehensive)
            {
                var lowerModel = model.ToLowerInvariant();
                // OpenAI 4o-mini variants: conservative ~64k
                if (lowerModel.Contains("gpt-4o-mini") || lowerModel.Contains("4o-mini") || lowerModel.Contains("gpt-4o"))
                {
                    return 64000;
                }
                // Anthropic Claude 3.7 family
                if (lowerModel.Contains("claude-3.7") || lowerModel.Contains("claude-3-7"))
                {
                    return 120000;
                }
                // Llama-3.1-70B typical contexts
                if (lowerModel.Contains("llama-3.1-70b"))
                {
                    return 32000;
                }
                // Qwen/Gemini/DeepSeek default conservative
                if (lowerModel.Contains("qwen") || lowerModel.Contains("gemini") || lowerModel.Contains("deepseek"))
                {
                    return 32000;
                }
            }

            return MapBase();
        }
    }
}
