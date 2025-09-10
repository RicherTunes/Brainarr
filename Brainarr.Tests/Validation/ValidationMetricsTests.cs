using System;
using System.Collections.Generic;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using Xunit;

namespace Brainarr.Tests.Validation
{
    public class ValidationMetricsTests
    {
        private static ValidationResult MakeResult(bool isValid, double score, long timeMs, ValidationSeverity severity = ValidationSeverity.Warning, string reason = null)
        {
            return new ValidationResult
            {
                IsValid = isValid,
                Score = score,
                Metadata = new ValidationMetadata { ValidationTimeMs = timeMs },
                Findings = string.IsNullOrEmpty(reason)
                    ? new List<ValidationFinding> { new ValidationFinding { Severity = severity, ScoreImpact = isValid ? 0.1 : -0.5 } }
                    : new List<ValidationFinding> { new ValidationFinding { Severity = severity, Message = reason, ScoreImpact = isValid ? 0.1 : -0.5 } }
            };
        }

        [Fact]
        public void Record_and_Report_should_aggregate_basic_metrics()
        {
            var metrics = new ValidationMetrics();
            metrics.RecordValidationResult(MakeResult(true, 0.9, 10), "OpenAI");
            metrics.RecordValidationResult(MakeResult(false, 0.2, 20, ValidationSeverity.Critical, "fictional"), "OpenAI");
            metrics.RecordBatchValidationResult(new List<ValidationResult>
            {
                MakeResult(true, 0.8, 15),
                MakeResult(false, 0.1, 30, ValidationSeverity.Error, "duplicate")
            }, "Ollama");

            var report = metrics.GenerateReport(TimeSpan.FromDays(1));
            report.TotalValidations.Should().Be(4);
            report.ValidRecommendations.Should().Be(2);
            report.InvalidRecommendations.Should().Be(2);
            report.ProviderMetrics.Count.Should().Be(2);
        }

        [Fact]
        public void Reset_clears_metrics()
        {
            var metrics = new ValidationMetrics();
            metrics.RecordValidationResult(MakeResult(true, 0.9, 10), "OpenAI");
            metrics.Reset();
            var report = metrics.GenerateReport(TimeSpan.FromDays(1));
            report.TotalValidations.Should().Be(0);
        }
    }
}
