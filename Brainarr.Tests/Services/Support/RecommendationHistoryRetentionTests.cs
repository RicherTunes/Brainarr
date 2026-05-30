using System;
using System.Collections.Generic;
using System.IO;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    /// <summary>
    /// RecommendationHistory's Suggestions/Rejected dictionaries (high-cardinality artist|album keys)
    /// previously grew unbounded on the process-lifetime singleton — CleanupOldEntries existed but had
    /// no caller. It's now wired on load + throttled per-run. These pin that stale entries are pruned
    /// (180-day retention) while recent ones survive and dedup is preserved.
    /// </summary>
    public class RecommendationHistoryRetentionTests : IDisposable
    {
        private readonly string _dir;

        public RecommendationHistoryRetentionTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "brainarr-histret-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "data"));
        }

        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

        private string HistoryPath => Path.Combine(_dir, "data", "recommendation_history.json");

        private const string Template = @"{
  ""Suggestions"": {
    ""oldartist|oldalbum"": { ""Artist"": ""OldArtist"", ""Album"": ""OldAlbum"", ""FirstSuggested"": ""OLDDATE"", ""LastSuggested"": ""OLDDATE"", ""SuggestionCount"": 1, ""Confidence"": 0.9 },
    ""recentartist|recentalbum"": { ""Artist"": ""RecentArtist"", ""Album"": ""RecentAlbum"", ""FirstSuggested"": ""RECENTDATE"", ""LastSuggested"": ""RECENTDATE"", ""SuggestionCount"": 1, ""Confidence"": 0.9 }
  },
  ""Rejected"": {
    ""rejold|x"": { ""Artist"": ""RejectedOld"", ""Album"": ""X"", ""RejectedDate"": ""OLDDATE"", ""SuggestionCount"": 1, ""DaysSinceSuggestion"": 0, ""RejectionReason"": ""test"" }
  },
  ""Accepted"": {}, ""Disliked"": {}, ""DislikePatterns"": {}
}";

        [Fact]
        public void Constructor_PrunesStaleEntries_OnLoad_KeepsRecent()
        {
            var json = Template
                .Replace("OLDDATE", DateTime.UtcNow.AddDays(-200).ToString("o"))
                .Replace("RECENTDATE", DateTime.UtcNow.ToString("o"));
            File.WriteAllText(HistoryPath, json);

            // Construction triggers LoadHistory + the startup CleanupOldEntries (which persists the prune).
            _ = new RecommendationHistory(TestLogger.CreateNullLogger(), _dir);

            var persisted = File.ReadAllText(HistoryPath);
            persisted.Should().NotContain("OldArtist", "a suggestion older than the 180-day retention must be pruned");
            persisted.Should().NotContain("RejectedOld", "a rejection older than the retention must be pruned");
            persisted.Should().Contain("RecentArtist", "a recent suggestion must be retained");
        }

        [Fact]
        public void RecordSuggestions_KeepsRecentItem_AfterPruneWiring()
        {
            // The prune wiring (startup CleanupOldEntries in the ctor + the throttled per-run prune at
            // the top of RecordSuggestions) must not drop a freshly recorded RECENT item. Note: the
            // ctor sets _lastCleanupUtc=UtcNow, so the per-run prune is throttled-off on this first
            // call — the prune LOGIC itself is covered by Constructor_PrunesStaleEntries above (same
            // PruneOldEntries); this pins that recording still works end-to-end through the wiring.
            var history = new RecommendationHistory(TestLogger.CreateNullLogger(), _dir);
            history.RecordSuggestions(new List<Recommendation>
            {
                new Recommendation { Artist = "FreshArtist", Album = "FreshAlbum", Confidence = 0.9 }
            });

            history.GetStats().TotalSuggested.Should().Be(1,
                "a freshly recorded suggestion survives the per-run prune (only >180-day entries go)");
        }
    }
}
