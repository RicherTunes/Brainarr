using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Storage;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public interface ILibraryHealerFindingStore
{
    /// <summary>
    /// Persists a batch of findings. Returns <c>true</c> only when the batch is known to have
    /// reached disk; returns <c>false</c> (without throwing) when the underlying write failed,
    /// so callers must not report the batch as persisted.
    /// </summary>
    bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings);

    IReadOnlyList<LibraryHealerFinding> GetRecent(int limit);

    IReadOnlyList<LibraryHealerFinding> GetAllRecent();

    void Clear();
}

public sealed class LibraryHealerFindingStore : ILibraryHealerFindingStore
{
    private const string FileName = "library_healer_findings.json";
    private const int MaxEntries = 5000;
    private const int MaxRecentLimit = 500;
    private const string PathContainingMessageRedaction = "<path-containing message redacted>";

    private readonly Logger _logger;
    private readonly JsonFileStore<string, LibraryHealerFinding> _store;

    public LibraryHealerFindingStore(string? dataPath = null, Logger? logger = null)
    {
        _logger = logger ?? LogManager.GetCurrentClassLogger();
        var root = dataPath ?? PluginConfigRoots.Resolve("Brainarr");
        Directory.CreateDirectory(root);
        _store = new JsonFileStore<string, LibraryHealerFinding>(
            Path.Combine(root, FileName),
            new JsonFileStoreOptions<string>
            {
                MaxEntries = MaxEntries,
                KeyNormalizer = static key => NormalizeKey(key),
                KeyComparer = StringComparer.OrdinalIgnoreCase,
                // Findings persistence must not lie about success: propagate write failures so
                // SaveBatch can report zero-persisted instead of silently pretending the batch
                // reached disk (Common's default is best-effort/no-throw, which is wrong here).
                ThrowOnSaveFailure = true,
            },
            logger: NLogAdapterFactory.CreateILogger(_logger));
    }

    public bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
    {
        if (findings is null || findings.Count == 0)
        {
            return true;
        }

        var sanitized = findings
            .Where(static finding => finding is not null)
            .Select(Sanitize)
            .ToList();
        if (sanitized.Count == 0)
        {
            return true;
        }

        var entries = sanitized
            .Select(static finding => new KeyValuePair<string, LibraryHealerFinding>(finding.Id, finding))
            .ToList();

        try
        {
            SafeAsyncHelper.RunSafeSync(async () =>
            {
                await _store.SetManyAsync(entries).ConfigureAwait(false);
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warn(
                ex,
                "Library healer finding store failed to persist {0} pending finding(s); reporting zero persisted",
                entries.Count);
            return false;
        }
    }

    public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaxRecentLimit);
        return GetAllRecent()
            .Take(boundedLimit)
            .ToList();
    }

    public IReadOnlyList<LibraryHealerFinding> GetAllRecent()
    {
        return EnumerateAll()
            .OrderByDescending(static finding => finding.ObservedAtUtc)
            .ToList();
    }

    public void Clear()
    {
        try
        {
            SafeAsyncHelper.RunSafeSync(async () => await _store.ClearAsync().ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Preserve Clear()'s existing best-effort contract: ThrowOnSaveFailure above makes
            // SetManyAsync/ClearAsync propagate write failures so SaveBatch can observe them, but
            // callers of Clear() (the healer/clearfindings action) still expect a void, non-throwing
            // API -- log it instead of changing that contract.
            _logger.Warn(ex, "Library healer finding store failed to clear persisted findings");
        }
    }

    private List<LibraryHealerFinding> EnumerateAll()
    {
        return SafeAsyncHelper.RunSafeSync(async () =>
        {
            var all = new List<LibraryHealerFinding>();
            var enumerator = _store.EnumerateAsync().GetAsyncEnumerator();
            try
            {
                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    if (enumerator.Current.Value is not null)
                    {
                        all.Add(enumerator.Current.Value);
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            return all;
        });
    }

    private static LibraryHealerFinding Sanitize(LibraryHealerFinding finding)
    {
        var redactedPath = PathPrivacy.RedactDisplayPath(finding.File.RedactedPath);
        var pathHash = SanitizePathHash(finding.File.PathHash);
        var file = finding.File with
        {
            RedactedPath = redactedPath,
            PathHash = pathHash,
        };

        var tagReaderMessage = SanitizeMessage(finding.TagReader.ErrorMessage);
        var tagReader = finding.TagReader with
        {
            ErrorType = SanitizePathLikeToken(finding.TagReader.ErrorType, "tag-error-"),
            ErrorMessage = tagReaderMessage,
            Metadata = SanitizeMetadata(finding.TagReader.Metadata),
        };

        var probe = finding.Probe;
        if (probe is not null)
        {
            var probeMessage = SanitizeMessage(probe.ErrorMessage);
            probe = probe with
            {
                Container = SanitizePathLikeToken(probe.Container, "probe-container-"),
                AudioCodec = SanitizePathLikeToken(probe.AudioCodec, "probe-codec-"),
                ErrorType = SanitizePathLikeToken(probe.ErrorType, "probe-error-"),
                ErrorMessage = probeMessage,
            };
        }

        return finding with
        {
            Id = SanitizeFindingId(finding.Id),
            File = file,
            Label = LibraryHealerReasonCodes.NormalizeLabel(finding.Label, tagReader.Metadata),
            InternalReasonCodes = LibraryHealerReasonCodes.Normalize(finding.InternalReasonCodes, tagReader.Metadata),
            TagReader = tagReader,
            Probe = probe,
        };
    }

    private static string SanitizeFindingId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || ShouldRedactTokenMaterial(id))
        {
            return "finding-" + PathPrivacy.HashPath(id);
        }

        return id;
    }

    private static string SanitizePathHash(string? pathHash)
    {
        return ShouldRedactTokenMaterial(pathHash)
            ? PathPrivacy.HashPath(pathHash)
            : pathHash ?? string.Empty;
    }

    private static IReadOnlyList<string> SanitizePathLikeTokens(
        IReadOnlyList<string>? values,
        string redactedPrefix)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var sanitized = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            sanitized[i] = SanitizePathLikeToken(values[i], redactedPrefix) ?? string.Empty;
        }

        return sanitized;
    }

    private static string? SanitizePathLikeToken(string? value, string redactedPrefix)
    {
        return ShouldRedactTokenMaterial(value)
            ? redactedPrefix + PathPrivacy.HashPath(value)
            : value;
    }

    private static string? SanitizeMessage(string? message)
    {
        return ContainsSensitiveMessagePathMaterial(message)
            ? PathContainingMessageRedaction
            : message;
    }

    private static TagMetadataEvidence? SanitizeMetadata(TagMetadataEvidence? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata with
        {
            MissingFields = TagMetadataFields.GetMissingFields(metadata),
        };
    }

    // Delegates to the shared predicate so persistence and API projection redact identically.
    // Historically this had a weaker local copy (no metadata/command-material checks) than the API
    // projection, which meant a token containing e.g. a bare MusicBrainz id could be redacted in the
    // API response yet persisted raw to disk. Sharing the predicate closes that divergence.
    private static bool ShouldRedactTokenMaterial(string? value)
    {
        return LibraryHealerTokenRedaction.ShouldRedactTokenMaterial(value);
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
            || LibraryHealerSensitiveText.ContainsMetadataMaterial(value);
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

    private static string NormalizeKey(string key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }
}
