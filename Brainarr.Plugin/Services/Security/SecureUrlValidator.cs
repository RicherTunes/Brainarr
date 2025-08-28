using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// Secure URL validation to prevent SSRF attacks and malicious URL injection.
    /// Implements defense-in-depth with multiple validation layers.
    /// </summary>
    public interface ISecureUrlValidator
    {
        bool IsValidLocalProviderUrl(string url);
        bool IsValidCloudProviderUrl(string url);
        string SanitizeUrl(string url);
    }

    public class SecureUrlValidator : ISecureUrlValidator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        // Dangerous schemes that should never be allowed
        private static readonly HashSet<string> DangerousSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            "javascript", "vbscript", "file", "ftp", "ftps", "sftp",
            "data", "about", "chrome", "chrome-extension", "ms-appx",
            "ms-appdata", "shell", "moz-extension", "view-source",
            "ws", "wss", "gopher", "telnet", "ssh", "ldap", "ldaps"
        };

        // Known metadata service endpoints to block (SSRF prevention)
        private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "169.254.169.254",                  // AWS metadata
            "metadata.google.internal",         // GCP metadata
            "metadata.azure.com",               // Azure metadata
            "metadata.packet.net",              // Packet metadata
            "metadata.platformequinix.com",    // Equinix Metal
            "[fd00:ec2::254]",                 // AWS IPv6 metadata
            "100.100.100.200"                   // Alibaba Cloud metadata
        };

        // Private IP ranges for validation
        private static readonly List<(IPAddress start, IPAddress end)> PrivateRanges = new()
        {
            (IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255")),           // 10.0.0.0/8
            (IPAddress.Parse("172.16.0.0"), IPAddress.Parse("172.31.255.255")),        // 172.16.0.0/12
            (IPAddress.Parse("192.168.0.0"), IPAddress.Parse("192.168.255.255")),      // 192.168.0.0/16
            (IPAddress.Parse("127.0.0.0"), IPAddress.Parse("127.255.255.255")),        // 127.0.0.0/8 (loopback)
            (IPAddress.Parse("169.254.0.0"), IPAddress.Parse("169.254.255.255")),      // 169.254.0.0/16 (link-local)
            (IPAddress.Parse("fc00::"), IPAddress.Parse("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")), // fc00::/7 (IPv6 private)
        };

        public bool IsValidLocalProviderUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    Logger.Debug("URL validation: Empty URL provided");
                    return false;
                }

                // Step 1: Basic URL parsing without exceptions
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    Logger.Warn($"URL validation failed: Invalid URL format - {url.Substring(0, Math.Min(url.Length, 50))}");
                    return false;
                }

                // Step 2: Scheme validation - only HTTP/HTTPS allowed
                if (!IsAllowedScheme(uri.Scheme))
                {
                    Logger.Warn($"URL validation failed: Dangerous scheme detected - {uri.Scheme}");
                    return false;
                }

                // Step 3: Host validation - must be local/private network
                if (!IsLocalOrPrivateHost(uri.Host))
                {
                    Logger.Warn($"URL validation failed: Non-local host - {uri.Host}");
                    return false;
                }

                // Step 4: Port validation for local providers
                if (!IsValidPort(uri.Port))
                {
                    Logger.Warn($"URL validation failed: Invalid port - {uri.Port}");
                    return false;
                }

                // Step 5: Path validation - no traversal attacks
                if (ContainsPathTraversal(uri.AbsolutePath))
                {
                    Logger.Warn("URL validation failed: Path traversal detected");
                    return false;
                }

                // Step 6: Check against known malicious patterns
                if (IsBlockedEndpoint(uri))
                {
                    Logger.Warn($"URL validation failed: Blocked endpoint - {uri.Host}");
                    return false;
                }

                Logger.Debug($"URL validation passed for local provider: {uri.Host}:{uri.Port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"URL validation error for: {url?.Substring(0, Math.Min(url?.Length ?? 0, 50))}");
                return false;
            }
        }

        public bool IsValidCloudProviderUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return false;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return false;

                // Cloud providers must use HTTPS
                if (uri.Scheme != "https")
                {
                    Logger.Warn($"Cloud provider URL must use HTTPS: {uri.Scheme}");
                    return false;
                }

                // Must be a public host (not local/private)
                if (IsLocalOrPrivateHost(uri.Host))
                {
                    Logger.Warn($"Cloud provider URL cannot use local/private host: {uri.Host}");
                    return false;
                }

                // Check against blocked endpoints
                if (IsBlockedEndpoint(uri))
                {
                    Logger.Warn($"Cloud provider URL is blocked: {uri.Host}");
                    return false;
                }

                // No path traversal
                if (ContainsPathTraversal(uri.AbsolutePath))
                {
                    Logger.Warn("Cloud provider URL contains path traversal");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Cloud provider URL validation error");
                return false;
            }
        }

        public string SanitizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            try
            {
                // Remove any Unicode control characters
                url = Regex.Replace(url, @"[\x00-\x1F\x7F-\x9F]", "");

                // Decode URL encoding to prevent double encoding attacks
                url = Uri.UnescapeDataString(url);

                // Parse and rebuild URL to normalize it
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    // Rebuild with only safe components
                    var builder = new UriBuilder(uri)
                    {
                        Scheme = uri.Scheme.ToLowerInvariant(),
                        UserName = "", // Remove any embedded credentials
                        Password = "",
                        Fragment = ""  // Remove fragment identifiers
                    };

                    return builder.Uri.ToString();
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsAllowedScheme(string scheme)
        {
            if (string.IsNullOrEmpty(scheme))
                return false;

            // Only HTTP and HTTPS are allowed
            return scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                   scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLocalOrPrivateHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;

            // Check localhost variations
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("[::1]", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Try to parse as IP address
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                return IsPrivateIPAddress(ipAddress);
            }

            // Check if it's a local hostname (no dots)
            if (!host.Contains('.'))
            {
                return true; // Likely a local network hostname
            }

            // Resolve hostname to check if it points to private IP
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                foreach (var addr in addresses)
                {
                    if (IsPrivateIPAddress(addr))
                        return true;
                }
            }
            catch
            {
                // DNS resolution failed - treat as non-local for safety
                return false;
            }

            return false;
        }

        private bool IsPrivateIPAddress(IPAddress ipAddress)
        {
            // Convert to IPv4 if mapped
            if (ipAddress.IsIPv4MappedToIPv6)
            {
                ipAddress = ipAddress.MapToIPv4();
            }

            var bytes = ipAddress.GetAddressBytes();

            // Check against private ranges
            foreach (var (start, end) in PrivateRanges)
            {
                if (ipAddress.AddressFamily != start.AddressFamily)
                    continue;

                var startBytes = start.GetAddressBytes();
                var endBytes = end.GetAddressBytes();

                bool inRange = true;
                for (int i = 0; i < bytes.Length && inRange; i++)
                {
                    inRange = bytes[i] >= startBytes[i] && bytes[i] <= endBytes[i];
                    if (bytes[i] != startBytes[i] && bytes[i] != endBytes[i])
                        break;
                }

                if (inRange)
                    return true;
            }

            return false;
        }

        private bool IsValidPort(int port)
        {
            // Default HTTP/HTTPS ports are always valid
            if (port == 80 || port == 443 || port == -1)
                return true;

            // Common local AI provider ports
            var allowedPorts = new HashSet<int> 
            { 
                11434,  // Ollama default
                1234,   // LM Studio default
                8080,   // Common alternative
                8000,   // Common API port
                5000,   // Common dev port
                3000,   // Common dev port
            };

            if (allowedPorts.Contains(port))
                return true;

            // Allow high ports commonly used by local services
            return port >= 1024 && port <= 65535;
        }

        private bool ContainsPathTraversal(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Check for path traversal patterns
            var dangerous = new[]
            {
                "..",
                "..\\",
                "../",
                "..%2F",
                "..%5C",
                "%2e%2e",
                "..;",
                ".%2e",
                "%252e%252e"
            };

            var lowerPath = path.ToLowerInvariant();
            foreach (var pattern in dangerous)
            {
                if (lowerPath.Contains(pattern))
                    return true;
            }

            return false;
        }

        private bool IsBlockedEndpoint(Uri uri)
        {
            // Check against blocked hosts (metadata services)
            if (BlockedHosts.Contains(uri.Host))
                return true;

            // Check for metadata service paths
            var blockedPaths = new[]
            {
                "/metadata",
                "/latest/meta-data",
                "/computeMetadata",
                "/instance",
                "/v1/instance"
            };

            var path = uri.AbsolutePath.ToLowerInvariant();
            foreach (var blocked in blockedPaths)
            {
                if (path.StartsWith(blocked.ToLowerInvariant()))
                    return true;
            }

            return false;
        }
    }
}