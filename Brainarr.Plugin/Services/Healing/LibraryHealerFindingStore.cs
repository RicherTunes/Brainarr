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
        var originalPathMaterial = finding.File.RedactedPath;
        var redactedPath = ShouldRedactPathMaterial(originalPathMaterial)
            ? PathPrivacy.Redact(originalPathMaterial)
            : originalPathMaterial;
        var file = string.Equals(redactedPath, finding.File.RedactedPath, StringComparison.Ordinal)
            ? finding.File
            : finding.File with { RedactedPath = redactedPath };

        var tagReaderMessage = SanitizeMessage(finding.TagReader.ErrorMessage);
        var tagReader = string.Equals(tagReaderMessage, finding.TagReader.ErrorMessage, StringComparison.Ordinal)
            ? finding.TagReader
            : finding.TagReader with { ErrorMessage = tagReaderMessage };

        var probe = finding.Probe;
        if (probe is not null)
        {
            var probeMessage = SanitizeMessage(probe.ErrorMessage);
            if (!string.Equals(probeMessage, probe.ErrorMessage, StringComparison.Ordinal))
            {
                probe = probe with { ErrorMessage = probeMessage };
            }
        }

        return finding with
        {
            Id = SanitizeFindingId(finding.Id),
            File = file,
            TagReader = tagReader,
            Probe = probe,
        };
    }

    private static string SanitizeFindingId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || ShouldRedactPathMaterial(id))
        {
            return "finding-" + PathPrivacy.HashPath(id);
        }

        return id;
    }

    private static string? SanitizeMessage(string? message)
    {
        return ContainsLikelyPath(message)
            ? PathContainingMessageRedaction
            : message;
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
