namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record HealerExecutionAuthorization(
    bool Authorized,
    string Authority,
    string Reason);

public sealed record HealerFindingFreshness(
    string EvidenceFreshness,
    string IdentityFreshness,
    bool MalformedRecord = false)
{
    public static HealerFindingFreshness Current { get; } = new(
        HealerTreatmentVocab.Freshness.Current,
        HealerTreatmentVocab.Freshness.Current);
}

public sealed record HealerTreatmentPlan(
    int SchemaVersion,
    string CandidateWorkflow,
    double Confidence,
    string Risk,
    string SafetyLevel,
    string EvidenceFreshness,
    string IdentityFreshness,
    HealerExecutionAuthorization ExecutionAuthorization,
    IReadOnlyList<string> BlockedReasons,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> RequiredPolicyGates,
    IReadOnlyList<string> RationaleCodes);

public static class HealerTreatmentVocab
{
    public const int SchemaVersion = 1;

    public static class Workflow
    {
        public const string None = "none";
        public const string Review = "review";
        public const string RepairDryRunCandidate = "repairDryRunCandidate";
        public const string TagRepairCandidate = "tagRepairCandidate";
        public const string ReacquireCandidate = "reacquireCandidate";
        public const string ReleaseReviewCandidate = "releaseReviewCandidate";
    }

    public static class Risk
    {
        public const string None = "none";
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string Critical = "critical";
    }

    public static class SafetyLevel
    {
        public const string ReadOnly = "readOnly";
    }

    public static class Freshness
    {
        public const string Current = "current";
        public const string Stale = "stale";
        public const string Missing = "missing";
        public const string Unknown = "unknown";
    }

    public static class AuthorizationAuthority
    {
        public const string None = "none";
    }

    public static class AuthorizationReason
    {
        public const string A2ReadOnly = "A2_READ_ONLY";
    }

    public static class BlockedReason
    {
        public const string None = "NONE";
        public const string HumanReviewRequired = "HUMAN_REVIEW_REQUIRED";
        public const string EvidenceFreshnessNotCurrent = "EVIDENCE_FRESHNESS_NOT_CURRENT";
        public const string IdentityFreshnessNotCurrent = "IDENTITY_FRESHNESS_NOT_CURRENT";
        public const string PathStateStaleOrMissing = "PATH_STATE_STALE_OR_MISSING";
        public const string PathProbeInconclusive = "PATH_PROBE_INCONCLUSIVE";
        public const string ProbeEvidenceMissing = "PROBE_EVIDENCE_MISSING";
        public const string FullDecodeEvidenceMissing = "FULL_DECODE_EVIDENCE_MISSING";
        public const string TaglibRereadAfterRewrapMissing = "TAGLIB_REREAD_AFTER_REWRAP_MISSING";
        public const string EvidenceConflict = "EVIDENCE_CONFLICT";
        public const string BackupPolicyMissing = "BACKUP_POLICY_MISSING";
        public const string JournalPolicyMissing = "JOURNAL_POLICY_MISSING";
        public const string RollbackGuideMissing = "ROLLBACK_GUIDE_MISSING";
        public const string CanonicalMetadataValidationMissing = "CANONICAL_METADATA_VALIDATION_MISSING";
        public const string TagWriteBackupPolicyMissing = "TAG_WRITE_BACKUP_POLICY_MISSING";
        public const string TagRepairNotImplemented = "TAG_REPAIR_NOT_IMPLEMENTED";
        public const string RepairDryRunNotImplemented = "REPAIR_DRY_RUN_NOT_IMPLEMENTED";
        public const string RecycleBinPolicyMissing = "RECYCLE_BIN_POLICY_MISSING";
        public const string AlbumWideScopeNotDisclosed = "ALBUM_WIDE_SCOPE_NOT_DISCLOSED";
        public const string LidarrSearchDryRunNotImplemented = "LIDARR_SEARCH_DRY_RUN_NOT_IMPLEMENTED";
        public const string MalformedFindingRecord = "MALFORMED_FINDING_RECORD";
        public const string UnknownFindingLabel = "UNKNOWN_FINDING_LABEL";
    }

    public static class RequiredEvidence
    {
        public const string None = "NONE";
        public const string FreshFileFingerprint = "FRESH_FILE_FINGERPRINT";
        public const string FreshPathIdentity = "FRESH_PATH_IDENTITY";
        public const string FfprobeStreamEvidence = "FFPROBE_STREAM_EVIDENCE";
        public const string FfmpegAvailable = "FFMPEG_AVAILABLE";
        public const string FullDecodeClean = "FULL_DECODE_CLEAN";
        public const string FullDecodeFatal = "FULL_DECODE_FATAL";
        public const string TaglibRereadAfterRewrap = "TAGLIB_REREAD_AFTER_REWRAP";
        public const string CanonicalMetadataValidation = "CANONICAL_METADATA_VALIDATION";
        public const string MusicBrainzReleaseValidation = "MUSICBRAINZ_RELEASE_VALIDATION";
        public const string LidarrSearchDryRunResult = "LIDARR_SEARCH_DRY_RUN_RESULT";
    }

    public static class RequiredPolicyGate
    {
        public const string None = "NONE";
        public const string BackupPolicyApproved = "BACKUP_POLICY_APPROVED";
        public const string JournalPolicyApproved = "JOURNAL_POLICY_APPROVED";
        public const string RollbackGuidePublished = "ROLLBACK_GUIDE_PUBLISHED";
        public const string RecycleBinConfigured = "RECYCLE_BIN_CONFIGURED";
        public const string AlbumWideScopeAccepted = "ALBUM_WIDE_SCOPE_ACCEPTED";
        public const string ExplicitOperatorOptIn = "EXPLICIT_OPERATOR_OPT_IN";
    }

    public static class Rationale
    {
        public const string None = "NONE";
        public const string FalsePositive = "FALSE_POSITIVE";
        public const string FileMissing = "FILE_MISSING";
        public const string PathProbeInconclusive = "PATH_PROBE_INCONCLUSIVE";
        public const string TagMetadataMissingTitle = "TAG_METADATA_MISSING_TITLE";
        public const string TagMetadataMissingArtist = "TAG_METADATA_MISSING_ARTIST";
        public const string TagMetadataMissingAlbum = "TAG_METADATA_MISSING_ALBUM";
        public const string TagMetadataMissingMusicBrainzId = "TAG_METADATA_MISSING_MUSICBRAINZ_ID";
        public const string TagReaderDurationZero = "TAG_READER_DURATION_ZERO";
        public const string TagReaderReadFailed = "TAG_READER_READ_FAILED";
        public const string NoProbeEvidence = "NO_PROBE_EVIDENCE";
        public const string ProbeSucceeded = "PROBE_SUCCEEDED";
        public const string ProbeFailed = "PROBE_FAILED";
        public const string EvidenceConflict = "EVIDENCE_CONFLICT";
        public const string DecodeFatal = "DECODE_FATAL";
        public const string DecodeMateriallyShort = "DECODE_MATERIALLY_SHORT";
        public const string StaleFileIdentity = "STALE_FILE_IDENTITY";
        public const string MalformedFindingRecord = "MALFORMED_FINDING_RECORD";
        public const string UnknownFindingLabel = "UNKNOWN_FINDING_LABEL";
    }
}
