using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Shared
{
    [Trait("Category", "Unit")]
    [Collection("LoggingTests")] // sequence with other env-var-mutating tests
    public class FormatPreferenceCacheTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalEnv;

        public FormatPreferenceCacheTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BrainarrTests", "FormatPrefs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalEnv = Environment.GetEnvironmentVariable("BRAINARR_PREFS_DIR");
            Environment.SetEnvironmentVariable("BRAINARR_PREFS_DIR", _tempDir);
            ResetCacheState();
        }

        private static void ResetCacheState()
        {
            // Reset the static _loaded flag and clear the dictionary so EnsureLoaded re-reads from our temp dir.
            var t = typeof(FormatPreferenceCache);
            var loadedField = t.GetField("_loaded", BindingFlags.Static | BindingFlags.NonPublic);
            loadedField?.SetValue(null, false);
            var dictField = t.GetField("_preferStructured", BindingFlags.Static | BindingFlags.NonPublic);
            var dict = dictField?.GetValue(null);
            var clear = dict?.GetType().GetMethod("Clear");
            clear?.Invoke(dict, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("BRAINARR_PREFS_DIR", _originalEnv);
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void GetPreferStructured_UnknownKey_ReturnsDefault_True()
        {
            var key = "unit-test-" + Guid.NewGuid().ToString("N");
            FormatPreferenceCache.GetPreferStructuredOrDefault(key, true).Should().BeTrue();
        }

        [Fact]
        public void GetPreferStructured_UnknownKey_ReturnsDefault_False()
        {
            var key = "unit-test-" + Guid.NewGuid().ToString("N");
            FormatPreferenceCache.GetPreferStructuredOrDefault(key, false).Should().BeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetPreferStructured_NullOrWhitespaceKey_ReturnsDefault(string key)
        {
            FormatPreferenceCache.GetPreferStructuredOrDefault(key, true).Should().BeTrue();
            FormatPreferenceCache.GetPreferStructuredOrDefault(key, false).Should().BeFalse();
        }

        [Fact]
        public void SetPreferStructured_PersistsValue_AndIsReadable()
        {
            var key = "set-roundtrip-" + Guid.NewGuid().ToString("N");
            FormatPreferenceCache.SetPreferStructured(key, true);
            FormatPreferenceCache.GetPreferStructuredOrDefault(key, false).Should().BeTrue();

            FormatPreferenceCache.SetPreferStructured(key, false);
            FormatPreferenceCache.GetPreferStructuredOrDefault(key, true).Should().BeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SetPreferStructured_NullOrWhitespaceKey_NoOp(string key)
        {
            // Should not throw and should not persist anything for empty keys
            FormatPreferenceCache.SetPreferStructured(key, true);
        }

        [Fact]
        public void SetPreferStructured_WritesFileInOverrideDir()
        {
            var key = "file-write-" + Guid.NewGuid().ToString("N");
            FormatPreferenceCache.SetPreferStructured(key, true);
            var path = Path.Combine(_tempDir, "format-preferences.json");
            File.Exists(path).Should().BeTrue();
            var content = File.ReadAllText(path);
            content.Should().Contain(key);
        }
    }
}
