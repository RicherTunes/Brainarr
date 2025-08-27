using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Cost
{
    /// <summary>
    /// Provides token counting and cost estimation for AI provider API calls.
    /// Helps users understand and control their AI provider expenses.
    /// </summary>
    public class TokenCostEstimator : ITokenCostEstimator
    {
        private readonly Logger _logger;
        private readonly Dictionary<AIProvider, ProviderPricing> _pricingData;
        
        public TokenCostEstimator(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pricingData = InitializePricingData();
        }
        
        /// <summary>
        /// Estimates the number of tokens in a text string.
        /// Uses GPT-3/4 tokenization approximation (1 token ≈ 4 characters or 0.75 words).
        /// </summary>
        public int EstimateTokenCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            
            // More accurate tokenization based on OpenAI's tiktoken rules
            // This is an approximation - actual tokenization varies by model
            
            // Count words and characters
            var words = Regex.Matches(text, @"\b\w+\b").Count;
            var characters = text.Length;
            
            // Use hybrid approach: average of word-based and character-based estimates
            var wordBasedEstimate = (int)(words / 0.75);
            var charBasedEstimate = (int)(characters / 4.0);
            
            // Weight character-based slightly higher as it's more consistent
            var estimate = (int)((wordBasedEstimate * 0.4) + (charBasedEstimate * 0.6));
            
            // Add overhead for special tokens (start, end, etc.)
            estimate += 3;
            
            return estimate;
        }
        
        /// <summary>
        /// Calculates the estimated cost for a prompt and expected response.
        /// </summary>
        public CostEstimate EstimateCost(
            AIProvider provider, 
            string model, 
            string prompt, 
            int expectedResponseTokens = 500)
        {
            var promptTokens = EstimateTokenCount(prompt);
            
            if (!_pricingData.TryGetValue(provider, out var pricing))
            {
                _logger.Warn($"No pricing data available for provider {provider}");
                return new CostEstimate 
                { 
                    Provider = provider,
                    Model = model,
                    PromptTokens = promptTokens,
                    ResponseTokens = expectedResponseTokens,
                    EstimatedCost = 0,
                    CostBreakdown = "Pricing data not available"
                };
            }
            
            var modelPricing = GetModelPricing(pricing, model);
            
            var promptCost = (promptTokens / 1000.0m) * modelPricing.InputPricePer1K;
            var responseCost = (expectedResponseTokens / 1000.0m) * modelPricing.OutputPricePer1K;
            var totalCost = promptCost + responseCost;
            
            return new CostEstimate
            {
                Provider = provider,
                Model = model,
                PromptTokens = promptTokens,
                ResponseTokens = expectedResponseTokens,
                EstimatedCost = totalCost,
                CostBreakdown = $"Input: ${promptCost:F6} ({promptTokens} tokens) + " +
                               $"Output: ${responseCost:F6} ({expectedResponseTokens} tokens)",
                PricePerMillionTokens = modelPricing.InputPricePer1K * 1000,
                Currency = "USD"
            };
        }
        
        /// <summary>
        /// Tracks actual usage and updates cost estimates based on real response sizes.
        /// </summary>
        public UsageReport TrackUsage(
            AIProvider provider,
            string model,
            string prompt,
            string response,
            TimeSpan duration)
        {
            var promptTokens = EstimateTokenCount(prompt);
            var responseTokens = EstimateTokenCount(response);
            
            var estimate = EstimateCost(provider, model, prompt, responseTokens);
            
            var report = new UsageReport
            {
                Provider = provider,
                Model = model,
                Timestamp = DateTime.UtcNow,
                PromptTokens = promptTokens,
                ResponseTokens = responseTokens,
                TotalTokens = promptTokens + responseTokens,
                EstimatedCost = estimate.EstimatedCost,
                Duration = duration,
                TokensPerSecond = responseTokens / Math.Max(1, duration.TotalSeconds)
            };
            
            // Store in usage history for reporting
            StoreUsageReport(report);
            
            return report;
        }
        
        /// <summary>
        /// Gets usage statistics for a time period.
        /// </summary>
        public UsageStatistics GetUsageStatistics(DateTime startDate, DateTime endDate)
        {
            var reports = GetStoredReports(startDate, endDate);
            
            if (!reports.Any())
            {
                return new UsageStatistics
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    TotalCost = 0,
                    TotalTokens = 0
                };
            }
            
            return new UsageStatistics
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalCost = reports.Sum(r => r.EstimatedCost),
                TotalTokens = reports.Sum(r => r.TotalTokens),
                TotalRequests = reports.Count,
                AverageTokensPerRequest = reports.Average(r => r.TotalTokens),
                AverageCostPerRequest = reports.Average(r => r.EstimatedCost),
                ProviderBreakdown = reports
                    .GroupBy(r => r.Provider)
                    .Select(g => new ProviderUsage
                    {
                        Provider = g.Key,
                        TotalCost = g.Sum(r => r.EstimatedCost),
                        TotalTokens = g.Sum(r => r.TotalTokens),
                        RequestCount = g.Count()
                    })
                    .ToList(),
                PeakUsageHour = reports
                    .GroupBy(r => r.Timestamp.Hour)
                    .OrderByDescending(g => g.Count())
                    .First().Key
            };
        }
        
        /// <summary>
        /// Checks if usage is approaching budget limits.
        /// </summary>
        public BudgetAlert CheckBudget(decimal monthlyBudget)
        {
            var currentMonth = DateTime.UtcNow;
            var startOfMonth = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            var stats = GetUsageStatistics(startOfMonth, DateTime.UtcNow);
            
            var percentUsed = (stats.TotalCost / monthlyBudget) * 100;
            var daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
            var daysElapsed = (DateTime.UtcNow - startOfMonth).Days + 1;
            var expectedUsage = (daysElapsed / (decimal)daysInMonth) * monthlyBudget;
            var projectedMonthlyTotal = (stats.TotalCost / daysElapsed) * daysInMonth;
            
            var alertLevel = percentUsed switch
            {
                >= 90 => AlertLevel.Critical,
                >= 75 => AlertLevel.Warning,
                >= 50 => AlertLevel.Info,
                _ => AlertLevel.None
            };
            
            return new BudgetAlert
            {
                MonthlyBudget = monthlyBudget,
                CurrentSpend = stats.TotalCost,
                PercentUsed = percentUsed,
                ProjectedMonthlyTotal = projectedMonthlyTotal,
                DaysRemaining = daysInMonth - daysElapsed,
                AlertLevel = alertLevel,
                Message = GenerateBudgetMessage(percentUsed, projectedMonthlyTotal, monthlyBudget),
                RecommendedDailyLimit = (monthlyBudget - stats.TotalCost) / Math.Max(1, daysInMonth - daysElapsed)
            };
        }
        
        private Dictionary<AIProvider, ProviderPricing> InitializePricingData()
        {
            // Pricing as of 2024 - should be configurable/updatable
            return new Dictionary<AIProvider, ProviderPricing>
            {
                [AIProvider.OpenAI] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["gpt-4-turbo"] = new ModelPricing { InputPricePer1K = 0.01m, OutputPricePer1K = 0.03m },
                        ["gpt-4"] = new ModelPricing { InputPricePer1K = 0.03m, OutputPricePer1K = 0.06m },
                        ["gpt-3.5-turbo"] = new ModelPricing { InputPricePer1K = 0.0005m, OutputPricePer1K = 0.0015m }
                    }
                },
                [AIProvider.Anthropic] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["claude-3-opus"] = new ModelPricing { InputPricePer1K = 0.015m, OutputPricePer1K = 0.075m },
                        ["claude-3-sonnet"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["claude-3-haiku"] = new ModelPricing { InputPricePer1K = 0.00025m, OutputPricePer1K = 0.00125m }
                    }
                },
                [AIProvider.Gemini] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["gemini-pro"] = new ModelPricing { InputPricePer1K = 0.00025m, OutputPricePer1K = 0.0005m },
                        ["gemini-ultra"] = new ModelPricing { InputPricePer1K = 0.007m, OutputPricePer1K = 0.021m }
                    }
                },
                [AIProvider.Perplexity] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["llama-3-sonar-large"] = new ModelPricing { InputPricePer1K = 0.001m, OutputPricePer1K = 0.001m },
                        ["llama-3-sonar-small"] = new ModelPricing { InputPricePer1K = 0.0002m, OutputPricePer1K = 0.0002m }
                    }
                },
                [AIProvider.Groq] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["mixtral-8x7b"] = new ModelPricing { InputPricePer1K = 0.00027m, OutputPricePer1K = 0.00027m },
                        ["llama2-70b"] = new ModelPricing { InputPricePer1K = 0.00064m, OutputPricePer1K = 0.00064m }
                    }
                },
                [AIProvider.DeepSeek] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["deepseek-chat"] = new ModelPricing { InputPricePer1K = 0.00014m, OutputPricePer1K = 0.00028m }
                    }
                },
                [AIProvider.OpenRouter] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0.001m, OutputPricePer1K = 0.001m }
                    }
                },
                // Local providers have no API costs
                [AIProvider.Ollama] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                },
                [AIProvider.LMStudio] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                }
            };
        }
        
        private ModelPricing GetModelPricing(ProviderPricing provider, string model)
        {
            // Try exact match first
            if (provider.Models.TryGetValue(model, out var pricing))
                return pricing;
            
            // Try to find partial match (e.g., "gpt-4-turbo-preview" matches "gpt-4-turbo")
            var partialMatch = provider.Models
                .Where(kvp => model.Contains(kvp.Key) || kvp.Key.Contains(model))
                .OrderByDescending(kvp => kvp.Key.Length)
                .FirstOrDefault();
            
            if (partialMatch.Value != null)
                return partialMatch.Value;
            
            // Return default pricing if available
            return provider.Models.GetValueOrDefault("default") ?? 
                   new ModelPricing { InputPricePer1K = 0.001m, OutputPricePer1K = 0.001m };
        }
        
        private void StoreUsageReport(UsageReport report)
        {
            // In production, this would store to database or file
            // For now, we'll use in-memory storage
            UsageHistory.Add(report);
            
            // Keep only last 30 days
            var cutoff = DateTime.UtcNow.AddDays(-30);
            UsageHistory.RemoveAll(r => r.Timestamp < cutoff);
        }
        
        private List<UsageReport> GetStoredReports(DateTime startDate, DateTime endDate)
        {
            return UsageHistory
                .Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate)
                .ToList();
        }
        
        private string GenerateBudgetMessage(decimal percentUsed, decimal projected, decimal budget)
        {
            if (percentUsed >= 90)
                return $"⚠️ CRITICAL: {percentUsed:F1}% of monthly budget used! Projected to exceed by ${projected - budget:F2}";
            if (percentUsed >= 75)
                return $"⚠️ Warning: {percentUsed:F1}% of monthly budget used. Monitor closely.";
            if (percentUsed >= 50)
                return $"ℹ️ Info: {percentUsed:F1}% of monthly budget used. On track.";
            
            return $"✅ Budget healthy: {percentUsed:F1}% used.";
        }
        
        // In-memory storage for demo - replace with persistent storage
        private static readonly List<UsageReport> UsageHistory = new List<UsageReport>();
        
        private class ProviderPricing
        {
            public Dictionary<string, ModelPricing> Models { get; set; }
        }
        
        private class ModelPricing
        {
            public decimal InputPricePer1K { get; set; }
            public decimal OutputPricePer1K { get; set; }
        }
    }
    
    public interface ITokenCostEstimator
    {
        int EstimateTokenCount(string text);
        CostEstimate EstimateCost(AIProvider provider, string model, string prompt, int expectedResponseTokens = 500);
        UsageReport TrackUsage(AIProvider provider, string model, string prompt, string response, TimeSpan duration);
        UsageStatistics GetUsageStatistics(DateTime startDate, DateTime endDate);
        BudgetAlert CheckBudget(decimal monthlyBudget);
    }
    
    public class CostEstimate
    {
        public AIProvider Provider { get; set; }
        public string Model { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int ResponseTokens { get; set; }
        public decimal EstimatedCost { get; set; }
        public string CostBreakdown { get; set; } = string.Empty;
        public decimal PricePerMillionTokens { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
    
    public class UsageReport
    {
        public AIProvider Provider { get; set; }
        public string Model { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int PromptTokens { get; set; }
        public int ResponseTokens { get; set; }
        public int TotalTokens { get; set; }
        public decimal EstimatedCost { get; set; }
        public TimeSpan Duration { get; set; }
        public double TokensPerSecond { get; set; }
    }
    
    public class UsageStatistics
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalCost { get; set; }
        public int TotalTokens { get; set; }
        public int TotalRequests { get; set; }
        public double AverageTokensPerRequest { get; set; }
        public decimal AverageCostPerRequest { get; set; }
        public List<ProviderUsage> ProviderBreakdown { get; set; } = new List<ProviderUsage>();
        public int PeakUsageHour { get; set; }
    }
    
    public class ProviderUsage
    {
        public AIProvider Provider { get; set; }
        public decimal TotalCost { get; set; }
        public int TotalTokens { get; set; }
        public int RequestCount { get; set; }
    }
    
    public class BudgetAlert
    {
        public decimal MonthlyBudget { get; set; }
        public decimal CurrentSpend { get; set; }
        public decimal PercentUsed { get; set; }
        public decimal ProjectedMonthlyTotal { get; set; }
        public int DaysRemaining { get; set; }
        public AlertLevel AlertLevel { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal RecommendedDailyLimit { get; set; }
    }
    
    public enum AlertLevel
    {
        None,
        Info,
        Warning,
        Critical
    }
}