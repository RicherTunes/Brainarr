using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Utils;

public static class StableHash
{
    public static StableHashResult Compute(IEnumerable<string> components)
    {
        var normalized = components?.Select(component => component ?? string.Empty).ToArray() ?? Array.Empty<string>();
        if (normalized.Length > 1)
        {
            Array.Sort(normalized, StringComparer.Ordinal);
        }

        var joined = string.Join('\u001F', normalized);
        var bytes = Encoding.UTF8.GetBytes(joined);
        var hash = SHA256.HashData(bytes);
        var seed32 = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(0, sizeof(uint)));
        var seed = (int)(seed32 & 0x7FFF_FFFF);
        var hashPrefix = Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
        var fullHash = Convert.ToHexString(hash).ToLowerInvariant();

        return new StableHashResult(seed, hashPrefix, normalized.Length, fullHash);
    }

    public readonly record struct StableHashResult(int Seed, string HashPrefix, int ComponentCount, string FullHash);
}
