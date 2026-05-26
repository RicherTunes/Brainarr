using System;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Brainarr.Plugin.Services.Security;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Security
{
    public class CertificateValidatorTests
    {
        private static bool InvokeValidateCertificate(HttpRequestMessage request, X509Certificate2 cert, SslPolicyErrors errors, bool enablePinning)
        {
            var mi = typeof(CertificateValidator).GetMethod(
                "ValidateCertificate",
                BindingFlags.Static | BindingFlags.NonPublic);
            mi.Should().NotBeNull();

            var result = (bool)mi!.Invoke(null, new object?[] { request, cert, null, errors, enablePinning })!;
            return result;
        }

        private static X509Certificate2 CreateSelfSignedRsa(string host, int keySizeBits = 2048, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
        {
            using var rsa = RSA.Create(keySizeBits);
            var req = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return req.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(30));
        }

        // Helper for weak RSA key size scenario

        [Fact]
        public void ValidateCertificate_all_good_returns_true()
        {
            var host = "example.com";
            using var cert = CreateSelfSignedRsa(host);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            // Inject a stub chain builder (self-signed certs fail real chain.Build).
            // This test exercises every check BEFORE chain.Build — the chain.Build step
            // itself is tested by ValidateCertificate_WithRevocationCheckEnabled_AttemptsChainBuild.
            CertificateValidator.ChainBuilderOverride = _ => true;
            try
            {
                var ok = InvokeValidateCertificate(req, cert, SslPolicyErrors.None, enablePinning: false);
                ok.Should().BeTrue();
            }
            finally
            {
                CertificateValidator.ChainBuilderOverride = null;
            }
        }

        [Fact]
        public void ValidateCertificate_not_yet_valid_and_expired_are_rejected()
        {
            var host = "nv.example";
            using var futureCert = CreateSelfSignedRsa(host, 2048, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(10));
            using var expiredCert = CreateSelfSignedRsa(host, 2048, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            InvokeValidateCertificate(req, futureCert, SslPolicyErrors.None, false).Should().BeFalse();
            InvokeValidateCertificate(req, expiredCert, SslPolicyErrors.None, false).Should().BeFalse();
        }

        [Fact]
        public void ValidateCertificate_chain_errors_allowed_for_localhost_in_development()
        {
            var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                using var cert = CreateSelfSignedRsa("localhost");
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://localhost");

                // RemoteCertificateChainErrors for localhost in Development => allowed
                InvokeValidateCertificate(req, cert, SslPolicyErrors.RemoteCertificateChainErrors, false).Should().BeTrue();
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
            }
        }

        [Fact]
        public void ValidateCertificate_rejects_weak_rsa_key_sizes()
        {
            var host = "weak.example";
            using var ecdsaCert = CreateSelfSignedRsa(host, 1024); // RSA 1024 < 2048 => reject
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            var ok = InvokeValidateCertificate(req, ecdsaCert, SslPolicyErrors.None, false);
            ok.Should().BeFalse();
        }

        [Fact]
        public void ValidateCertificate_certificate_pinning_success_and_failure()
        {
            var host = "pin.test";
            using var cert = CreateSelfSignedRsa(host);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            // Stub chain builder so self-signed cert passes the chain step.
            // This test is focused on the pinning logic, not chain verification.
            CertificateValidator.ChainBuilderOverride = _ => true;
            try
            {
                // Mismatch => should fail when pinning enabled
                CertificateValidator.UpdateCertificatePins(host, "deadbeef");
                InvokeValidateCertificate(req, cert, SslPolicyErrors.None, true).Should().BeFalse();

                // Match => should pass
                CertificateValidator.UpdateCertificatePins(host, cert.Thumbprint);
                InvokeValidateCertificate(req, cert, SslPolicyErrors.None, true).Should().BeTrue();
            }
            finally
            {
                // Cleanup pins and override for isolation
                CertificateValidator.UpdateCertificatePins(host);
                CertificateValidator.ChainBuilderOverride = null;
            }
        }

        // ---- Mission #29: chain.Build revocation tests ----

        /// <summary>
        /// Positive control: establishes test infrastructure by confirming expired certs
        /// are rejected before even reaching the chain-build step.
        /// </summary>
        [Fact]
        public void ValidateCertificate_WithExpiredCert_ReturnsFalse()
        {
            var host = "expired.example";
            using var cert = CreateSelfSignedRsa(
                host,
                keySizeBits: 2048,
                notBefore: DateTimeOffset.UtcNow.AddDays(-60),
                notAfter: DateTimeOffset.UtcNow.AddDays(-1));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            var ok = InvokeValidateCertificate(req, cert, SslPolicyErrors.None, enablePinning: false);

            ok.Should().BeFalse("an expired certificate must be rejected");
        }

        /// <summary>
        /// Verifies that chain.Build is invoked by the code path.
        /// We inject a spy via ChainBuilderOverride that records whether it was called,
        /// then return false so validation fails even for a structurally valid cert.
        /// </summary>
        [Fact]
        public void ValidateCertificate_WithRevocationCheckEnabled_AttemptsChainBuild()
        {
            var host = "revocation.example";
            using var cert = CreateSelfSignedRsa(host);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            var chainBuilderInvoked = false;

            // Inject spy: records call, returns false to simulate revocation failure
            CertificateValidator.ChainBuilderOverride = _ =>
            {
                chainBuilderInvoked = true;
                return false; // treat as revoked / chain build failure
            };

            try
            {
                var ok = InvokeValidateCertificate(req, cert, SslPolicyErrors.None, enablePinning: false);

                chainBuilderInvoked.Should().BeTrue("PerformAdditionalCertificateChecks must call the chain builder");
                ok.Should().BeFalse("a chain build failure must cause validation to return false");
            }
            finally
            {
                CertificateValidator.ChainBuilderOverride = null;
            }
        }

        /// <summary>
        /// Self-signed certs fail real chain.Build because there is no trusted root.
        /// With the default chain builder (no override), validation must return false.
        /// Also verifies the spy path from the previous test is restorable.
        /// </summary>
        [Fact]
        public void ValidateCertificate_WithSelfSignedCert_ReturnsFalse_DefaultBuilder()
        {
            var host = "selfsigned.example";
            using var cert = CreateSelfSignedRsa(host);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{host}");

            // No override — default builder must be used and must fail on self-signed
            CertificateValidator.ChainBuilderOverride = null;

            var ok = InvokeValidateCertificate(req, cert, SslPolicyErrors.None, enablePinning: false);

            ok.Should().BeFalse("a self-signed cert has no trusted chain and must be rejected by chain.Build");
        }
    }
}
