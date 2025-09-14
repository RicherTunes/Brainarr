using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Improved URL validation helper that avoids exception-driven logic.
    /// Addresses tech lead feedback about complex URL validation in BrainarrSettingsValidator.
    /// </summary>
    public static class UrlValidator
    {
        private static readonly string[] DangerousSchemes =
        {
            "javascript:", "file:", "ftp:", "data:", "vbscript:"
        };

        /// <summary>
        /// Validates a URL using a linear approach without try-catch for control flow.
        /// </summary>
        /// <param name="url">The URL to validate</param>
        /// <param name="allowEmpty">Whether to allow empty/null URLs</param>
        /// <returns>True if the URL is valid, false otherwise</returns>
        public static bool IsValidUrl(string url, bool allowEmpty = true)
        {
            if (string.IsNullOrWhiteSpace(url))
                return allowEmpty;

            // Step 1: Decode URL safely
            string decodedUrl;
            try
            {
                decodedUrl = Uri.UnescapeDataString(url);
            }
            catch
            {
                // If URL decoding fails, use original URL
                decodedUrl = url;
            }

            // Step 2: Check for dangerous schemes (both original and decoded)
            if (ContainsDangerousScheme(url.ToLowerInvariant()) ||
                ContainsDangerousScheme(decodedUrl.ToLowerInvariant()))
            {
                return false;
            }

            // Step 3: Handle missing scheme
            string urlToValidate = EnsureScheme(decodedUrl);
            if (urlToValidate == null)
                return false;

            // Step 4: Validate using Uri.TryCreate (no exceptions)
            if (!Uri.TryCreate(urlToValidate, UriKind.Absolute, out var uri))
                return false;

            // Step 5: Ensure only HTTP/HTTPS schemes and valid port range (when specified)
            if (!(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return false;
            var port = uri.IsDefaultPort ? -1 : uri.Port;
            if (port != -1 && (port < 0 || port > 65535))
                return false;
            return true;
        }

        private static bool ContainsDangerousScheme(string lowerUrl)
        {
            foreach (var scheme in DangerousSchemes)
            {
                if (lowerUrl.StartsWith(scheme))
                    return true;
            }
            return false;
        }

        private static string EnsureScheme(string url)
        {
            // If it already has a scheme, validate it
            if (url.Contains("://"))
            {
                // Only allow http/https schemes
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return null;
                return url;
            }

            // No scheme provided - add http:// but validate format first
            if (url.Contains(' ') || url.StartsWith('.') || url.EndsWith('.'))
                return null;

            // Must look like a URL (have a dot or colon for port)
            if (!url.Contains('.') && !url.Contains(':'))
                return null;

            return "http://" + url;
        }

        /// <summary>
        /// Validates a URL specifically for local AI providers (Ollama, LM Studio).
        /// </summary>
        public static bool IsValidLocalProviderUrl(string url)
        {
            if (!IsValidUrl(url, false))
                return false;

            // Additional validation for local providers: allow typical intranet patterns too
            var decoded = url;
            try { decoded = Uri.UnescapeDataString(url); } catch { decoded = url; }
            if (!Uri.TryCreate(EnsureScheme(decoded), UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host.ToLowerInvariant();
            // Additional host and port sanity checks
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (host.Contains(" ")) return false;
            if (host.StartsWith(".") || host.EndsWith(".")) return false;
            if (host.Contains("..")) return false;
            if (uri.Port > 65535) return false;
            // Accept localhost, IPv4/IPv6, RFC1918 ranges, single-label intranet hosts, and dotted domains.
            if (host == "localhost") return true;
            if (System.Net.IPAddress.TryParse(host, out _)) return true;
            if (host.StartsWith("192.168.") || host.StartsWith("10.") || host.StartsWith("172.")) return true;
            var isSingleLabel = !host.Contains('.') && System.Text.RegularExpressions.Regex.IsMatch(host, "^[a-z0-9-]+$");
            if (isSingleLabel) return true;
            if (host.Contains('.')) return true;
            return false;
        }

        /// <summary>
        /// Normalizes an HTTP(S) URL to a canonical form similar to what UI expects.
        /// - Ensures http:// scheme if missing
        /// - Trims whitespace
        /// - Returns authority when path is '/'
        /// Falls back to the original value if not a valid HTTP(S) URL.
        /// </summary>
        public static string NormalizeHttpUrlOrOriginal(string value)
        {
            try
            {
                var v = value;
                if (string.IsNullOrWhiteSpace(v)) return v;
                // If a non-http(s) scheme is present, do not rewrite
                if (v.Contains("://", StringComparison.Ordinal) &&
                    !(v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    return value;
                }
                // Only add scheme if it looks like a URL (dot or port), avoid normalizing obvious invalids
                if (!v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (v.Contains(' ') || v.StartsWith('.') || v.EndsWith('.'))
                        return value;
                    if (!v.Contains('.') && !v.Contains(':'))
                        return value;
                    v = $"http://{v}";
                }
                if (Uri.TryCreate(v.Trim(), UriKind.Absolute, out var u))
                {
                    if (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                    {
                        var authority = u.GetLeftPart(UriPartial.Authority);
                        var path = u.AbsolutePath;
                        return string.Equals(path, "/", StringComparison.Ordinal) ? authority : authority + path;
                    }
                }
                return value;
            }
            catch
            {
                return value;
            }
        }
    }
}
