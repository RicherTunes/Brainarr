using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

/// <summary>
/// Computes a finding's freshness read-only at scan time by comparing the evidence Lidarr already
/// recorded for a track (its DB <c>Size</c>/<c>Modified</c>) against the live on-disk probe just
/// taken. It never writes: it only reads the two states already gathered by the scan.
/// <list type="bullet">
/// <item>on-disk state matches the recorded state -> <c>current</c></item>
/// <item>on-disk state drifted from the recorded state -> <c>stale</c></item>
/// <item>the file is gone -> <c>missing</c></item>
/// <item>the file could not be probed -> <c>unknown</c></item>
/// </list>
/// </summary>
internal static class HealerFreshnessEvaluator
{
    // Filesystem timestamps (100ns tick precision) and Lidarr's stored Modified can differ by
    // sub-second rounding for an otherwise-unchanged file; only a real drift should read as stale.
    private static readonly TimeSpan ModifiedTolerance = TimeSpan.FromSeconds(2);

    public readonly record struct Freshness(string Evidence, string Identity);

    /// <summary>The path probe could not determine the file's state (timeout, access denied, ...).</summary>
    public static Freshness Unprobeable { get; } = new(
        HealerTreatmentVocab.Freshness.Unknown,
        HealerTreatmentVocab.Freshness.Unknown);

    /// <summary>The path probe confirmed the file no longer exists.</summary>
    public static Freshness Gone { get; } = new(
        HealerTreatmentVocab.Freshness.Missing,
        HealerTreatmentVocab.Freshness.Missing);

    /// <summary>
    /// Freshness for a file that existed at probe time, comparing the recorded DB state against the
    /// live fingerprint.
    /// </summary>
    public static Freshness ForProbedFile(long? recordedSize, DateTime? recordedModified, FileFingerprint probe)
    {
        if (probe is null || !probe.Exists)
        {
            return Gone;
        }

        // Nothing readable off disk -> we cannot judge drift either way.
        if (probe.Size is null && probe.ModifiedUtc is null)
        {
            return Unprobeable;
        }

        var drifted = false;
        var compared = false;

        if (probe.Size.HasValue && recordedSize.HasValue)
        {
            compared = true;
            drifted |= probe.Size.Value != recordedSize.Value;
        }

        if (probe.ModifiedUtc.HasValue && recordedModified.HasValue)
        {
            compared = true;
            drifted |= !TimestampsMatch(recordedModified.Value, probe.ModifiedUtc.Value);
        }

        if (!compared)
        {
            // We read something off disk but had no recorded counterpart to compare it against.
            return Unprobeable;
        }

        var state = drifted
            ? HealerTreatmentVocab.Freshness.Stale
            : HealerTreatmentVocab.Freshness.Current;
        return new Freshness(state, state);
    }

    private static bool TimestampsMatch(DateTime recorded, DateTime probed)
    {
        var recordedUtc = ToUtc(recorded);
        var probedUtc = ToUtc(probed);
        return (recordedUtc - probedUtc).Duration() <= ModifiedTolerance;
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
