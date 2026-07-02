namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record LibraryHealerClassification(
    LibraryHealerLabel Label,
    IReadOnlyList<string> InternalReasonCodes);

public static class LibraryHealerClassifier
{
    public static LibraryHealerClassification ClassifyFileFingerprint(FileFingerprint fingerprint)
    {
        return ClassifyFileExistence(new FileExistenceEvidence(true, true, fingerprint.Exists, null, null));
    }

    public static LibraryHealerClassification ClassifyFileExistence(FileExistenceEvidence existence)
    {
        if (existence.CheckSucceeded)
        {
            return existence.Exists
                ? new LibraryHealerClassification(
                    LibraryHealerLabel.FalsePositive,
                    new[] { "FILE_EXISTS" })
                : new LibraryHealerClassification(
                    LibraryHealerLabel.PathInconsistency,
                    new[] { "FILE_MISSING" });
        }

        var reasons = new List<string> { "PATH_PROBE_INCONCLUSIVE" };
        if (!string.IsNullOrWhiteSpace(existence.ErrorType))
        {
            reasons.Add(existence.ErrorType);
        }

        return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
    }

    public static LibraryHealerClassification Classify(TagReaderEvidence tagReader, ProbeEvidence? probe)
    {
        var reasons = new List<string>();

        if (!tagReader.ReadAttempted)
        {
            reasons.Add("TAG_READER_NOT_ATTEMPTED");
            return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
        }

        if (!tagReader.ReadSucceeded)
        {
            reasons.Add("TAG_READER_FAILED");
        }
        else if (tagReader.DurationSeconds.GetValueOrDefault() > 0)
        {
            reasons.Add("TAG_READER_DURATION_POSITIVE");
            AddTagMetadataReasons(tagReader.Metadata, reasons);
            if (GetMissingFields(tagReader.Metadata).Count > 0)
            {
                return new LibraryHealerClassification(LibraryHealerLabel.TagMetadataIssue, reasons);
            }

            return new LibraryHealerClassification(LibraryHealerLabel.FalsePositive, reasons);
        }
        else
        {
            reasons.Add("TAG_READER_ZERO_DURATION");
        }

        if (probe is null || !probe.ProbeAttempted)
        {
            return new LibraryHealerClassification(
                tagReader.ReadSucceeded ? LibraryHealerLabel.TagReaderSymptom : LibraryHealerLabel.NeedsHumanReview,
                reasons);
        }

        if (!probe.ProbeSucceeded)
        {
            reasons.Add("PROBE_FAILED");
            return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
        }

        if (probe.DurationSeconds.GetValueOrDefault() > 0)
        {
            reasons.Add("PROBE_DURATION_POSITIVE");
            if (IsMp4FlacSignature(probe))
            {
                reasons.Add("HEADER_REPAIR_CANDIDATE");
            }

            return new LibraryHealerClassification(LibraryHealerLabel.ProbeEvidence, reasons);
        }

        reasons.Add("PROBE_DURATION_ZERO");
        return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
    }

    private static bool IsMp4FlacSignature(ProbeEvidence probe)
    {
        return Contains(probe.Container, "mp4")
            || Contains(probe.Container, "m4a")
            || Contains(probe.Container, "mov")
            ? Contains(probe.AudioCodec, "flac")
            : false;
    }

    private static bool Contains(string? value, string token)
    {
        return value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddTagMetadataReasons(TagMetadataEvidence? metadata, List<string> reasons)
    {
        var missingFields = GetMissingFields(metadata);
        if (missingFields.Count > 0)
        {
            reasons.Add("TAG_METADATA_MISSING");
            foreach (var missingField in missingFields)
            {
                if (!string.IsNullOrWhiteSpace(missingField))
                {
                    reasons.Add("TAG_MISSING_" + missingField.Trim().ToUpperInvariant());
                }
            }
        }
    }

    private static IReadOnlyList<string> GetMissingFields(TagMetadataEvidence? metadata)
    {
        return TagMetadataFields.GetMissingFields(metadata);
    }
}
