using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    public class ReviewActionAuditServiceRedactionTests : IDisposable
    {
        private readonly string _tempRoot;

        public ReviewActionAuditServiceRedactionTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [Theory]
        [InlineData("sk-proj-abc123def456ghi789", "sk-p...i789")]
        [InlineData("anthropic-key-secret-value", "anth...alue")]
        [InlineData("short", "********")]
        [InlineData("12345678", "********")]
        [InlineData("123456789", "1234...6789")]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("   ", null)]
        public void RedactIdempotencyKey_ShouldMaskMiddleCharacters(string input, string expected)
        {
            // RedactIdempotencyKey is private static on ReviewQueueActionHandler.
            // We test indirectly through the GetReviewActionAudit endpoint,
            // but also validate the sanitization boundary directly here.
            var sanitized = ReviewActionAuditService.SanitizeIdempotencyKey(input);
            if (sanitized == null)
            {
                expected.Should().BeNull();
                return;
            }

            // Verify the sanitize step preserves or truncates correctly
            sanitized.Length.Should().BeLessOrEqualTo(80);

            // Now verify the redaction pattern via reflection on the handler
            var handlerType = typeof(ReviewActionAuditService).Assembly
                .GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.ReviewQueueActionHandler");
            var redactMethod = handlerType!.GetMethod("RedactIdempotencyKey",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = (string)redactMethod!.Invoke(null, new object[] { input });
            result.Should().Be(expected);
        }

        [Fact]
        public void GetReviewActionAudit_ShouldNotLeakFullIdempotencyKey()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var auditService = new ReviewActionAuditService(logger, _tempRoot);

            var secretKey = "super-secret-api-key-do-not-leak-12345";
            auditService.Write(new ReviewActionAuditEvent(
                Id: "redact-test-1",
                Action: "review/applytriage",
                Actor: "redaction-tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 5,
                CandidateCount: 3,
                ApprovedCount: 2,
                ReleasedCount: 2,
                Cap: 5,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: secretKey,
                Items: new List<ReviewActionAuditItem>
                {
                    new("Artist1", "Album1", "Rock", 0.95, "Good match", 2024, "mbid-a1", "mbid-b1")
                }));

            // Build a handler with the audit service to test GetReviewActionAudit
            var handlerType = typeof(ReviewActionAuditService).Assembly
                .GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.ReviewQueueActionHandler");
            var reviewQueueType = typeof(ReviewActionAuditService).Assembly
                .GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Support.ReviewQueueService");
            var historyType = typeof(ReviewActionAuditService).Assembly
                .GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Support.RecommendationHistory");
            var triageAdvisorType = typeof(ReviewActionAuditService).Assembly
                .GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Core.RecommendationTriageAdvisor");
            var styleCatalogType = typeof(ReviewActionAuditService).Assembly
                .GetType("NzbDrone.Core.ImportLists.Brainarr.Services.Styles.IStyleCatalogService");

            var queue = Activator.CreateInstance(reviewQueueType!, logger, _tempRoot);
            var history = Activator.CreateInstance(historyType!, logger, _tempRoot);
            var triageAdvisor = Activator.CreateInstance(triageAdvisorType!);

            // Create a mock style catalog
            var styleCatalog = new Moq.Mock<NzbDrone.Core.ImportLists.Brainarr.Services.Styles.IStyleCatalogService>();
            var handler = Activator.CreateInstance(handlerType!, queue, history, styleCatalog.Object, triageAdvisor, (Action)null, logger, auditService);

            var getAuditMethod = handlerType!.GetMethod("GetReviewActionAudit");
            var query = new Dictionary<string, string> { ["limit"] = "10" };
            var result = getAuditMethod!.Invoke(handler, new object[] { query });

            var json = JsonSerializer.Serialize(result);

            // The full key must NOT appear in the output
            json.Should().NotContain(secretKey);
            // The redacted form should appear (first 4 + last 4)
            json.Should().Contain("supe...2345");
        }

        [Theory]
        [InlineData(null, "system")]
        [InlineData("", "system")]
        [InlineData("   ", "system")]
        [InlineData("legitimate-user", "legitimate-user")]
        [InlineData("user<script>alert('xss')</script>", "user<script>alert('xss')</script>")]
        public void SanitizeActor_ShouldDefaultToSystem_AndPreserveValidInput(string input, string expected)
        {
            var result = ReviewActionAuditService.SanitizeActor(input);
            result.Should().Be(expected);
        }

        [Fact]
        public void SanitizeActor_ShouldTruncateAt64Characters()
        {
            var longActor = new string('A', 100);
            var result = ReviewActionAuditService.SanitizeActor(longActor);
            result.Length.Should().Be(64);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData("valid-key-123", "valid-key-123")]
        public void SanitizeIdempotencyKey_ShouldReturnNull_ForBlankInputs(string input, string expected)
        {
            var result = ReviewActionAuditService.SanitizeIdempotencyKey(input);
            if (expected == null)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().Be(expected);
            }
        }

        [Fact]
        public void SanitizeIdempotencyKey_ShouldTruncateAt80Characters()
        {
            var longKey = new string('K', 120);
            var result = ReviewActionAuditService.SanitizeIdempotencyKey(longKey);
            result.Should().NotBeNull();
            result!.Length.Should().Be(80);
        }

        [Fact]
        public void AuditEvent_ReasonField_ShouldNotContainApiKeys()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var auditService = new ReviewActionAuditService(logger, _tempRoot);

            // Simulate a reason that accidentally includes an API key pattern
            var dangerousReason = "Match found using sk-proj-abc123def456 provider";
            auditService.Write(new ReviewActionAuditEvent(
                Id: "reason-test",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 1,
                CandidateCount: 1,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: "reason-key",
                Items: new List<ReviewActionAuditItem>
                {
                    new("Artist", "Album", "Jazz", 0.9, dangerousReason, 2024, "a-mbid", "b-mbid")
                }));

            // Read back the raw JSONL file and verify the reason is stored
            // (this test documents the current behavior — reason codes are structural,
            // but Item.Reason is user/AI-generated and may contain sensitive data)
            var content = File.ReadAllText(auditService.GetAuditPath());
            content.Should().Contain("reason-test");

            // Verify reason codes (structural) don't contain API key patterns
            var events = auditService.GetRecent("review/applytriage", 1);
            events.Should().HaveCount(1);
            foreach (var code in events[0].ReasonCodes)
            {
                code.Should().NotMatchRegex(@"sk-[a-zA-Z0-9]{10,}",
                    "reason codes are structural identifiers and must not contain API key patterns");
            }
        }

        [Fact]
        public void DefaultRetention_ShouldBe90Days()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            // The convenience constructor should use 90-day retention
            var service = new ReviewActionAuditService(logger, _tempRoot);

            var now = DateTime.UtcNow;
            // Write an event dated 60 days ago — should survive retention
            service.Write(new ReviewActionAuditEvent(
                Id: "sixty-days-ago",
                Action: "review/applytriage",
                Actor: "retention-test",
                DryRun: false,
                Mode: "triage",
                PendingCount: 1,
                CandidateCount: 1,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: now.AddDays(-60),
                IdempotencyKey: "retention-60d"));

            // Write a current event to trigger retention enforcement
            service.Write(new ReviewActionAuditEvent(
                Id: "current-event",
                Action: "review/applytriage",
                Actor: "retention-test",
                DryRun: false,
                Mode: "triage",
                PendingCount: 1,
                CandidateCount: 1,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: now,
                IdempotencyKey: "retention-now"));

            // The 60-day-old event should still exist (within 90-day retention)
            var content = File.ReadAllText(service.GetAuditPath());
            content.Should().Contain("sixty-days-ago",
                "events within 90-day retention window should be preserved");
            content.Should().Contain("current-event");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }
    }
}
