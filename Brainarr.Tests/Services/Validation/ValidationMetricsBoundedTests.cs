using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    /// <summary>
    /// ValidationMetrics._validationHistory is capped at 10,000 records (instance-scoped). Pins that
    /// the cap actually holds under sustained recording — previously bounded but untested.
    /// </summary>
    public class ValidationMetricsBoundedTests
    {
        [Fact]
        public void RecordValidationResult_KeepsHistoryBounded()
        {
            var metrics = new ValidationMetrics();

            for (int i = 0; i < 12000; i++)
            {
                metrics.RecordValidationResult(new ValidationResult { IsValid = true, Score = 1.0 }, "TestProvider");
            }

            var report = metrics.GenerateReport(TimeSpan.FromDays(365));

            report.TotalValidations.Should().BeLessThanOrEqualTo(10000,
                "single-record validation history must stay bounded at the 10k cap");
            report.TotalValidations.Should().BeGreaterThan(0, "recent records are retained");
        }

        [Fact]
        public void RecordBatchValidationResult_KeepsHistoryBounded()
        {
            // The batch path bulk-adds, so it must trim down to the cap in one pass (a regression from
            // an earlier version that let the batch path bypass the cap).
            var metrics = new ValidationMetrics();

            for (int batch = 0; batch < 12; batch++)
            {
                var results = new System.Collections.Generic.List<ValidationResult>();
                for (int i = 0; i < 1000; i++)
                {
                    results.Add(new ValidationResult { IsValid = true, Score = 1.0 });
                }
                metrics.RecordBatchValidationResult(results, "TestProvider");
            }

            var report = metrics.GenerateReport(TimeSpan.FromDays(365));

            report.TotalValidations.Should().BeLessThanOrEqualTo(10000,
                "batch validation history must also stay bounded at the 10k cap");
        }
    }
}
