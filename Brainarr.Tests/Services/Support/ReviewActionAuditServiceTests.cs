using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    public class ReviewActionAuditServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public ReviewActionAuditServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [Fact]
        public void Write_ShouldTrimAuditFile_ByMaxBytes()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var service = new ReviewActionAuditService(
                logger,
                _tempRoot,
                maxEntries: 1000,
                retentionDays: 365,
                maxBytes: 800,
                utcNow: null);

            for (var index = 0; index < 20; index++)
            {
                service.Write(new ReviewActionAuditEvent(
                    Id: $"evt-{index}",
                    Action: "review/applytriage",
                    Actor: "tester",
                    DryRun: false,
                    Mode: "triage",
                    PendingCount: 10,
                    CandidateCount: 8,
                    ApprovedCount: 2,
                    ReleasedCount: 2,
                    Cap: 2,
                    Capped: true,
                    ReasonCodes: new List<string> { "CONFIDENCE_BELOW_THRESHOLD", "DUPLICATE_SIGNAL", "MISSING_REQUIRED_MBIDS" },
                    OccurredAtUtc: DateTime.UtcNow,
                    IdempotencyKey: $"idem-{index}"));
            }

            var path = service.GetAuditPath();
            File.Exists(path).Should().BeTrue();

            var fileInfo = new FileInfo(path);
            fileInfo.Length.Should().BeLessOrEqualTo(800);
            File.ReadAllText(path).Should().Contain("evt-19");
        }

        [Fact]
        public void Write_ShouldDropExpiredEvents_ByRetentionWindow()
        {
            var now = new DateTime(2026, 2, 16, 0, 0, 0, DateTimeKind.Utc);
            var logger = Helpers.TestLogger.CreateNullLogger();
            var service = new ReviewActionAuditService(
                logger,
                _tempRoot,
                maxEntries: 1000,
                retentionDays: 7,
                maxBytes: 8 * 1024,
                utcNow: () => now);

            service.Write(new ReviewActionAuditEvent(
                Id: "old-event",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 5,
                CandidateCount: 4,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: true,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: now.AddDays(-30),
                IdempotencyKey: "old-idem"));

            service.Write(new ReviewActionAuditEvent(
                Id: "new-event",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 5,
                CandidateCount: 4,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: now,
                IdempotencyKey: "new-idem"));

            var content = File.ReadAllText(service.GetAuditPath());
            content.Should().NotContain("old-event");
            content.Should().Contain("new-event");
        }

        [Fact]
        public void TryGetByIdempotencyKey_ShouldReturnMostRecentMatch()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var service = new ReviewActionAuditService(logger, _tempRoot);

            service.Write(new ReviewActionAuditEvent(
                Id: "first",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 3,
                CandidateCount: 2,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: true,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: DateTime.UtcNow.AddSeconds(-2),
                IdempotencyKey: "idem-key"));

            service.Write(new ReviewActionAuditEvent(
                Id: "second",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 3,
                CandidateCount: 2,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: "idem-key"));

            var found = service.TryGetByIdempotencyKey("review/applytriage", "idem-key", out var auditEvent);

            found.Should().BeTrue();
            auditEvent.Should().NotBeNull();
            auditEvent.Id.Should().Be("second");
        }

        [Fact]
        public void GetRecent_ShouldFilterByAction_AndRespectLimit()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var service = new ReviewActionAuditService(logger, _tempRoot);

            service.Write(new ReviewActionAuditEvent(
                Id: "evt-1",
                Action: "review/apply",
                Actor: "tester",
                DryRun: false,
                Mode: "selection",
                PendingCount: 3,
                CandidateCount: 2,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: false,
                ReasonCodes: new List<string> { "MANUAL_SELECTION" },
                OccurredAtUtc: DateTime.UtcNow.AddSeconds(-2),
                IdempotencyKey: "idem-1"));

            service.Write(new ReviewActionAuditEvent(
                Id: "evt-2",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 4,
                CandidateCount: 3,
                ApprovedCount: 1,
                ReleasedCount: 1,
                Cap: 1,
                Capped: true,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: DateTime.UtcNow.AddSeconds(-1),
                IdempotencyKey: "idem-2"));

            service.Write(new ReviewActionAuditEvent(
                Id: "evt-3",
                Action: "review/applytriage",
                Actor: "tester",
                DryRun: false,
                Mode: "triage",
                PendingCount: 5,
                CandidateCount: 4,
                ApprovedCount: 2,
                ReleasedCount: 2,
                Cap: 2,
                Capped: false,
                ReasonCodes: new List<string> { "CONSISTENT_SIGNALS" },
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: "idem-3"));

            var recent = service.GetRecent("review/applytriage", 1);

            recent.Should().HaveCount(1);
            recent[0].Id.Should().Be("evt-3");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }
    }
}
