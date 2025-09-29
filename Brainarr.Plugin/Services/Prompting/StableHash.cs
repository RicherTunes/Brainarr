using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public static class StableHash
{
    private const char Delimiter = '\u001F';

    public static StableHashResult FromComponents(params string[] components)
    {
        var normalized = components?.Select(component => component ?? string.Empty).ToArray() ?? Array.Empty<string>();
        if (normalized.Length > 1)
        {
            Array.Sort(normalized, StringComparer.Ordinal);
        }

        return ComputeInternal(normalized);
    }

    public static StableHashResult FromEnumerable(IEnumerable<string> components)
    {
        var materialized = components?.Select(component => component ?? string.Empty).ToArray() ?? Array.Empty<string>();
        if (materialized.Length > 1)
        {
            Array.Sort(materialized, StringComparer.Ordinal);
        }

        return ComputeInternal(materialized);
    }

    public static StableHashResult Compute(IEnumerable<string> components) => FromEnumerable(components);

    private static StableHashResult ComputeInternal(IReadOnlyList<string> components)
    {
        var joined = string.Join(Delimiter, components);
        var bytes = Encoding.UTF8.GetBytes(joined);
        var hash = SHA256.HashData(bytes);

        var seed32 = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(0, sizeof(uint)));
        var seed = (int)(seed32 & 0x7FFF_FFFF);
        var hashPrefix = Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
        var fullHash = Convert.ToHexString(hash).ToLowerInvariant();

        return new StableHashResult(seed, hashPrefix, components.Count, fullHash);
    }

    public readonly record struct StableHashResult(int Seed, string HashPrefix, int ComponentCount, string FullHash);
}
