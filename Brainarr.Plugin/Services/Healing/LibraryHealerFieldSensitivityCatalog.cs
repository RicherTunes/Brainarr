namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public static class LibraryHealerFieldSensitivityCatalog
{
    public const int SchemaVersion = 1;
    private const string GetFindingsAction = "healer/getfindings";

    private static readonly IReadOnlyList<LibraryHealerFieldSensitivityEntry> Entries =
    [
        LocalIdentifier("items[].albumId", "Lidarr album ids are local library identifiers."),
        LocalIdentifier("items[].artistId", "Lidarr artist ids are local library identifiers."),
        RedactedIdentifier("items[].id", "Finding ids are stable correlation tokens for local diagnostics."),
        Public("items[].label", "Fixed diagnostic label vocabulary."),
        PrivateFileFact("items[].modifiedUtc", "File timestamps can fingerprint local library content."),
        PrivateFileFact("items[].observedAtUtc", "Observation times can reveal local scan cadence."),
        RedactedPathIdentifier("items[].path", "Redacted display paths can still contain filename-level clues."),
        RedactedPathIdentifier("items[].pathHash", "Stable path hashes can correlate the same private file across reports."),
        PrivateDiagnostic("items[].probe.AudioCodec", "Probe codec tokens are diagnostic-only until probe collection owns an allowlisted vocabulary."),
        PrivateDiagnostic("items[].probe.Container", "Probe container tokens are diagnostic-only until probe collection owns an allowlisted vocabulary."),
        PrivateFileFact("items[].probe.DurationSeconds", "Durations can help identify specific tracks."),
        PrivateDiagnostic("items[].probe.ErrorMessage", "Probe error text is redacted but remains diagnostic-only."),
        PrivateDiagnostic("items[].probe.ErrorType", "Probe error tokens are diagnostic-only."),
        Public("items[].probe.ProbeAttempted", "Boolean probe status."),
        Public("items[].probe.ProbeSucceeded", "Boolean probe status."),
        Public("items[].reasons[]", "Fixed diagnostic reason-code vocabulary."),
        PrivateFileFact("items[].size", "File sizes can fingerprint local library content."),
        PrivateFileFact("items[].tagReader.DurationSeconds", "Durations can help identify specific tracks."),
        PrivateDiagnostic("items[].tagReader.ErrorMessage", "Tag reader error text is redacted but remains diagnostic-only."),
        PrivateDiagnostic("items[].tagReader.ErrorType", "Tag reader error tokens are diagnostic-only."),
        Public("items[].tagReader.Metadata.AlbumPresent", "Boolean tag-presence evidence."),
        Public("items[].tagReader.Metadata.AnyMusicBrainzIdPresent", "Boolean MusicBrainz tag-presence evidence."),
        Public("items[].tagReader.Metadata.ArtistPresent", "Boolean tag-presence evidence."),
        Public("items[].tagReader.Metadata.MissingFields[]", "Fixed missing-field vocabulary."),
        Public("items[].tagReader.Metadata.TitlePresent", "Boolean tag-presence evidence."),
        Public("items[].tagReader.ReadAttempted", "Boolean tag-reader status."),
        Public("items[].tagReader.ReadSucceeded", "Boolean tag-reader status."),
        LocalIdentifier("items[].trackFileId", "Lidarr TrackFile ids are local library identifiers."),
        Public("items[].treatmentPlan.blockedReasons[]", "Fixed treatment blocked-reason vocabulary."),
        Public("items[].treatmentPlan.candidateWorkflow", "Fixed treatment workflow vocabulary."),
        Public("items[].treatmentPlan.confidence", "Deterministic planner confidence."),
        Public("items[].treatmentPlan.evidenceFreshness", "Fixed freshness vocabulary."),
        Public("items[].treatmentPlan.executionAuthorization.authority", "Fixed authorization authority vocabulary."),
        Public("items[].treatmentPlan.executionAuthorization.authorized", "Boolean authorization state."),
        Public("items[].treatmentPlan.executionAuthorization.reason", "Fixed authorization reason vocabulary."),
        Public("items[].treatmentPlan.identityFreshness", "Fixed freshness vocabulary."),
        Public("items[].treatmentPlan.rationaleCodes[]", "Fixed treatment rationale vocabulary."),
        Public("items[].treatmentPlan.requiredEvidence[]", "Fixed required-evidence vocabulary."),
        Public("items[].treatmentPlan.requiredPolicyGates[]", "Fixed required-policy-gate vocabulary."),
        Public("items[].treatmentPlan.risk", "Fixed treatment risk vocabulary."),
        Public("items[].treatmentPlan.safetyLevel", "Fixed treatment safety-level vocabulary."),
        Public("items[].treatmentPlan.schemaVersion", "Treatment contract version."),
        Public("summary.authorization.authorized", "Aggregate count."),
        Public("summary.authorization.unauthorized", "Aggregate count."),
        Public("summary.blockedReasons", "Aggregate blocked-reason counts keyed by fixed vocabulary."),
        Public("summary.byRisk", "Aggregate risk counts keyed by fixed vocabulary."),
        Public("summary.byWorkflow", "Aggregate workflow counts keyed by fixed vocabulary."),
        Public("summary.byWorkflowByRisk", "Aggregate workflow/risk counts keyed by fixed vocabulary."),
        Public("summary.total", "Aggregate count."),
    ];

    public static object Project()
    {
        return new
        {
            schemaVersion = SchemaVersion,
            action = GetFindingsAction,
            fields = Entries.Select(entry => new
            {
                field = entry.Field,
                sensitivity = entry.Sensitivity,
                localDiagnosticExport = entry.LocalDiagnosticExport,
                shareableSupportExport = entry.ShareableSupportExport,
                aiPrompt = entry.AiPrompt,
                reason = entry.Reason,
            }),
        };
    }

    private static LibraryHealerFieldSensitivityEntry Public(string field, string reason)
    {
        return new LibraryHealerFieldSensitivityEntry(field, "public", true, true, true, reason);
    }

    private static LibraryHealerFieldSensitivityEntry LocalIdentifier(string field, string reason)
    {
        return new LibraryHealerFieldSensitivityEntry(field, "local_identifier", true, false, false, reason);
    }

    private static LibraryHealerFieldSensitivityEntry RedactedIdentifier(string field, string reason)
    {
        return new LibraryHealerFieldSensitivityEntry(field, "redacted_identifier", true, false, false, reason);
    }

    private static LibraryHealerFieldSensitivityEntry RedactedPathIdentifier(string field, string reason)
    {
        return new LibraryHealerFieldSensitivityEntry(field, "redacted_path_identifier", true, false, false, reason);
    }

    private static LibraryHealerFieldSensitivityEntry PrivateFileFact(string field, string reason)
    {
        return new LibraryHealerFieldSensitivityEntry(field, "private_file_fact", true, false, false, reason);
    }

    private static LibraryHealerFieldSensitivityEntry PrivateDiagnostic(string field, string reason)
    {
        return new LibraryHealerFieldSensitivityEntry(field, "private_diagnostic", true, false, false, reason);
    }

    private sealed record LibraryHealerFieldSensitivityEntry(
        string Field,
        string Sensitivity,
        bool LocalDiagnosticExport,
        bool ShareableSupportExport,
        bool AiPrompt,
        string Reason);
}
