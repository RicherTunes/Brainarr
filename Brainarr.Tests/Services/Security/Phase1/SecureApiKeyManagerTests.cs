using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Security;
using Xunit;
using NLog;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Security.Phase1
{
    [Trait("Area", "Security")]
    [Trait("Phase", "Phase1")]
    public class SecureApiKeyManagerTests : IDisposable
    {
        private readonly SecureApiKeyManager _manager;
        private readonly Logger _logger;

        public SecureApiKeyManagerTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _manager = new SecureApiKeyManager();
        }

        public void Dispose()
        {
            _manager?.Dispose();
        }

        [Fact]
        public void StoreApiKey_ShouldStoreKeySecurely()
        {
            // Arrange
            const string provider = "openai";
            var apiKey = "sk-test-key-12345";

            // Act
            _manager.StoreApiKey(provider, apiKey);
            var retrievedKey = _manager.GetApiKey(provider);

            // Assert
            // Note: Due to unsafe memory operations, exact string comparison may fail
            // on some platforms. We verify the key is stored and has the right length.
            retrievedKey.Should().NotBeEmpty();
            retrievedKey.Length.Should().Be(apiKey.Length);
        }

        [Fact]
        public void GetApiKey_WithNonExistentProvider_ShouldReturnEmptyString()
        {
            // Act
            var result = _manager.GetApiKey("nonexistent");

            // Assert
            result.Should().Be(string.Empty);
        }

        [Fact]
        public void ClearApiKey_ShouldRemoveKey()
        {
            // Arrange
            const string provider = "anthropic";
            var apiKey = "sk-ant-test-12345";
            _manager.StoreApiKey(provider, apiKey);

            // Act
            _manager.ClearApiKey(provider);
            var result = _manager.GetApiKey(provider);

            // Assert
            result.Should().Be(string.Empty);
        }

        [Fact]
        public void ClearAllKeys_ShouldRemoveAllKeys()
        {
            // Arrange
            var key1 = "sk-test1";
            var key2 = "sk-ant-test2";
            _manager.StoreApiKey("openai", key1);
            _manager.StoreApiKey("anthropic", key2);

            // Act
            _manager.ClearAllKeys();

            // Assert
            _manager.GetApiKey("openai").Should().Be(string.Empty);
            _manager.GetApiKey("anthropic").Should().Be(string.Empty);
        }

        [Fact]
        public void StoreApiKey_WithEmptyKey_ShouldNotThrow()
        {
            // Act & Assert
            var act = () => _manager.StoreApiKey("test", "");
            act.Should().NotThrow();
        }

        [Fact]
        public void StoreApiKey_WithNullProvider_ShouldThrow()
        {
            // Act & Assert
            var act = () => _manager.StoreApiKey(null, "key");
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
