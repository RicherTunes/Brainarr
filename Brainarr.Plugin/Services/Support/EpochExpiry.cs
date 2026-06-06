using System;
using System.Text.Json;
using Lidarr.Plugin.Common.Utilities;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// R2-08 / LOOP-007: parse an untrusted <c>expiresAt</c>/<c>expires_at</c> epoch from credential JSON without
    /// throwing on a malformed or overflowing value. This is a thin JSON adapter over Common's
    /// <see cref="TimeParsing"/>: it pulls an int64 out of the element and delegates the fail-closed range check
    /// and conversion to <see cref="TimeParsing.TryFromUnixTimeMilliseconds"/> /
    /// <see cref="TimeParsing.TryFromUnixTimeSeconds"/>, so a hostile token file yields <c>null</c>
    /// ("no usable expiry") instead of blowing up the credential-load path. The previous bespoke range guard
    /// (which mirrored Common) has been collapsed onto the shared helper now that the plugin pins a Common that
    /// ships it — single source of truth for the epoch bounds.
    /// </summary>
    internal static class EpochExpiry
    {
        /// <summary>Epoch milliseconds → UTC instant, or null when the element is not an in-range integer.</summary>
        public static DateTimeOffset? FromMilliseconds(JsonElement element)
            => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var ms)
               && TimeParsing.TryFromUnixTimeMilliseconds(ms, out var value)
                ? value
                : null;

        /// <summary>Epoch seconds → UTC instant, or null when the element is not an in-range integer.</summary>
        public static DateTimeOffset? FromSeconds(JsonElement element)
            => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var s)
               && TimeParsing.TryFromUnixTimeSeconds(s, out var value)
                ? value
                : null;
    }
}
