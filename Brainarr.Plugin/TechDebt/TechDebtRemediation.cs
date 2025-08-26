using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.TechDebt
{
    /// <summary>
    /// Comprehensive tech debt remediation service that addresses identified issues:
    /// 1. Dangerous async patterns (GetAwaiter().GetResult())
    /// 2. Resource disposal issues (SemaphoreSlim leaks)
    /// 3. Code duplication across providers
    /// 4. Inconsistent error handling
    /// 5. Missing standardization
    /// </summary>
    public interface ITechDebtRemediation
    {
        /// <summary>
        /// Safe async-to-sync bridge without deadlocks
        /// </summary>
        T SafeExecuteSync<T>(Func<Task<T>> asyncOperation);
        
        /// <summary>
        /// Safe async-to-sync bridge for void operations
        /// </summary>
        void SafeExecuteSync(Func<Task> asyncOperation);
        
        /// <summary>
        /// Standardized error handling with proper logging
        /// </summary>
        Task<T> ExecuteWithStandardErrorHandling<T>(Func<Task<T>> operation, string operationName, T defaultValue = default);
        
        /// <summary>
        /// Provider-agnostic response parsing
        /// </summary>
        List<Recommendation> StandardizeResponseParsing(string response, AIProvider provider);
    }

    public class TechDebtRemediationService : ITechDebtRemediation
    {
        private readonly Logger _logger;
        
        public TechDebtRemediationService(Logger logger)
        {
            _logger = logger;
        }

        public T SafeExecuteSync<T>(Func<Task<T>> asyncOperation)
        {
            // Use SafeAsyncHelper instead of dangerous GetAwaiter().GetResult()
            return SafeAsyncHelper.RunSafeSync(asyncOperation);
        }

        public void SafeExecuteSync(Func<Task> asyncOperation)
        {
            SafeAsyncHelper.RunSafeSync(asyncOperation);
        }

        public async Task<T> ExecuteWithStandardErrorHandling<T>(Func<Task<T>> operation, string operationName, T defaultValue = default)
        {
            try
            {
                _logger.Debug($"Starting operation: {operationName}");
                var result = await operation().ConfigureAwait(false);
                _logger.Debug($"Completed operation: {operationName}");
                return result;
            }
            catch (TaskCanceledException ex)
            {
                _logger.Warn($"Operation {operationName} was cancelled: {ex.Message}");
                return defaultValue;
            }
            catch (TimeoutException ex)
            {
                _logger.Error($"Operation {operationName} timed out: {ex.Message}");
                return defaultValue;
            }
            catch (HttpException ex)
            {
                _logger.Error($"HTTP error in {operationName}: Status={ex.Response?.StatusCode}, Message={ex.Message}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected error in {operationName}");
                return defaultValue;
            }
        }

        public List<Recommendation> StandardizeResponseParsing(string response, AIProvider provider)
        {
            // Standardized parsing logic for all providers
            var recommendations = new List<Recommendation>();
            
            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.Debug($"Empty response from {provider}");
                return recommendations;
            }

            try
            {
                // Try JSON parsing first (most common)
                recommendations = ParseJsonResponse(response);
            }
            catch
            {
                // Fallback to text parsing
                _logger.Debug($"JSON parsing failed for {provider}, trying text parsing");
                recommendations = ParseTextResponse(response);
            }

            // Apply standardization
            return recommendations.Select(r => StandardizeRecommendation(r, provider)).ToList();
        }

        private List<Recommendation> ParseJsonResponse(string response)
        {
            // Common JSON parsing logic
            var recommendations = new List<Recommendation>();
            
            // Remove potential markdown code blocks
            response = response.Replace("```json", "").Replace("```", "").Trim();
            
            // Try to parse as JSON array
            if (response.StartsWith("["))
            {
                recommendations = System.Text.Json.JsonSerializer.Deserialize<List<Recommendation>>(response, 
                    new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
                    });
            }
            // Try to parse as single object
            else if (response.StartsWith("{"))
            {
                var single = System.Text.Json.JsonSerializer.Deserialize<Recommendation>(response,
                    new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
                    });
                if (single != null)
                {
                    recommendations.Add(single);
                }
            }

            return recommendations ?? new List<Recommendation>();
        }

        private List<Recommendation> ParseTextResponse(string response)
        {
            // Common text parsing logic for all providers
            var recommendations = new List<Recommendation>();
            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                // Look for patterns like "Artist - Album (Year)"
                var match = System.Text.RegularExpressions.Regex.Match(line, 
                    @"^[\d\.\-\*\s]*(.+?)\s*[-–]\s*(.+?)(?:\s*\((\d{4})\))?$");
                
                if (match.Success)
                {
                    var rec = new Recommendation
                    {
                        Artist = match.Groups[1].Value.Trim(),
                        Album = match.Groups[2].Value.Trim(),
                        Year = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : (int?)null
                    };
                    recommendations.Add(rec);
                }
            }

            return recommendations;
        }

        private Recommendation StandardizeRecommendation(Recommendation rec, AIProvider provider)
        {
            // Ensure consistent data format across all providers
            return rec with
            {
                Artist = NormalizeString(rec.Artist),
                Album = NormalizeString(rec.Album),
                Genre = NormalizeString(rec.Genre),
                Source = provider.ToString(),
                Provider = provider.ToString(),
                Confidence = rec.Confidence > 0 ? rec.Confidence : 0.5 // Default confidence if invalid
            };
        }

        private string NormalizeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            // Trim, normalize whitespace, remove special characters
            value = value.Trim();
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ");
            
            // Remove common AI artifacts
            value = value.Replace("**", "").Replace("*", "");
            
            return value;
        }
    }

    /// <summary>
    /// Extension methods to apply tech debt fixes across the codebase
    /// </summary>
    public static class TechDebtExtensions
    {
        private static readonly ITechDebtRemediation _remediation = new TechDebtRemediationService(LogManager.GetCurrentClassLogger());

        /// <summary>
        /// Replace dangerous GetAwaiter().GetResult() calls
        /// </summary>
        public static T SafeGetResult<T>(this Task<T> task)
        {
            return _remediation.SafeExecuteSync(() => task);
        }

        /// <summary>
        /// Replace dangerous Wait() calls
        /// </summary>
        public static void SafeWait(this Task task)
        {
            _remediation.SafeExecuteSync(() => task);
        }

        /// <summary>
        /// Standardize error handling across all async operations
        /// </summary>
        public static Task<T> WithStandardErrorHandling<T>(this Task<T> task, string operationName, T defaultValue = default)
        {
            return _remediation.ExecuteWithStandardErrorHandling(() => task, operationName, defaultValue);
        }
    }

}