using System;
using System.Text.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// R2-08: parse an untrusted <c>expiresAt</c>/<c>expires_at</c> epoch from credential JSON without throwing
    /// on a malformed or overflowing value. <see cref="DateTimeOffset.FromUnixTimeMilliseconds"/> /
    /// <see cref="DateTimeOffset.FromUnixTimeSeconds"/> throw <see cref="ArgumentOutOfRangeException"/> past their
    /// representable range (e.g. <see cref="long.MaxValue"/>), so a hostile token file would otherwise blow up the
    /// credential-load path. These return <c>null</c> instead, so the caller treats the value as "no usable
    /// expiry". Mirrors Common's <c>TimeParsing</c>; collapses onto it once the plugin re-pins to a Common that
    /// ships it.
    /// </summary>
    internal static class EpochExpiry
    {
        private static readonly long MinMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
        private static readonly long MaxMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
        private static readonly long MinS = DateTimeOffset.MinValue.ToUnixTimeSeconds();
        private static readonly long MaxS = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

        /// <summary>Epoch milliseconds → UTC instant, or null when the element is not an in-range integer.</summary>
        public static DateTimeOffset? FromMilliseconds(JsonElement element)
            => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var ms) && ms >= MinMs && ms <= MaxMs
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;

        /// <summary>Epoch seconds → UTC instant, or null when the element is not an in-range integer.</summary>
        public static DateTimeOffset? FromSeconds(JsonElement element)
            => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var s) && s >= MinS && s <= MaxS
                ? DateTimeOffset.FromUnixTimeSeconds(s)
                : null;
    }
}
