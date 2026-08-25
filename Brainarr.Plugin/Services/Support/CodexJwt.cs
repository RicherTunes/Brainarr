using System;
using System.Text;
using System.Text.Json;
using Lidarr.Plugin.Common.Utilities;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Minimal, fail-closed reader for the ChatGPT-subscription JWTs stored in
    /// <c>~/.codex/auth.json</c> (<c>tokens.access_token</c> / <c>tokens.id_token</c>).
    ///
    /// <para>
    /// The Codex auth file does NOT persist a separate <c>expires_at</c> field — the access
    /// token's lifetime lives in its JWT <c>exp</c> claim. We only need to READ the expiry to
    /// decide when to refresh, so this decodes the base64url payload segment and pulls out
    /// <c>exp</c> (and, when asked, the <c>chatgpt_account_id</c> claim). Signature is NOT
    /// verified — that's the backend's job; a forged token simply fails the live call. Any
    /// malformed input yields <c>null</c> rather than throwing, so the credential-load path
    /// never blows up on a hostile file.
    /// </para>
    /// </summary>
    internal static class CodexJwt
    {
        /// <summary>
        /// Returns the <c>exp</c> claim as a UTC instant, or null when the token is not a
        /// well-formed JWT with an in-range integer <c>exp</c>.
        /// </summary>
        public static DateTimeOffset? GetExpiry(string? jwt)
        {
            var payload = DecodePayload(jwt);
            if (payload is null) return null;

            using var doc = payload;
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("exp", out var expElement)) return null;

            return EpochExpiryFromSeconds(expElement);
        }

        /// <summary>
        /// Returns the nested <c>https://api.openai.com/auth.chatgpt_account_id</c> claim, or
        /// null when absent/malformed. Used as a fallback when <c>tokens.account_id</c> is missing.
        /// </summary>
        public static string? GetChatGptAccountId(string? jwt)
        {
            var payload = DecodePayload(jwt);
            if (payload is null) return null;

            using var doc = payload;
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth)) return null;
            if (auth.ValueKind != JsonValueKind.Object) return null;
            if (!auth.TryGetProperty("chatgpt_account_id", out var acc)) return null;

            return acc.ValueKind == JsonValueKind.String ? acc.GetString() : null;
        }

        private static DateTimeOffset? EpochExpiryFromSeconds(JsonElement element)
            => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var s)
               && TimeParsing.TryFromUnixTimeSeconds(s, out var value)
                ? value
                : null;

        private static JsonDocument? DecodePayload(string? jwt)
        {
            if (string.IsNullOrWhiteSpace(jwt)) return null;

            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                return JsonDocument.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] Base64UrlDecode(string segment)
        {
            var s = segment.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
