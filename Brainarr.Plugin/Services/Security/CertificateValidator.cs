using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using NLog;

namespace Brainarr.Plugin.Services.Security
{
    /// <summary>
    /// Provides certificate validation and pinning for secure API communications
    /// </summary>
    public class CertificateValidator
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
        /// <summary>
        /// Known certificate thumbprints for popular AI provider APIs
        /// These should be updated periodically as certificates rotate
        /// </summary>
        private static readonly Dictionary<string, HashSet<string>> KnownCertificateThumbprints = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // OpenAI API certificates (DigiCert)
            ["api.openai.com"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Add current certificate thumbprints here
                // These are examples and should be updated with actual values
            },
            
            // Anthropic API certificates
            ["api.anthropic.com"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Add current certificate thumbprints
            },
            
            // Google API certificates
            ["generativelanguage.googleapis.com"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Add current certificate thumbprints
            },
            
            // MusicBrainz API certificates
            ["musicbrainz.org"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Add current certificate thumbprints
            }
        };

        /// <summary>
        /// Minimum allowed certificate expiry time (days)
        /// </summary>
        private const int MinimumCertificateExpiryDays = 7;

        /// <summary>
        /// Create a HttpClientHandler with certificate validation
        /// </summary>
        public static HttpClientHandler CreateSecureHandler(bool enableCertificatePinning = false)
        {
            var handler = new HttpClientHandler();
            
            // Set up certificate validation callback
            handler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
            {
                return ValidateCertificate(sender, certificate, chain, sslPolicyErrors, enableCertificatePinning);
            };

            // Additional security settings
            handler.AllowAutoRedirect = false; // Prevent automatic redirects
            handler.MaxAutomaticRedirections = 0;
            handler.UseCookies = false; // Don't store cookies
            handler.UseDefaultCredentials = false;
            
            return handler;
        }

        /// <summary>
        /// Validate server certificate
        /// </summary>
        private static bool ValidateCertificate(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors,
            bool enablePinning)
        {
            // Log certificate details for debugging
            _logger.Debug($"Validating certificate for {request?.RequestUri?.Host}");
            _logger.Debug($"Certificate Subject: {certificate?.Subject}");
            _logger.Debug($"Certificate Thumbprint: {certificate?.Thumbprint}");
            _logger.Debug($"SSL Policy Errors: {sslPolicyErrors}");

            // Check for SSL policy errors
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // No SSL errors, perform additional checks
                return PerformAdditionalCertificateChecks(request, certificate, enablePinning);
            }

            // Handle specific SSL policy errors
            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            {
                _logger.Error("Remote certificate not available");
                return false;
            }

            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                _logger.Error($"Certificate name mismatch for {request?.RequestUri?.Host}");
                return false;
            }

            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            {
                // Log chain errors
                if (chain != null)
                {
                    foreach (var status in chain.ChainStatus)
                    {
                        _logger.Error($"Certificate chain error: {status.Status} - {status.StatusInformation}");
                    }
                }
                
                // In production, we should reject chain errors
                // But allow for self-signed certificates in development
                var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                if (isDevelopment && request?.RequestUri?.Host == "localhost")
                {
                    _logger.Warn("Allowing self-signed certificate for localhost in development");
                    return true;
                }
                
                return false;
            }

            return false;
        }

        /// <summary>
        /// Perform additional certificate security checks
        /// </summary>
        private static bool PerformAdditionalCertificateChecks(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            bool enablePinning)
        {
            if (certificate == null)
            {
                _logger.Error("Certificate is null");
                return false;
            }

            var hostname = request?.RequestUri?.Host;
            if (string.IsNullOrEmpty(hostname))
            {
                _logger.Error("Request hostname is null or empty");
                return false;
            }

            // Check certificate expiry
            if (!IsCertificateValid(certificate))
            {
                return false;
            }

            // Check for weak signature algorithms
            if (IsWeakSignatureAlgorithm(certificate))
            {
                _logger.Error($"Certificate uses weak signature algorithm: {certificate.SignatureAlgorithm.FriendlyName}");
                return false;
            }

            // Check key size
            if (!IsKeySizeSecure(certificate))
            {
                return false;
            }

            // Certificate pinning (optional)
            if (enablePinning && !ValidateCertificatePinning(hostname, certificate))
            {
                return false;
            }

            // Check for known compromised certificates
            if (IsCompromisedCertificate(certificate))
            {
                _logger.Error("Certificate is known to be compromised");
                return false;
            }

            _logger.Debug($"Certificate validation successful for {hostname}");
            return true;
        }

        /// <summary>
        /// Check if certificate is valid (not expired and not yet valid)
        /// </summary>
        private static bool IsCertificateValid(X509Certificate2 certificate)
        {
            var now = DateTime.UtcNow;
            
            if (certificate.NotBefore > now)
            {
                _logger.Error($"Certificate not yet valid. Valid from: {certificate.NotBefore:yyyy-MM-dd}");
                return false;
            }

            if (certificate.NotAfter < now)
            {
                _logger.Error($"Certificate expired on: {certificate.NotAfter:yyyy-MM-dd}");
                return false;
            }

            // Warn if certificate expires soon
            var daysUntilExpiry = (certificate.NotAfter - now).TotalDays;
            if (daysUntilExpiry < MinimumCertificateExpiryDays)
            {
                _logger.Warn($"Certificate expires in {daysUntilExpiry:F0} days");
            }

            return true;
        }

        /// <summary>
        /// Check for weak signature algorithms
        /// </summary>
        private static bool IsWeakSignatureAlgorithm(X509Certificate2 certificate)
        {
            var weakAlgorithms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "MD2", "MD4", "MD5", "SHA1", "SHA-1"
            };

            var algorithmName = certificate.SignatureAlgorithm.FriendlyName;
            if (string.IsNullOrEmpty(algorithmName))
            {
                return false;
            }

            return weakAlgorithms.Any(weak => algorithmName.Contains(weak, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if key size is secure
        /// </summary>
        private static bool IsKeySizeSecure(X509Certificate2 certificate)
        {
            try
            {
                using var rsa = certificate.GetRSAPublicKey();
                if (rsa != null)
                {
                    var keySize = rsa.KeySize;
                    if (keySize < 2048)
                    {
                        _logger.Error($"RSA key size too small: {keySize} bits (minimum 2048)");
                        return false;
                    }
                }
                
                using var ecdsa = certificate.GetECDsaPublicKey();
                if (ecdsa != null)
                {
                    var keySize = ecdsa.KeySize;
                    if (keySize < 256)
                    {
                        _logger.Error($"ECDSA key size too small: {keySize} bits (minimum 256)");
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking key size");
                return false;
            }
        }

        /// <summary>
        /// Validate certificate pinning
        /// </summary>
        private static bool ValidateCertificatePinning(string hostname, X509Certificate2 certificate)
        {
            if (!KnownCertificateThumbprints.TryGetValue(hostname, out var knownThumbprints))
            {
                // No pinning configured for this host
                _logger.Debug($"No certificate pinning configured for {hostname}");
                return true;
            }

            if (knownThumbprints == null || knownThumbprints.Count == 0)
            {
                // Empty pinning list, allow all
                return true;
            }

            var thumbprint = certificate.Thumbprint;
            if (knownThumbprints.Contains(thumbprint))
            {
                _logger.Debug($"Certificate pinning successful for {hostname}");
                return true;
            }

            // Check intermediate certificates in the chain
            using (var chain = new X509Chain())
            {
                chain.Build(certificate);
                foreach (var element in chain.ChainElements)
                {
                    if (knownThumbprints.Contains(element.Certificate.Thumbprint))
                    {
                        _logger.Debug($"Certificate pinning successful for {hostname} (intermediate certificate)");
                        return true;
                    }
                }
            }

            _logger.Error($"Certificate pinning failed for {hostname}. Thumbprint: {thumbprint}");
            return false;
        }

        /// <summary>
        /// Check if certificate is known to be compromised
        /// </summary>
        private static bool IsCompromisedCertificate(X509Certificate2 certificate)
        {
            // This would check against a list of known compromised certificates
            // In production, this could query a certificate revocation list (CRL) or OCSP
            var compromisedThumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Add known compromised certificate thumbprints here
            };

            return compromisedThumbprints.Contains(certificate.Thumbprint);
        }

        /// <summary>
        /// Update certificate pins for a specific host
        /// </summary>
        public static void UpdateCertificatePins(string hostname, params string[] thumbprints)
        {
            if (string.IsNullOrWhiteSpace(hostname))
            {
                throw new ArgumentNullException(nameof(hostname));
            }

            if (thumbprints == null || thumbprints.Length == 0)
            {
                KnownCertificateThumbprints.Remove(hostname);
                _logger.Info($"Removed certificate pins for {hostname}");
            }
            else
            {
                KnownCertificateThumbprints[hostname] = new HashSet<string>(thumbprints, StringComparer.OrdinalIgnoreCase);
                _logger.Info($"Updated certificate pins for {hostname}: {thumbprints.Length} thumbprints");
            }
        }

        /// <summary>
        /// Get current certificate information for a host
        /// </summary>
        public static async System.Threading.Tasks.Task<CertificateInfo> GetCertificateInfoAsync(string url)
        {
            try
            {
                var uri = new Uri(url);
                using var handler = CreateSecureHandler(enableCertificatePinning: false);
                using var client = new HttpClient(handler);
                
                // Make a HEAD request to get certificate info without downloading content
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                
                var certificateInfo = new CertificateInfo();
                
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (cert != null)
                    {
                        certificateInfo.Subject = cert.Subject;
                        certificateInfo.Issuer = cert.Issuer;
                        certificateInfo.Thumbprint = cert.Thumbprint;
                        certificateInfo.NotBefore = cert.NotBefore;
                        certificateInfo.NotAfter = cert.NotAfter;
                        certificateInfo.SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName;
                        certificateInfo.SerialNumber = cert.SerialNumber;
                    }
                    return true; // Accept for info gathering
                };

                await client.SendAsync(request);
                return certificateInfo;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get certificate info for {url}");
                return null;
            }
        }

        /// <summary>
        /// Certificate information class
        /// </summary>
        public class CertificateInfo
        {
            public string Subject { get; set; }
            public string Issuer { get; set; }
            public string Thumbprint { get; set; }
            public DateTime NotBefore { get; set; }
            public DateTime NotAfter { get; set; }
            public string SignatureAlgorithm { get; set; }
            public string SerialNumber { get; set; }
            
            public bool IsExpired => DateTime.UtcNow > NotAfter;
            public bool IsNotYetValid => DateTime.UtcNow < NotBefore;
            public int DaysUntilExpiry => (int)(NotAfter - DateTime.UtcNow).TotalDays;
        }
    }
}