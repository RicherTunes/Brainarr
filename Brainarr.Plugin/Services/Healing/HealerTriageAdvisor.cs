namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public static class HealerTriageAdvisor
{
    public static HealerTreatmentPlan Advise(
        LibraryHealerFinding finding,
        HealerFindingFreshness? freshness = null)
    {
        var state = freshness ?? HealerFindingFreshness.Current;

        if (finding is null)
        {
            return Malformed(state);
        }

        if (IsMalformedShape(finding))
        {
            return Malformed(state);
        }

        if (state.MalformedRecord)
        {
            return Malformed(state);
        }

        if (!IsCurrent(state.EvidenceFreshness))
        {
            return Review(
                0.10,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.EvidenceFreshnessNotCurrent },
                new[] { HealerTreatmentVocab.RequiredEvidence.FreshFileFingerprint },
                Rationale(finding));
        }

        if (!IsCurrent(state.IdentityFreshness))
        {
            return Review(
                0.10,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.IdentityFreshnessNotCurrent },
                new[] { HealerTreatmentVocab.RequiredEvidence.FreshPathIdentity },
                Rationale(finding));
        }

        if (!Enum.IsDefined(typeof(LibraryHealerLabel), finding.Label))
        {
            return Review(
                0.10,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.UnknownFindingLabel },
                new[] { HealerTreatmentVocab.RequiredEvidence.None },
                new[] { HealerTreatmentVocab.Rationale.UnknownFindingLabel });
        }

        var label = LibraryHealerReasonCodes.NormalizeLabel(finding.Label, finding.TagReader.Metadata);

        return label switch
        {
            LibraryHealerLabel.FalsePositive => Plan(
                HealerTreatmentVocab.Workflow.None,
                1.00,
                HealerTreatmentVocab.Risk.None,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.None },
                new[] { HealerTreatmentVocab.RequiredEvidence.None },
                new[] { HealerTreatmentVocab.RequiredPolicyGate.None },
                new[] { HealerTreatmentVocab.Rationale.FalsePositive }),

            LibraryHealerLabel.TagMetadataIssue => Plan(
                HealerTreatmentVocab.Workflow.TagRepairCandidate,
                0.65,
                HealerTreatmentVocab.Risk.Medium,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.CanonicalMetadataValidationMissing,
                    HealerTreatmentVocab.BlockedReason.TagWriteBackupPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.TagRepairNotImplemented,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredEvidence.CanonicalMetadataValidation,
                    HealerTreatmentVocab.RequiredEvidence.MusicBrainzReleaseValidation,
                },
                new[] { HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved },
                Rationale(finding)),

            LibraryHealerLabel.TagReaderSymptom => Plan(
                HealerTreatmentVocab.Workflow.RepairDryRunCandidate,
                0.55,
                HealerTreatmentVocab.Risk.Medium,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.ProbeEvidenceMissing,
                    HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing,
                    HealerTreatmentVocab.BlockedReason.BackupPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.JournalPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredEvidence.FfprobeStreamEvidence,
                    HealerTreatmentVocab.RequiredEvidence.FullDecodeClean,
                    HealerTreatmentVocab.RequiredEvidence.TaglibRereadAfterRewrap,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved,
                    HealerTreatmentVocab.RequiredPolicyGate.JournalPolicyApproved,
                },
                Rationale(finding)),

            LibraryHealerLabel.ProbeEvidence when finding.Probe?.ProbeSucceeded == true => Plan(
                HealerTreatmentVocab.Workflow.RepairDryRunCandidate,
                0.70,
                HealerTreatmentVocab.Risk.Medium,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing,
                    HealerTreatmentVocab.BlockedReason.TaglibRereadAfterRewrapMissing,
                    HealerTreatmentVocab.BlockedReason.BackupPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.JournalPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredEvidence.FullDecodeClean,
                    HealerTreatmentVocab.RequiredEvidence.TaglibRereadAfterRewrap,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved,
                    HealerTreatmentVocab.RequiredPolicyGate.JournalPolicyApproved,
                },
                Rationale(finding)),

            LibraryHealerLabel.NeedsHumanReview => Review(
                0.25,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.HumanReviewRequired },
                new[] { HealerTreatmentVocab.RequiredEvidence.None },
                Rationale(finding)),

            LibraryHealerLabel.PathInconsistency => Review(
                0.70,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.HumanReviewRequired,
                    HealerTreatmentVocab.BlockedReason.PathStateStaleOrMissing,
                },
                new[] { HealerTreatmentVocab.RequiredEvidence.FreshPathIdentity },
                Rationale(finding)),

            _ => Review(
                0.40,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.EvidenceConflict,
                    HealerTreatmentVocab.BlockedReason.HumanReviewRequired,
                },
                new[] { HealerTreatmentVocab.RequiredEvidence.FreshFileFingerprint },
                Rationale(finding)),
        };
    }

    private static HealerTreatmentPlan Review(
        double confidence,
        HealerFindingFreshness freshness,
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> requiredEvidence,
        IReadOnlyList<string> rationale)
    {
        return Plan(
            HealerTreatmentVocab.Workflow.Review,
            confidence,
            HealerTreatmentVocab.Risk.High,
            freshness,
            blockedReasons,
            requiredEvidence,
            new[] { HealerTreatmentVocab.RequiredPolicyGate.None },
            rationale);
    }

    private static HealerTreatmentPlan Malformed(HealerFindingFreshness freshness)
    {
        return Review(
            0.10,
            freshness,
            new[] { HealerTreatmentVocab.BlockedReason.MalformedFindingRecord },
            new[] { HealerTreatmentVocab.RequiredEvidence.None },
            new[] { HealerTreatmentVocab.Rationale.MalformedFindingRecord });
    }

    private static HealerTreatmentPlan Plan(
        string workflow,
        double confidence,
        string risk,
        HealerFindingFreshness freshness,
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> requiredEvidence,
        IReadOnlyList<string> requiredPolicyGates,
        IReadOnlyList<string> rationale)
    {
        return new HealerTreatmentPlan(
            HealerTreatmentVocab.SchemaVersion,
            workflow,
            confidence,
            risk,
            HealerTreatmentVocab.SafetyLevel.ReadOnly,
            freshness.EvidenceFreshness,
            freshness.IdentityFreshness,
            new HealerExecutionAuthorization(
                false,
                HealerTreatmentVocab.AuthorizationAuthority.None,
                HealerTreatmentVocab.AuthorizationReason.A2ReadOnly),
            DistinctOrNone(blockedReasons, HealerTreatmentVocab.BlockedReason.None),
            DistinctOrNone(requiredEvidence, HealerTreatmentVocab.RequiredEvidence.None),
            DistinctOrNone(requiredPolicyGates, HealerTreatmentVocab.RequiredPolicyGate.None),
            DistinctOrNone(rationale, HealerTreatmentVocab.Rationale.None));
    }

    private static IReadOnlyList<string> Rationale(LibraryHealerFinding finding)
    {
        var result = new List<string>();
        foreach (var reason in LibraryHealerReasonCodes.Normalize(finding.InternalReasonCodes, finding.TagReader?.Metadata))
        {
            switch (reason)
            {
                case "FILE_MISSING":
                    result.Add(HealerTreatmentVocab.Rationale.FileMissing);
                    break;
                case "PATH_PROBE_INCONCLUSIVE":
                    result.Add(HealerTreatmentVocab.Rationale.PathProbeInconclusive);
                    break;
                case "TAG_MISSING_TITLE":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingTitle);
                    break;
                case "TAG_MISSING_ARTIST":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingArtist);
                    break;
                case "TAG_MISSING_ALBUM":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingAlbum);
                    break;
                case "TAG_MISSING_MUSICBRAINZID":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingMusicBrainzId);
                    break;
                case "TAG_READER_ZERO_DURATION":
                    result.Add(HealerTreatmentVocab.Rationale.TagReaderDurationZero);
                    break;
                case "TAG_READER_FAILED":
                    result.Add(HealerTreatmentVocab.Rationale.TagReaderReadFailed);
                    break;
                case "PROBE_DURATION_POSITIVE":
                    result.Add(HealerTreatmentVocab.Rationale.ProbeSucceeded);
                    break;
                case "PROBE_FAILED":
                    result.Add(HealerTreatmentVocab.Rationale.ProbeFailed);
                    break;
            }
        }

        if (finding.Label == LibraryHealerLabel.TagReaderSymptom && finding.Probe is null)
        {
            result.Add(HealerTreatmentVocab.Rationale.NoProbeEvidence);
        }

        return DistinctOrNone(result, HealerTreatmentVocab.Rationale.None);
    }

    private static bool IsMalformedShape(LibraryHealerFinding finding)
    {
        return finding.File is null || finding.TagReader is null;
    }

    private static bool IsCurrent(string freshness)
    {
        return string.Equals(freshness, HealerTreatmentVocab.Freshness.Current, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> DistinctOrNone(IReadOnlyList<string> values, string noneValue)
    {
        var clean = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return clean.Length == 0 ? new[] { noneValue } : clean;
    }
}
