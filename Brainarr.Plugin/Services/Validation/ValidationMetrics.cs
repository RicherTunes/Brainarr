using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Interface for collecting and analyzing validation metrics.
    /// </summary>
    public interface IValidationMetrics
    {
        void RecordValidationResult(ValidationResult result, string providerName);
        void RecordBatchValidationResult(List<ValidationResult> results, string providerName);
        ValidationMetricsReport GenerateReport(TimeSpan? timeWindow = null);
        void Reset();
    }

    /// <summary>
    /// Comprehensive metrics collector for recommendation validation system.
    /// </summary>
    public class ValidationMetrics : IValidationMetrics
    {
        private readonly object _lockObject = new object();
        private readonly List<ValidationRecord> _validationHistory = new List<ValidationRecord>();

        public void RecordValidationResult(ValidationResult result, string providerName)
        {
            lock (_lockObject)
            {
                _validationHistory.Add(new ValidationRecord
                {
                    Timestamp = DateTime.UtcNow,
                    ProviderName = providerName,
                    ValidationResult = result,
                    WasBatchValidation = false
                });

                // Keep only last 10,000 records to prevent memory issues
                if (_validationHistory.Count > 10000)
                {
                    _validationHistory.RemoveRange(0, 1000);
                }
            }
        }

        public void RecordBatchValidationResult(List<ValidationResult> results, string providerName)
        {
            lock (_lockObject)
            {
                var timestamp = DateTime.UtcNow;

                foreach (var result in results)
                {
                    _validationHistory.Add(new ValidationRecord
                    {
                        Timestamp = timestamp,
                        ProviderName = providerName,
                        ValidationResult = result,
                        WasBatchValidation = true
                    });
                }
            }
        }

        public ValidationMetricsReport GenerateReport(TimeSpan? timeWindow = null)
        {
            lock (_lockObject)
            {
                var cutoffTime = timeWindow.HasValue
                    ? DateTime.UtcNow - timeWindow.Value
                    : DateTime.MinValue;

                var relevantRecords = _validationHistory
                    .Where(r => r.Timestamp >= cutoffTime)
                    .ToList();

                if (!relevantRecords.Any())
                {
                    return new ValidationMetricsReport
                    {
                        TimeWindow = timeWindow ?? TimeSpan.FromDays(365),
                        TotalValidations = 0
                    };
                }

                return GenerateReportFromRecords(relevantRecords, timeWindow);
            }
        }

        public void Reset()
        {
            lock (_lockObject)
            {
                _validationHistory.Clear();
            }
        }

        private ValidationMetricsReport GenerateReportFromRecords(List<ValidationRecord> records, TimeSpan? timeWindow)
        {
            var report = new ValidationMetricsReport
            {
                TimeWindow = timeWindow ?? TimeSpan.FromDays(365),
                TotalValidations = records.Count,
                ValidRecommendations = records.Count(r => r.ValidationResult.IsValid),
                InvalidRecommendations = records.Count(r => !r.ValidationResult.IsValid),
                AverageValidationScore = records.Average(r => r.ValidationResult.Score),
                AverageValidationTimeMs = records.Average(r => r.ValidationResult.Metadata.ValidationTimeMs)
            };

            // Calculate validation rate
            report.ValidationRate = report.TotalValidations > 0
                ? (double)report.ValidRecommendations / report.TotalValidations
                : 0.0;

            // Provider-specific metrics
            report.ProviderMetrics = records
                .GroupBy(r => r.ProviderName)
                .ToDictionary(g => g.Key, g => new ProviderValidationMetrics
                {
                    TotalValidations = g.Count(),
                    ValidRecommendations = g.Count(r => r.ValidationResult.IsValid),
                    AverageScore = g.Average(r => r.ValidationResult.Score),
                    AverageValidationTimeMs = g.Average(r => r.ValidationResult.Metadata.ValidationTimeMs),
                    MostCommonFailureReasons = GetTopFailureReasons(g.Where(r => !r.ValidationResult.IsValid).ToList())
                });

            // Validation check statistics
            report.CheckTypeStatistics = GenerateCheckTypeStatistics(records);

            // Failure reason analysis
            report.TopFailureReasons = GetTopFailureReasons(records.Where(r => !r.ValidationResult.IsValid).ToList());

            // Hallucination detection statistics
            report.HallucinationStats = GenerateHallucinationStatistics(records);

            // Duplicate detection statistics
            report.DuplicateStats = GenerateDuplicateStatistics(records);

            // Time-based analysis
            report.TimeBasedAnalysis = GenerateTimeBasedAnalysis(records);

            return report;
        }

        private Dictionary<ValidationCheckType, CheckTypeMetrics> GenerateCheckTypeStatistics(List<ValidationRecord> records)
        {
            var checkStats = new Dictionary<ValidationCheckType, CheckTypeMetrics>();

            var allFindings = records.SelectMany(r => r.ValidationResult.Findings).ToList();
            var findingsByType = allFindings.GroupBy(f => f.CheckType);

            foreach (var group in findingsByType)
            {
                var checkType = group.Key;
                var findings = group.ToList();

                checkStats[checkType] = new CheckTypeMetrics
                {
                    TotalChecks = findings.Count,
                    FailedChecks = findings.Count(f => f.Severity == ValidationSeverity.Critical || f.Severity == ValidationSeverity.Error),
                    AverageScoreImpact = findings.Average(f => f.ScoreImpact),
                    MostCommonSeverity = findings
                        .GroupBy(f => f.Severity)
                        .OrderByDescending(g => g.Count())
                        .First().Key
                };
            }

            return checkStats;
        }

        private List<FailureReason> GetTopFailureReasons(List<ValidationRecord> failedRecords, int limit = 10)
        {
            var reasonCounts = new Dictionary<string, int>();

            foreach (var record in failedRecords)
            {
                foreach (var finding in record.ValidationResult.Findings)
                {
                    if (finding.Severity == ValidationSeverity.Critical || finding.Severity == ValidationSeverity.Error)
                    {
                        var key = $"{finding.CheckType}: {finding.Message}";
                        reasonCounts[key] = (reasonCounts.ContainsKey(key) ? reasonCounts[key] : 0) + 1;
                    }
                }
            }

            return reasonCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(limit)
                .Select(kvp => new FailureReason
                {
                    Reason = kvp.Key,
                    Count = kvp.Value,
                    Percentage = (double)kvp.Value / failedRecords.Count * 100
                })
                .ToList();
        }

        private HallucinationStatistics GenerateHallucinationStatistics(List<ValidationRecord> records)
        {
            var hallucinationFindings = records
                .SelectMany(r => r.ValidationResult.Findings)
                .Where(f => f.CheckType == ValidationCheckType.HallucinationDetection)
                .ToList();

            var totalHallucinations = hallucinationFindings.Count;
            var avgConfidence = hallucinationFindings.Any()
                ? hallucinationFindings.Average(f =>
                    {
                        if (f.Context?.ContainsKey("HallucinationConfidence") == true &&
                            f.Context["HallucinationConfidence"] is double confidence)
                            return confidence;
                        return 0.0;
                    })
                : 0.0;

            // Extract pattern types from context
            var patternTypes = new Dictionary<string, int>();
            foreach (var finding in hallucinationFindings)
            {
                if (finding.Context?.ContainsKey("DetectedPatterns") == true)
                {
                    if (finding.Context["DetectedPatterns"] is List<string> patterns)
                    {
                        foreach (var pattern in patterns)
                        {
                            patternTypes[pattern] = (patternTypes.ContainsKey(pattern) ? patternTypes[pattern] : 0) + 1;
                        }
                    }
                }
            }

            return new HallucinationStatistics
            {
                TotalDetected = totalHallucinations,
                AverageConfidence = avgConfidence,
                MostCommonPatterns = patternTypes
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
        }

        private DuplicateStatistics GenerateDuplicateStatistics(List<ValidationRecord> records)
        {
            var duplicateFindings = records
                .SelectMany(r => r.ValidationResult.Findings)
                .Where(f => f.CheckType == ValidationCheckType.DuplicateDetection)
                .ToList();

            return new DuplicateStatistics
            {
                TotalDuplicatesDetected = duplicateFindings.Count,
                DuplicateRate = records.Any() ? (double)duplicateFindings.Count / records.Count : 0.0
            };
        }

        private TimeBasedAnalysis GenerateTimeBasedAnalysis(List<ValidationRecord> records)
        {
            if (!records.Any()) return new TimeBasedAnalysis();

            var now = DateTime.UtcNow;
            var hourlyBuckets = new Dictionary<int, List<ValidationRecord>>();

            // Group by hour of day
            foreach (var record in records)
            {
                var hour = record.Timestamp.Hour;
                if (!hourlyBuckets.ContainsKey(hour))
                    hourlyBuckets[hour] = new List<ValidationRecord>();
                hourlyBuckets[hour].Add(record);
            }

            var peakHour = hourlyBuckets.OrderByDescending(kvp => kvp.Value.Count).First();

            return new TimeBasedAnalysis
            {
                PeakValidationHour = peakHour.Key,
                PeakHourValidationCount = peakHour.Value.Count,
                ValidationsLast24Hours = records.Count(r => r.Timestamp > now.AddDays(-1)),
                ValidationsLastWeek = records.Count(r => r.Timestamp > now.AddDays(-7)),
                TrendDirection = CalculateTrend(records)
            };
        }

        private string CalculateTrend(List<ValidationRecord> records)
        {
            if (records.Count < 10) return "Insufficient data";

            var now = DateTime.UtcNow;
            var recentHalf = records.Where(r => r.Timestamp > now.AddHours(-12)).Count();
            var previousHalf = records.Where(r => r.Timestamp > now.AddDays(-1) && r.Timestamp <= now.AddHours(-12)).Count();

            if (recentHalf > previousHalf * 1.2) return "Increasing";
            if (recentHalf < previousHalf * 0.8) return "Decreasing";
            return "Stable";
        }
    }

    /// <summary>
    /// Individual validation record for metrics tracking.
    /// </summary>
    public class ValidationRecord
    {
        public DateTime Timestamp { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public ValidationResult ValidationResult { get; set; } = new ValidationResult();
        public bool WasBatchValidation { get; set; }
    }

    /// <summary>
    /// Comprehensive validation metrics report.
    /// </summary>
    public class ValidationMetricsReport
    {
        public TimeSpan TimeWindow { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        // Overall metrics
        public int TotalValidations { get; set; }
        public int ValidRecommendations { get; set; }
        public int InvalidRecommendations { get; set; }
        public double ValidationRate { get; set; }
        public double AverageValidationScore { get; set; }
        public double AverageValidationTimeMs { get; set; }

        // Provider-specific metrics
        public Dictionary<string, ProviderValidationMetrics> ProviderMetrics { get; set; }
            = new Dictionary<string, ProviderValidationMetrics>();

        // Check type statistics
        public Dictionary<ValidationCheckType, CheckTypeMetrics> CheckTypeStatistics { get; set; }
            = new Dictionary<ValidationCheckType, CheckTypeMetrics>();

        // Failure analysis
        public List<FailureReason> TopFailureReasons { get; set; } = new List<FailureReason>();

        // Specialized statistics
        public HallucinationStatistics HallucinationStats { get; set; } = new HallucinationStatistics();
        public DuplicateStatistics DuplicateStats { get; set; } = new DuplicateStatistics();
        public TimeBasedAnalysis TimeBasedAnalysis { get; set; } = new TimeBasedAnalysis();
    }

    /// <summary>
    /// Provider-specific validation metrics.
    /// </summary>
    public class ProviderValidationMetrics
    {
        public int TotalValidations { get; set; }
        public int ValidRecommendations { get; set; }
        public double AverageScore { get; set; }
        public double AverageValidationTimeMs { get; set; }
        public List<FailureReason> MostCommonFailureReasons { get; set; } = new List<FailureReason>();

        public double ValidationRate => TotalValidations > 0
            ? (double)ValidRecommendations / TotalValidations
            : 0.0;
    }

    /// <summary>
    /// Metrics for individual validation check types.
    /// </summary>
    public class CheckTypeMetrics
    {
        public int TotalChecks { get; set; }
        public int FailedChecks { get; set; }
        public double AverageScoreImpact { get; set; }
        public ValidationSeverity MostCommonSeverity { get; set; }

        public double FailureRate => TotalChecks > 0 ? (double)FailedChecks / TotalChecks : 0.0;
    }

    /// <summary>
    /// Information about a specific failure reason.
    /// </summary>
    public class FailureReason
    {
        public string Reason { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    /// <summary>
    /// Statistics about AI hallucination detection.
    /// </summary>
    public class HallucinationStatistics
    {
        public int TotalDetected { get; set; }
        public double AverageConfidence { get; set; }
        public Dictionary<string, int> MostCommonPatterns { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Statistics about duplicate detection.
    /// </summary>
    public class DuplicateStatistics
    {
        public int TotalDuplicatesDetected { get; set; }
        public double DuplicateRate { get; set; }
    }

    /// <summary>
    /// Time-based analysis of validation patterns.
    /// </summary>
    public class TimeBasedAnalysis
    {
        public int PeakValidationHour { get; set; }
        public int PeakHourValidationCount { get; set; }
        public int ValidationsLast24Hours { get; set; }
        public int ValidationsLastWeek { get; set; }
        public string TrendDirection { get; set; } = "Unknown";
    }
}
