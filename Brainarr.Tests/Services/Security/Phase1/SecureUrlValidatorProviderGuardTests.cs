using NzbDrone.Core.ImportLists.Brainarr.Services.Security;
using Xunit;

namespace Brainarr.Tests.Services.Security.Phase1
{
    /// <summary>
    /// F-06: SecureUrlValidator.IsSafeProviderUrl is the SSRF guard wired into the Ollama / LM Studio URL
    /// validators. It allows ANY host (local or remote — users may run a model server on a LAN box or a
    /// remote tunnel) but blocks the exfil/SSRF vectors: non-http(s) schemes, cloud-metadata endpoints, and
    /// path traversal. This is the "metadata/scheme guard" option (not strict local-only).
    /// </summary>
    public class SecureUrlValidatorProviderGuardTests
    {
        [Theory]
        // Legitimate local + remote provider URLs are allowed.
        [InlineData("http://localhost:11434", true)]
        [InlineData("http://127.0.0.1:1234", true)]
        [InlineData("http://192.168.1.50:11434", true)]
        [InlineData("https://my-ollama.example.com", true)]      // remote public host allowed
        [InlineData("localhost:11434", true)]                     // bare host[:port] accepted (http assumed)
        // Dangerous schemes blocked.
        [InlineData("file:///etc/passwd", false)]
        [InlineData("gopher://evil/x", false)]
        [InlineData("ftp://host/x", false)]
        // Cloud-metadata endpoints blocked (the exfil vector).
        [InlineData("http://169.254.169.254/latest/meta-data/", false)]
        [InlineData("http://metadata.google.internal/computeMetadata/v1/", false)]
        [InlineData("http://100.100.100.200/", false)]
        [InlineData("http://localhost:11434/latest/meta-data/iam", false)] // metadata path on any host
        // Empty / malformed.
        [InlineData("", false)]
        [InlineData("   ", false)]
        public void IsSafeProviderUrl_AllowsAnyHost_BlocksSsrfVectors(string url, bool expected)
        {
            Assert.Equal(expected, SecureUrlValidator.IsSafeProviderUrl(url));
        }

        [Fact]
        public void IsSafeProviderUrl_Null_ReturnsFalse()
        {
            Assert.False(SecureUrlValidator.IsSafeProviderUrl(null));
        }
    }
}
