namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LibraryHealerActionHandler
{
    private const string PathContainingValueRedaction = "<path-containing value redacted>";
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
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(request.MaxSeconds, 1, 30)));
            var result = _scanRunner.Scan(request, cancellation.Token);
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
        var items = _store.GetRecent(limit)
            .Select(ProjectFinding)
            .ToList();

        return new { items };
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

    private static object ProjectFinding(LibraryHealerFinding finding)
    {
        return new
        {
            id = SanitizeTokenString(finding.Id),
            trackFileId = finding.File.TrackFileId,
            artistId = finding.File.ArtistId,
            albumId = finding.File.AlbumId,
            path = SanitizePathDisplayString(finding.File.RedactedPath),
            pathHash = SanitizeTokenString(finding.File.PathHash),
            size = finding.File.Size,
            modifiedUtc = finding.File.ModifiedUtc,
            label = finding.Label.ToString(),
            reasons = finding.InternalReasonCodes.Select(SanitizeTokenString).ToList(),
            observedAtUtc = finding.ObservedAtUtc,
            tagReader = ProjectTagReader(finding.TagReader),
            probe = finding.Probe is null ? null : ProjectProbe(finding.Probe),
        };
    }

    private static TagReaderEvidence ProjectTagReader(TagReaderEvidence evidence)
    {
        return new TagReaderEvidence(
            evidence.ReadAttempted,
            evidence.ReadSucceeded,
            evidence.DurationSeconds,
            SanitizeTokenString(evidence.ErrorType),
            SanitizeMessageString(evidence.ErrorMessage));
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
            SanitizeMessageString(evidence.ErrorMessage));
    }

    internal static string? SanitizeBoundaryString(string? value)
    {
        return SanitizeMessageString(value);
    }

    private static string? SanitizePathDisplayString(string? value)
    {
        return PathPrivacy.RedactDisplayPath(value);
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
            || trimmed.Any(char.IsWhiteSpace);
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
            || ContainsMediaExtension(value);
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
}
