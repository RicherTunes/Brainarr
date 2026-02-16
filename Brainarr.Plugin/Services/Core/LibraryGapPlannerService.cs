using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    internal sealed class LibraryGapPlannerService
    {
        private static readonly string[] EraOrder = { "Classic", "Golden Age", "Modern", "Contemporary" };

        public IReadOnlyList<LibraryGapPlanItem> BuildPlan(LibraryProfile profile, int maxItems = 3)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            maxItems = Math.Max(1, maxItems);

            var plan = new List<LibraryGapPlanItem>();
            AddGenreDiversificationTargets(profile, plan);
            AddEraTargets(profile, plan);

            return plan
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.Confidence)
                .Take(maxItems)
                .ToList();
        }

        private static void AddGenreDiversificationTargets(LibraryProfile profile, List<LibraryGapPlanItem> plan)
        {
            var distribution = TryReadGenreDistribution(profile.Metadata);
            if (distribution.Count == 0)
            {
                return;
            }

            foreach (var candidate in distribution.Where(d => d.Value > 0.0 && d.Value < 8.0).OrderBy(d => d.Value).Take(2))
            {
                var targetFloor = 8.0;
                var gap = Math.Max(0.0, targetFloor - candidate.Value);
                var priority = candidate.Value < 3.0 ? 90 : 70;
                var confidence = candidate.Value < 3.0 ? 0.88 : 0.72;
                plan.Add(new LibraryGapPlanItem(
                    "style",
                    candidate.Key,
                    priority,
                    confidence,
                    $"'{candidate.Key}' is underrepresented at {candidate.Value:F1}% of the library footprint.",
                    $"Prioritize {candidate.Key} picks that stay adjacent to existing top genres.",
                    new[]
                    {
                        $"genre_share={candidate.Value:F1}%",
                        $"target_floor={targetFloor:F1}%",
                        $"gap={gap:F1}pp"
                    },
                    gap,
                    $"Gap for '{candidate.Key}' is {gap:F1} percentage points below the target floor."));
            }
        }

        private static void AddEraTargets(LibraryProfile profile, List<LibraryGapPlanItem> plan)
        {
            var preferredEras = TryReadStringList(profile.Metadata, "PreferredEras");
            if (preferredEras.Count == 0)
            {
                preferredEras = new List<string> { "Modern", "Contemporary" };
            }

            var missingEras = EraOrder.Where(e => preferredEras.All(x => !string.Equals(x, e, StringComparison.OrdinalIgnoreCase))).ToList();
            if (missingEras.Count > 0)
            {
                var targetEra = missingEras[0];
                plan.Add(new LibraryGapPlanItem(
                    "era",
                    targetEra,
                    80,
                    0.79,
                    $"Current preferred eras are [{string.Join(", ", preferredEras)}], leaving '{targetEra}' underexplored.",
                    $"Add 2-3 high-confidence {targetEra} era albums that match existing style preferences.",
                    new[]
                    {
                        $"preferred_eras=[{string.Join(", ", preferredEras)}]",
                        $"missing_era={targetEra}"
                    },
                    0.20,
                    $"'{targetEra}' is currently absent from preferred era signals."));
            }

            var newReleaseRatio = TryReadDouble(profile.Metadata, "NewReleaseRatio");
            if (newReleaseRatio > 0.45)
            {
                var recencyGap = Math.Max(0.0, newReleaseRatio - 0.35);
                plan.Add(new LibraryGapPlanItem(
                    "era-balance",
                    "Catalog Backfill",
                    75,
                    0.74,
                    $"New release ratio is {newReleaseRatio:P0}, indicating heavy recency bias.",
                    "Backfill influential catalog releases from earlier decades.",
                    new[]
                    {
                        $"new_release_ratio={newReleaseRatio:P0}",
                        "target_band=15%-35%"
                    },
                    recencyGap,
                    $"Recency ratio exceeds target band by {recencyGap:P0}; backfill improves era balance."));
            }
            else if (newReleaseRatio < 0.12)
            {
                var freshnessGap = Math.Max(0.0, 0.18 - newReleaseRatio);
                plan.Add(new LibraryGapPlanItem(
                    "era-balance",
                    "Current Releases",
                    75,
                    0.74,
                    $"New release ratio is {newReleaseRatio:P0}, indicating low recency coverage.",
                    "Add recent releases from trusted artists/styles to improve freshness.",
                    new[]
                    {
                        $"new_release_ratio={newReleaseRatio:P0}",
                        "target_band=15%-35%"
                    },
                    freshnessGap,
                    $"Recency ratio is below target band by {freshnessGap:P0}; add current releases."));
            }
        }

        private static Dictionary<string, double> TryReadGenreDistribution(Dictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue("GenreDistribution", out var raw) || raw == null)
            {
                return new Dictionary<string, double>();
            }

            if (raw is Dictionary<string, double> typed)
            {
                return typed
                    .Where(x => !x.Key.Contains("_", StringComparison.Ordinal))
                    .ToDictionary(x => x.Key, x => x.Value);
            }

            if (raw is IDictionary<string, object> objectMap)
            {
                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in objectMap)
                {
                    if (pair.Key.Contains("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (pair.Value is IConvertible)
                    {
                        result[pair.Key] = Convert.ToDouble(pair.Value);
                    }
                }

                return result;
            }

            return new Dictionary<string, double>();
        }

        private static List<string> TryReadStringList(Dictionary<string, object> metadata, string key)
        {
            if (metadata == null || !metadata.TryGetValue(key, out var raw) || raw == null)
            {
                return new List<string>();
            }

            if (raw is List<string> list)
            {
                return list.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }

            if (raw is string[] array)
            {
                return array.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }

            if (raw is IEnumerable<object> objects)
            {
                return objects.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }

            return new List<string>();
        }

        private static double TryReadDouble(Dictionary<string, object> metadata, string key)
        {
            if (metadata == null || !metadata.TryGetValue(key, out var raw) || raw == null)
            {
                return 0.0;
            }

            return raw is IConvertible ? Convert.ToDouble(raw) : 0.0;
        }
    }

    internal sealed record LibraryGapPlanItem(
        string Category,
        string Target,
        int Priority,
        double Confidence,
        string Rationale,
        string SuggestedAction,
        IReadOnlyList<string> Evidence = null,
        double ExpectedLift = 0.0,
        string WhyNow = null);
}
