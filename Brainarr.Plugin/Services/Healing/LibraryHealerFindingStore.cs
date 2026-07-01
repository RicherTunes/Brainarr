using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Storage;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public interface ILibraryHealerFindingStore
{
    void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings);

    IReadOnlyList<LibraryHealerFinding> GetRecent(int limit);

    void Clear();
}

public sealed class LibraryHealerFindingStore : ILibraryHealerFindingStore
{
    private const string FileName = "library_healer_findings.json";
    private const int MaxEntries = 5000;
    private const int MaxRecentLimit = 500;
    private const string PathContainingMessageRedaction = "<path-containing message redacted>";

    private readonly JsonFileStore<string, LibraryHealerFinding> _store;

    public LibraryHealerFindingStore(string? dataPath = null)
    {
        var root = dataPath ?? PluginConfigRoots.Resolve("Brainarr");
        Directory.CreateDirectory(root);
        _store = new JsonFileStore<string, LibraryHealerFinding>(
            Path.Combine(root, FileName),
            new JsonFileStoreOptions<string>
            {
                MaxEntries = MaxEntries,
                KeyNormalizer = static key => NormalizeKey(key),
                KeyComparer = StringComparer.OrdinalIgnoreCase,
            });
    }

    public void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
    {
        if (findings is null || findings.Count == 0)
        {
            return;
        }

        var sanitized = findings
            .Where(static finding => finding is not null)
            .Select(Sanitize)
            .ToList();
        if (sanitized.Count == 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            foreach (var finding in sanitized)
            {
                await _store.SetAsync(finding.Id, finding).ConfigureAwait(false);
            }
        }).GetAwaiter().GetResult();
    }

    public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaxRecentLimit);
        return EnumerateAll()
            .OrderByDescending(static finding => finding.ObservedAtUtc)
            .Take(boundedLimit)
            .ToList();
    }

    public void Clear()
    {
        Task.Run(async () => await _store.ClearAsync().ConfigureAwait(false))
            .GetAwaiter().GetResult();
    }

    private List<LibraryHealerFinding> EnumerateAll()
    {
        return Task.Run(async () =>
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
        }).GetAwaiter().GetResult();
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

    private static bool ShouldRedactPathMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.IndexOfAny(new[] { '\\', '/' }) >= 0 || HasWindowsRoot(trimmed);
    }

    private static bool ShouldRedactTokenMaterial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ShouldRedactPathMaterial(trimmed)
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
