using System;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using Xunit;

namespace Brainarr.Tests.Logging
{
    public class SecureStructuredLoggerTests
    {
        [Fact]
        public void SensitiveDataMasker_masks_common_secrets()
        {
            var masker = new SensitiveDataMasker();

            var input = "Posting with api_key=ABCDEF1234567890SECRET and token=xyz and email a@b.com";
            var masked = masker.MaskSensitiveData(input);

            Assert.NotEqual(input, masked);
            Assert.DoesNotContain("ABCDEF1234567890SECRET", masked);
            Assert.DoesNotContain("a@b.com", masked);
        }

        [Fact]
        public void SecureLogger_can_log_without_throwing()
        {
            var logger = NLog.LogManager.GetCurrentClassLogger();
            var secure = new SecureStructuredLogger(logger);

            var ex = Record.Exception(() => secure.LogInfo("Hello", new { key = "sk-THISSHOULDBEMASKED-ABCDEFGHIJKLMNOPQRSTUVWXYZ012345" }));
            Assert.Null(ex);
        }
    }
}
