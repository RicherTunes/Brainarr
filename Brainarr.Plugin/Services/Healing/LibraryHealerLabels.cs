namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public enum LibraryHealerLabel
{
    FalsePositive = 0,
    TagReaderSymptom = 1,
    ProbeEvidence = 2,
    NeedsHumanReview = 3,
    PathInconsistency = 4,
    TagMetadataIssue = 5
}

public enum LibraryHealerEvidenceKind
{
    FileFingerprint = 0,
    TagReader = 1,
    Probe = 2,
    Classifier = 3
}

public enum LibraryHealerScanStatus
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
