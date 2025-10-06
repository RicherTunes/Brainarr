using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting
{
    public static class StableHash
    {
        private const char Delimiter = '\u001F';
        private const int MaxComponents = 4096;
        private const int MaxComponentLength = 24576;

        public static StableHashResult FromComponents(params string[] components)
        {
            var normalized = components?.Select(component => component ?? string.Empty).ToArray() ?? Array.Empty<string>();
            var sanitized = Sanitize(normalized);

            if (sanitized.Length > 1)
            {
                Array.Sort(sanitized, StringComparer.Ordinal);
            }

            return ComputeInternal(sanitized);
        }

        public static StableHashResult FromEnumerable(IEnumerable<string> components)
        {
            var materialized = components?.Select(component => component ?? string.Empty).ToArray() ?? Array.Empty<string>();
            var sanitized = Sanitize(materialized);

            if (sanitized.Length > 1)
            {
                Array.Sort(sanitized, StringComparer.Ordinal);
            }

            return ComputeInternal(sanitized);
        }

        public static StableHashResult Compute(IEnumerable<string> components) => FromEnumerable(components);

        private static string[] Sanitize(string[] values)
        {
            if (values.Length == 0)
            {
                return Array.Empty<string>();
            }

            var length = values.Length > MaxComponents ? MaxComponents : values.Length;
            var sanitized = new string[length];

            for (var i = 0; i < length; i++)
            {
                var value = values[i] ?? string.Empty;
                sanitized[i] = value.Length > MaxComponentLength ? value.Substring(0, MaxComponentLength) : value;
            }

            return sanitized;
        }

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
}
