namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

internal static class HealerFreshnessNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return HealerTreatmentVocab.Freshness.Unknown;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, HealerTreatmentVocab.Freshness.Current, StringComparison.Ordinal)
            || string.Equals(trimmed, HealerTreatmentVocab.Freshness.Stale, StringComparison.Ordinal)
            || string.Equals(trimmed, HealerTreatmentVocab.Freshness.Missing, StringComparison.Ordinal)
            || string.Equals(trimmed, HealerTreatmentVocab.Freshness.Unknown, StringComparison.Ordinal))
        {
            return trimmed;
        }

        return HealerTreatmentVocab.Freshness.Unknown;
    }
}
