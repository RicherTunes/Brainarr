namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

internal static class LibraryHealerReasonCodes
{
    private const string TagMetadataMissing = "TAG_METADATA_MISSING";

    private static readonly HashSet<string> StaticReasonCodes = new(StringComparer.Ordinal)
    {
        "FILE_EXISTS",
        "FILE_MISSING",
        "PATH_PROBE_INCONCLUSIVE",
        "PATH_EMPTY",
        "PATH_IS_DIRECTORY",
        "PATH_PARENT_UNAVAILABLE",
        "PATH_ACCESS_DENIED",
        "PATH_INVALID",
        "PATH_PROBE_IO_ERROR",
        nameof(TimeoutException),
        nameof(OperationCanceledException),
        nameof(TaskCanceledException),
        "TAG_READER_NOT_ATTEMPTED",
        "TAG_READER_FAILED",
        "TAG_READER_DURATION_POSITIVE",
        "TAG_READER_ZERO_DURATION",
        "PROBE_FAILED",
        "PROBE_DURATION_POSITIVE",
        "PROBE_DURATION_ZERO",
        "HEADER_REPAIR_CANDIDATE",
    };

    public static IReadOnlyList<string> Normalize(
        IReadOnlyList<string>? reasonCodes,
        TagMetadataEvidence? metadata)
    {
        var normalized = new List<string>();
        if (reasonCodes is not null)
        {
            foreach (var reasonCode in reasonCodes)
            {
                AddStaticReason(normalized, reasonCode);
            }
        }

        var missingFields = TagMetadataFields.GetMissingFields(metadata);
        if (missingFields.Count > 0)
        {
            AddIfMissing(normalized, TagMetadataMissing);
            foreach (var missingField in missingFields)
            {
                AddIfMissing(normalized, "TAG_MISSING_" + missingField.Trim().ToUpperInvariant());
            }
        }

        return normalized;
    }

    public static LibraryHealerLabel NormalizeLabel(LibraryHealerLabel label, TagMetadataEvidence? metadata)
    {
        // Only downgrade to FalsePositive when metadata is actually PRESENT and every field is set.
        // A null metadata is NOT evidence of "all tags present" (GetMissingFields(null) returns empty),
        // so downgrading on it would hide a real TagMetadataIssue as a false-negative. Leave the label
        // as-is; the malformed-record gate surfaces the absent-metadata case as NeedsHumanReview.
        if (label == LibraryHealerLabel.TagMetadataIssue
            && metadata is not null
            && TagMetadataFields.GetMissingFields(metadata).Count == 0)
        {
            return LibraryHealerLabel.FalsePositive;
        }

        return label;
    }

    private static void AddStaticReason(List<string> normalized, string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return;
        }

        var trimmed = reasonCode.Trim();
        if (trimmed == TagMetadataMissing || trimmed.StartsWith("TAG_MISSING_", StringComparison.Ordinal))
        {
            return;
        }

        if (StaticReasonCodes.Contains(trimmed))
        {
            AddIfMissing(normalized, trimmed);
        }
    }

    private static void AddIfMissing(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }
}
