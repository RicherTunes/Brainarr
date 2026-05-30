using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    /// <summary>
    /// Pins that the confidence-provenance flag survives the review-queue carrier round-trip
    /// (Enqueue → GetPending → DequeueAccepted). The ReviewItem and the rebuilt Recommendation are
    /// both rebuild sites that previously dropped <see cref="Recommendation.ConfidenceProvided"/>,
    /// which would make the provenance-aware triage a no-op for queued items.
    /// </summary>
    public class ReviewQueueServiceProvenanceTests : IDisposable
    {
        private readonly string _dir;

        public ReviewQueueServiceProvenanceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "brainarr-rqtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void Enqueue_PreservesConfidenceProvided_PerItem()
        {
            var queue = new ReviewQueueService(Brainarr.Tests.Helpers.TestLogger.CreateNullLogger(), _dir);

            queue.Enqueue(new List<Recommendation>
            {
                new Recommendation { Artist = "Scored", Album = "X", Confidence = 0.9, ConfidenceProvided = true },
                new Recommendation { Artist = "Unscored", Album = "Y", Confidence = 0.7, ConfidenceProvided = false },
            });

            var pending = queue.GetPending();

            pending.Single(i => i.Artist == "Scored").ConfidenceProvided.Should().BeTrue();
            pending.Single(i => i.Artist == "Unscored").ConfidenceProvided.Should().BeFalse();
        }

        [Fact]
        public void DequeueAccepted_RoundTripsConfidenceProvided()
        {
            var queue = new ReviewQueueService(Brainarr.Tests.Helpers.TestLogger.CreateNullLogger(), _dir);
            queue.Enqueue(new List<Recommendation>
            {
                new Recommendation { Artist = "Unscored", Album = "Y", Confidence = 0.7, ConfidenceProvided = false },
            });

            queue.SetStatus("Unscored", "Y", ReviewQueueService.ReviewStatus.Accepted);
            var released = queue.DequeueAccepted();

            released.Should().ContainSingle();
            released[0].ConfidenceProvided.Should().BeFalse("provenance must survive the ReviewItem→Recommendation rebuild");
        }

        [Fact]
        public void EnqueueDefault_ConfidenceProvidedTrue_ForBackCompat()
        {
            // A Recommendation that never set the flag (legacy producers) defaults to provided=true,
            // preserving prior triage behavior.
            var queue = new ReviewQueueService(Brainarr.Tests.Helpers.TestLogger.CreateNullLogger(), _dir);
            queue.Enqueue(new List<Recommendation>
            {
                new Recommendation { Artist = "Legacy", Album = "Z", Confidence = 0.8 },
            });

            queue.GetPending().Single().ConfidenceProvided.Should().BeTrue();
        }
    }
}
