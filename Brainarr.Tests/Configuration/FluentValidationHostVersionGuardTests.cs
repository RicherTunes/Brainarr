using System;
using System.IO;
using FluentValidation;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    /// <summary>
    /// Guard test ensuring the loaded FluentValidation assembly matches the Lidarr host version.
    /// The plugin crosses the FV boundary via NzbDroneValidationResult; a version mismatch
    /// causes MissingMethodException at runtime. See issue #396.
    /// </summary>
    [Trait("Category", "Unit")]
    public class FluentValidationHostVersionGuardTests
    {
        private const int ExpectedMajor = 9;

        [Fact]
        public void LoadedFluentValidation_MajorVersion_MatchesHostExpectation()
        {
            // Validates the FV assembly actually loaded into the test process, not just
            // a file on disk. This catches NuGet resolution pulling a wrong version.
            var fvAssembly = typeof(AbstractValidator<>).Assembly;
            var version = fvAssembly.GetName().Version!;

            Assert.True(
                version.Major == ExpectedMajor,
                $"FluentValidation major version mismatch: loaded {version} (from {fvAssembly.Location}), " +
                $"expected major {ExpectedMajor}. " +
                "To fix: (1) update Directory.Packages.props FV pin to match the Lidarr host, " +
                "(2) update the Lidarr Docker tag if the host upgraded, " +
                "(3) adapt BrainarrSettingsValidator.cs if FV API changed (e.g. CustomContext → ValidationContext<T>).");
        }

        [Fact]
        public void HostFluentValidationDll_WhenPresent_MatchesExpectedVersion()
        {
            var lidarrPath = Environment.GetEnvironmentVariable("LIDARR_PATH");
            if (string.IsNullOrEmpty(lidarrPath))
            {
                // Skip in packaging-only or minimal CI contexts where host assemblies aren't extracted.
                // The loaded-assembly test above still provides coverage via NuGet pin.
                return;
            }

            var hostFvPath = Path.Combine(lidarrPath, "FluentValidation.dll");
            if (!File.Exists(hostFvPath))
                return;

            var hostVersion = System.Reflection.AssemblyName.GetAssemblyName(hostFvPath).Version!;
            Assert.True(
                hostVersion.Major == ExpectedMajor,
                $"Host FluentValidation.dll at {hostFvPath} is version {hostVersion}, " +
                $"expected major {ExpectedMajor}. " +
                "The Lidarr Docker tag may have been bumped to a version with a newer FV. " +
                "Update Directory.Packages.props and BrainarrSettingsValidator.cs accordingly.");
        }
    }
}
