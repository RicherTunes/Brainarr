namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LibraryHealerActionHandler
{
    private const string PathContainingValueRedaction = "<path-containing value redacted>";
    private const string EvidenceErrorMessageRedaction = "<evidence error message redacted>";
    private static readonly string[] WorkflowFilterValues =
    {
        HealerTreatmentVocab.Workflow.None,
        HealerTreatmentVocab.Workflow.Review,
        HealerTreatmentVocab.Workflow.RepairDryRunCandidate,
        HealerTreatmentVocab.Workflow.TagRepairCandidate,
        HealerTreatmentVocab.Workflow.ReacquireCandidate,
        HealerTreatmentVocab.Workflow.ReleaseReviewCandidate,
    };
    private static readonly string[] RiskFilterValues =
    {
        HealerTreatmentVocab.Risk.None,
        HealerTreatmentVocab.Risk.Low,
        HealerTreatmentVocab.Risk.Medium,
        HealerTreatmentVocab.Risk.High,
        HealerTreatmentVocab.Risk.Critical,
    };
    private static readonly string[] BlockedReasonFilterValues =
    {
        HealerTreatmentVocab.BlockedReason.None,
        HealerTreatmentVocab.BlockedReason.A2ReadOnly,
        HealerTreatmentVocab.BlockedReason.HumanReviewRequired,
        HealerTreatmentVocab.BlockedReason.EvidenceFreshnessNotCurrent,
        HealerTreatmentVocab.BlockedReason.IdentityFreshnessNotCurrent,
        HealerTreatmentVocab.BlockedReason.PathStateStaleOrMissing,
        HealerTreatmentVocab.BlockedReason.PathProbeInconclusive,
        HealerTreatmentVocab.BlockedReason.ProbeEvidenceMissing,
        HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing,
        HealerTreatmentVocab.BlockedReason.TaglibRereadAfterRewrapMissing,
        HealerTreatmentVocab.BlockedReason.EvidenceConflict,
        HealerTreatmentVocab.BlockedReason.BackupPolicyMissing,
        HealerTreatmentVocab.BlockedReason.JournalPolicyMissing,
        HealerTreatmentVocab.BlockedReason.RollbackGuideMissing,
        HealerTreatmentVocab.BlockedReason.CanonicalMetadataValidationMissing,
        HealerTreatmentVocab.BlockedReason.TagWriteBackupPolicyMissing,
        HealerTreatmentVocab.BlockedReason.TagRepairNotImplemented,
        HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented,
        HealerTreatmentVocab.BlockedReason.RecycleBinPolicyMissing,
        HealerTreatmentVocab.BlockedReason.AlbumWideScopeNotDisclosed,
        HealerTreatmentVocab.BlockedReason.LidarrSearchDryRunNotImplemented,
        HealerTreatmentVocab.BlockedReason.MalformedFindingRecord,
        HealerTreatmentVocab.BlockedReason.UnknownFindingLabel,
    };
    private readonly ILibraryHealerScanRunner _scanRunner;
    private readonly ILibraryHealerFindingStore _store;
    private int _scanInProgress;

    public LibraryHealerActionHandler(ILibraryHealerScanRunner scanRunner, ILibraryHealerFindingStore store)
    {
        _scanRunner = scanRunner ?? throw new ArgumentNullException(nameof(scanRunner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public object Handle(string action, IDictionary<string, string> query)
    {
        return action.ToLowerInvariant() switch
        {
            "healer/scan" => Scan(query),
            "healer/getfindings" => GetFindings(query),
            "healer/getfieldcatalog" => LibraryHealerFieldSensitivityCatalog.Project(),
            "healer/clearfindings" => ClearFindings(),
            _ => throw new NotSupportedException($"Healer action '{action}' is not supported"),
        };
    }

    private object Scan(IDictionary<string, string> query)
    {
        if (Interlocked.Exchange(ref _scanInProgress, 1) == 1)
        {
            return new
            {
                ok = false,
                status = LibraryHealerScanStatus.Running.ToString(),
                error = "A Library Healer scan is already running",
            };
        }

        try
        {
            var request = ParseScanRequest(query);
            var result = _scanRunner.Scan(request, CancellationToken.None);
            return new
            {
                ok = result.Status == LibraryHealerScanStatus.Completed,
                status = result.Status.ToString(),
                totalArtists = result.TotalArtists,
                availableTrackFiles = result.AvailableTrackFiles,
                scannedTrackFiles = result.ScannedTrackFiles,
                persistedFindings = result.PersistedFindings,
                truncated = result.Truncated,
                nextAfterTrackFileId = result.NextAfterTrackFileId,
                error = SanitizeMessageString(result.ErrorMessage),
            };
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }
    }

    private static LibraryHealerScanRequest ParseScanRequest(IDictionary<string, string> query)
    {
        var artistId = TryParsePositiveQueryInt(query, "artistId");
        var afterTrackFileId = TryParsePositiveQueryInt(query, "afterTrackFileId");
        var maxFiles = Math.Clamp(TryParseQueryInt(query, "maxFiles") ?? 100, 1, 500);
        var maxSeconds = Math.Clamp(TryParseQueryInt(query, "maxSeconds") ?? 10, 1, 30);

        return new LibraryHealerScanRequest(artistId, afterTrackFileId, maxFiles, maxSeconds);
    }

    private object GetFindings(IDictionary<string, string> query)
    {
        var limit = Math.Clamp(TryParseQueryInt(query, "limit") ?? 100, 1, 500);
        var findings = HasActiveFindingFilters(query)
            ? _store.GetAllRecent()
            : _store.GetRecent(limit);
        var projectedPlans = findings
            .Select(SanitizeFindingForProjection)
            .Select(finding => new ProjectedPlan(
                finding,
                HealerTriageAdvisor.Advise(finding.Finding, finding.Freshness)))
            .Where(plan => MatchesFindingFilters(query, plan.TreatmentPlan))
            .Take(limit)
            .ToList();
        var plans = projectedPlans
            .Select(plan => plan.TreatmentPlan)
            .ToList();
        var items = projectedPlans
            .Select(plan => ProjectFinding(plan.Finding.Finding, plan.TreatmentPlan))
            .ToList();

        return new
        {
            items,
            summary = ProjectSummary(HealerTriageSummary.Create(plans)),
        };
    }

    private static bool HasActiveFindingFilters(IDictionary<string, string> query)
    {
        return ParseKnownFilterValues(query, "workflow", WorkflowFilterValues).Count > 0
            || ParseKnownFilterValues(query, "risk", RiskFilterValues).Count > 0
            || ParseKnownFilterValues(query, "blockedReason", BlockedReasonFilterValues).Count > 0
            || TryParseQueryBool(query, "authorized") is not null;
    }

    private static bool MatchesFindingFilters(IDictionary<string, string> query, HealerTreatmentPlan plan)
    {
        if (!MatchesScalarFilter(query, "workflow", plan.CandidateWorkflow, WorkflowFilterValues))
        {
            return false;
        }

        if (!MatchesScalarFilter(query, "risk", plan.Risk, RiskFilterValues))
        {
            return false;
        }

        if (!MatchesListFilter(query, "blockedReason", plan.BlockedReasons, BlockedReasonFilterValues))
        {
            return false;
        }

        var authorized = TryParseQueryBool(query, "authorized");
        return authorized is null || plan.ExecutionAuthorization.Authorized == authorized.Value;
    }

    private static bool MatchesScalarFilter(
        IDictionary<string, string> query,
        string key,
        string value,
        IReadOnlyList<string> allowedValues)
    {
        var filters = ParseKnownFilterValues(query, key, allowedValues);
        return filters.Count == 0
            || filters.Any(filter => string.Equals(filter, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesListFilter(
        IDictionary<string, string> query,
        string key,
        IReadOnlyList<string> values,
        IReadOnlyList<string> allowedValues)
    {
        var filters = ParseKnownFilterValues(query, key, allowedValues);
        return filters.Count == 0
            || values.Any(value => filters.Any(filter => string.Equals(filter, value, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> ParseKnownFilterValues(
        IDictionary<string, string> query,
        string key,
        IReadOnlyList<string> allowedValues)
    {
        if (query == null || !query.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => allowedValues.FirstOrDefault(allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase)))
            .Where(value => value != null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool? TryParseQueryBool(IDictionary<string, string> query, string key)
    {
        if (query != null
            && query.TryGetValue(key, out var raw)
            && bool.TryParse(raw?.Trim(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private object ClearFindings()
    {
        if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0)
        {
            return new
            {
                ok = false,
                status = LibraryHealerScanStatus.Running.ToString(),
                error = "Cannot clear Library Healer findings while a scan is running",
            };
        }

        try
        {
            _store.Clear();
            return new { ok = true };
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }
    }

    private static ProjectedFinding SanitizeFindingForProjection(LibraryHealerFinding? finding)
    {
        if (finding is null)
        {
            return new ProjectedFinding(
                EmptyMalformedFinding(),
                HealerFindingFreshness.Current with { MalformedRecord = true });
        }

        var malformedRecord = IsMalformedStoredFinding(finding);
        var file = finding.File is null ? EmptyFileIdentity() : finding.File with
        {
            RedactedPath = SanitizePathDisplayString(finding.File.RedactedPath) ?? string.Empty,
            PathHash = SanitizeTokenString(finding.File.PathHash) ?? string.Empty,
            Size = finding.File.Size.GetValueOrDefault() < 0 ? null : finding.File.Size,
        };
        var tagReader = finding.TagReader is null ? EmptyTagReaderEvidence() : ProjectTagReader(finding.TagReader);
        var probe = finding.Probe is null ? null : ProjectProbe(finding.Probe);
        var label = malformedRecord
            ? LibraryHealerLabel.NeedsHumanReview
            : LibraryHealerReasonCodes.NormalizeLabel(finding.Label, tagReader.Metadata);
        var reasons = malformedRecord
            ? new[] { HealerTreatmentVocab.BlockedReason.MalformedFindingRecord }
            : LibraryHealerReasonCodes.Normalize(finding.InternalReasonCodes, tagReader.Metadata);

        return new ProjectedFinding(
            finding with
            {
                Id = SanitizeTokenString(finding.Id) ?? string.Empty,
                File = file,
                Label = label,
                InternalReasonCodes = reasons,
                TagReader = tagReader,
                Probe = probe,
            },
            HealerFindingFreshness.Current with { MalformedRecord = malformedRecord });
    }

    private static LibraryHealerFinding EmptyMalformedFinding()
    {
        return new LibraryHealerFinding(
            string.Empty,
            EmptyFileIdentity(),
            LibraryHealerLabel.NeedsHumanReview,
            new[] { HealerTreatmentVocab.BlockedReason.MalformedFindingRecord },
            EmptyTagReaderEvidence(),
            null,
            DateTime.MinValue);
    }

    private static LibraryHealerFileIdentity EmptyFileIdentity()
    {
        return new LibraryHealerFileIdentity(
            TrackFileId: 0,
            ArtistId: 0,
            AlbumId: 0,
            RedactedPath: string.Empty,
            PathHash: string.Empty,
            Size: null,
            ModifiedUtc: null);
    }

    private static TagReaderEvidence EmptyTagReaderEvidence()
    {
        return new TagReaderEvidence(
            ReadAttempted: false,
            ReadSucceeded: false,
            DurationSeconds: null,
            ErrorType: null,
            ErrorMessage: null);
    }

    private static bool IsMalformedStoredFinding(LibraryHealerFinding finding)
    {
        return finding.File is null
            || finding.TagReader is null
            || !Enum.IsDefined(finding.Label)
            || IsMalformedFileIdentity(finding.File)
            // A TagMetadataIssue with absent metadata is unclassifiable: GetMissingFields(null) returns
            // empty, which NormalizeLabel would otherwise treat as "all tags present" and silently
            // downgrade to FalsePositive (a false-negative). Fail closed -> NeedsHumanReview.
            || (finding.Label == LibraryHealerLabel.TagMetadataIssue && finding.TagReader.Metadata is null);
    }

    private static bool IsMalformedFileIdentity(LibraryHealerFileIdentity? file)
    {
        return file is null
            || file.TrackFileId <= 0
            || file.ArtistId <= 0
            || file.AlbumId <= 0
            || string.IsNullOrWhiteSpace(file.RedactedPath)
            || string.IsNullOrWhiteSpace(file.PathHash)
            || file.Size.GetValueOrDefault() < 0;
    }

    private static object ProjectFinding(LibraryHealerFinding finding, HealerTreatmentPlan treatmentPlan)
    {
        return new
        {
            id = finding.Id,
            trackFileId = finding.File.TrackFileId,
            artistId = finding.File.ArtistId,
            albumId = finding.File.AlbumId,
            path = finding.File.RedactedPath,
            pathHash = finding.File.PathHash,
            size = finding.File.Size,
            modifiedUtc = finding.File.ModifiedUtc,
            label = finding.Label.ToString(),
            reasons = finding.InternalReasonCodes,
            observedAtUtc = finding.ObservedAtUtc,
            tagReader = finding.TagReader,
            probe = finding.Probe,
            treatmentPlan = ProjectTreatmentPlan(treatmentPlan),
        };
    }

    private static object ProjectTreatmentPlan(HealerTreatmentPlan plan)
    {
        return new
        {
            schemaVersion = plan.SchemaVersion,
            candidateWorkflow = plan.CandidateWorkflow,
            confidence = plan.Confidence,
            risk = plan.Risk,
            safetyLevel = plan.SafetyLevel,
            evidenceFreshness = plan.EvidenceFreshness,
            identityFreshness = plan.IdentityFreshness,
            executionAuthorization = new
            {
                authorized = plan.ExecutionAuthorization.Authorized,
                authority = plan.ExecutionAuthorization.Authority,
                reason = plan.ExecutionAuthorization.Reason,
            },
            blockedReasons = plan.BlockedReasons,
            requiredEvidence = plan.RequiredEvidence,
            requiredPolicyGates = plan.RequiredPolicyGates,
            rationaleCodes = plan.RationaleCodes,
        };
    }

    private static object ProjectSummary(HealerTriageSummary summary)
    {
        return new
        {
            total = summary.Total,
            byWorkflow = summary.ByWorkflow,
            byRisk = summary.ByRisk,
            byWorkflowByRisk = summary.ByWorkflowByRisk,
            authorization = summary.Authorization,
            blockedReasons = summary.BlockedReasons,
        };
    }

    private static TagReaderEvidence ProjectTagReader(TagReaderEvidence evidence)
    {
        return new TagReaderEvidence(
            evidence.ReadAttempted,
            evidence.ReadSucceeded,
            evidence.DurationSeconds,
            SanitizeTokenString(evidence.ErrorType),
            SanitizeEvidenceMessageString(evidence.ErrorMessage),
            ProjectMetadata(evidence.Metadata));
    }

    private static TagMetadataEvidence? ProjectMetadata(TagMetadataEvidence? evidence)
    {
        if (evidence is null)
        {
            return null;
        }

        return evidence with
        {
            MissingFields = TagMetadataFields.GetMissingFields(evidence),
        };
    }

    private static ProbeEvidence ProjectProbe(ProbeEvidence evidence)
    {
        return new ProbeEvidence(
            evidence.ProbeAttempted,
            evidence.ProbeSucceeded,
            evidence.DurationSeconds,
            SanitizeTokenString(evidence.Container),
            SanitizeTokenString(evidence.AudioCodec),
            SanitizeTokenString(evidence.ErrorType),
            SanitizeEvidenceMessageString(evidence.ErrorMessage));
    }

    internal static string? SanitizeBoundaryString(string? value)
    {
        return SanitizeMessageString(value);
    }

    private static string? SanitizePathDisplayString(string? value)
    {
        var redacted = PathPrivacy.RedactDisplayPath(value);
        return ContainsUnsafeDisplayPathMaterial(redacted)
            ? PathContainingValueRedaction
            : redacted;
    }

    private static string? SanitizeTokenString(string? value)
    {
        return ShouldRedactTokenMaterial(value)
            ? PathContainingValueRedaction
            : value;
    }

    private static string? SanitizeMessageString(string? value)
    {
        var redacted = PathPrivacy.RedactMessage(value);
        return ContainsSensitiveMessagePathMaterial(value) || ContainsSensitiveMessagePathMaterial(redacted)
            ? PathContainingValueRedaction
            : redacted;
    }

    private static string? SanitizeEvidenceMessageString(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : EvidenceErrorMessageRedaction;
    }

    private static bool ShouldRedactTokenMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ContainsLikelyPath(trimmed)
            || HasDriveDesignator(trimmed)
            || ContainsMediaExtension(trimmed)
            || LibraryHealerSensitiveText.ContainsMetadataMaterial(trimmed)
            || LibraryHealerSensitiveText.ContainsCommandMaterial(trimmed)
            || trimmed.Any(char.IsWhiteSpace);
    }

    private static bool ContainsUnsafeDisplayPathMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var displayName = GetDisplayNamePart(value.Trim());
        return ContainsLikelyPath(displayName)
            || HasDriveDesignator(displayName)
            || LibraryHealerSensitiveText.ContainsMetadataMaterial(displayName)
            || LibraryHealerSensitiveText.ContainsCommandMaterial(displayName);
    }

    private static string GetDisplayNamePart(string value)
    {
        var separator = value.LastIndexOf('#');
        if (separator <= 0 || separator + 13 != value.Length)
        {
            return value;
        }

        var candidateHash = value.Substring(separator + 1);
        for (var i = 0; i < candidateHash.Length; i++)
        {
            var ch = candidateHash[i];
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
            {
                return value;
            }
        }

        return value.Substring(0, separator);
    }

    private static bool ContainsLikelyPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return HasWindowsRoot(value)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || HasUnixRoot(value);
    }

    private static bool ContainsSensitiveMessagePathMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ContainsLikelyPath(value)
            || HasDriveDesignator(value)
            || ContainsMediaExtension(value)
            || LibraryHealerSensitiveText.ContainsMetadataMaterial(value)
            || LibraryHealerSensitiveText.ContainsCommandMaterial(value);
    }

    private static bool HasWindowsRoot(string value)
    {
        for (var i = 0; i + 2 < value.Length; i++)
        {
            if (char.IsLetter(value[i])
                && value[i + 1] == ':'
                && (value[i + 2] == '\\' || value[i + 2] == '/'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDriveDesignator(string value)
    {
        for (var i = 0; i + 1 < value.Length; i++)
        {
            if (char.IsLetter(value[i]) && value[i + 1] == ':')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMediaExtension(string value)
    {
        var mediaExtensions = new[]
        {
            ".aac",
            ".aif",
            ".aiff",
            ".alac",
            ".ape",
            ".flac",
            ".m4a",
            ".mka",
            ".mp3",
            ".mp4",
            ".ogg",
            ".opus",
            ".wav",
            ".wv",
        };

        return mediaExtensions.Any(extension => value.Contains(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasUnixRoot(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '/')
            {
                continue;
            }

            if (i == 0 || char.IsWhiteSpace(value[i - 1]) || value[i - 1] is '"' or '\'' or '(' or '[')
            {
                return true;
            }
        }

        return false;
    }

    private static int? TryParseQueryInt(IDictionary<string, string> query, string key)
    {
        if (query != null && query.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? TryParsePositiveQueryInt(IDictionary<string, string> query, string key)
    {
        var parsed = TryParseQueryInt(query, key);
        return parsed.GetValueOrDefault() > 0 ? parsed : null;
    }

    private sealed record ProjectedFinding(LibraryHealerFinding Finding, HealerFindingFreshness Freshness);
    private sealed record ProjectedPlan(ProjectedFinding Finding, HealerTreatmentPlan TreatmentPlan);
}
