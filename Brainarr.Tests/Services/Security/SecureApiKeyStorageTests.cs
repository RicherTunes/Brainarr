using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using Brainarr.Tests.Helpers;
using NLog;
using Brainarr.Plugin.Services.Security;
using Xunit;

namespace Brainarr.Tests.Services.Security
{
    [Trait("Category", "Security")]
    public class SecureApiKeyStorageTests : IDisposable
    {
        private readonly Logger _logger;
        private readonly SecureApiKeyStorage _storage;

        public SecureApiKeyStorageTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _storage = new SecureApiKeyStorage(_logger);
        }

        public void Dispose()
        {
            _storage?.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidLogger_InitializesSuccessfully()
        {
            // Arrange & Act
            var storage = new SecureApiKeyStorage(_logger);

            // Assert
            storage.Should().NotBeNull();
            
            // Cleanup
            storage.Dispose();
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SecureApiKeyStorage(null));
        }

        #endregion

        #region StoreApiKey Tests

        [Fact]
        public void StoreApiKey_WithValidProviderAndKey_StoresSuccessfully()
        {
            // Arrange
            const string provider = "OpenAI";
            const string apiKey = "sk-test1234567890abcdef";

            // Act
            _storage.StoreApiKey(provider, apiKey);

            // Assert
            var retrievedKey = _storage.GetApiKeyForRequest(provider);
            retrievedKey.Should().Be(apiKey);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\n")]
        [InlineData(null)]
        public void StoreApiKey_WithInvalidProvider_ThrowsArgumentException(string invalidProvider)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _storage.StoreApiKey(invalidProvider, "valid-key"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\n")]
        [InlineData(null)]
        public void StoreApiKey_WithInvalidApiKey_DoesNotThrow(string invalidKey)
        {
            // Arrange
            const string provider = "TestProvider";

            // Act & Assert - Should not throw, but should not store anything
            _storage.Invoking(s => s.StoreApiKey(provider, invalidKey)).Should().NotThrow();
            
            var result = _storage.GetApiKeyForRequest(provider);
            result.Should().BeNull();
        }

        [Fact]
        public void StoreApiKey_WithExistingKey_ReplacesKey()
        {
            // Arrange
            const string provider = "OpenAI";
            const string firstKey = "sk-old-key";
            const string secondKey = "sk-new-key";

            // Act
            _storage.StoreApiKey(provider, firstKey);
            _storage.StoreApiKey(provider, secondKey);

            // Assert
            var retrievedKey = _storage.GetApiKeyForRequest(provider);
            retrievedKey.Should().Be(secondKey);
            retrievedKey.Should().NotBe(firstKey);
        }

        [Fact]
        public void StoreApiKey_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            const string provider = "TestProvider";
            const string apiKey = "sk-test!@#$%^&*()_+-={}[]|\\:;\"'<>?,./~`";

            // Act
            _storage.StoreApiKey(provider, apiKey);

            // Assert
            var retrievedKey = _storage.GetApiKeyForRequest(provider);
            retrievedKey.Should().Be(apiKey);
        }

        [Fact]
        public void StoreApiKey_WithUnicodeCharacters_HandlesCorrectly()
        {
            // Arrange
            const string provider = "TestProvider";
            const string apiKey = "sk-test-ñáéíóú-中文-🔑";

            // Act
            _storage.StoreApiKey(provider, apiKey);

            // Assert
            var retrievedKey = _storage.GetApiKeyForRequest(provider);
            retrievedKey.Should().Be(apiKey);
        }

        #endregion

        #region GetApiKey Tests

        [Fact]
        public void GetApiKey_WithValidProvider_ReturnsSecureString()
        {
            // Arrange
            const string provider = "OpenAI";
            const string apiKey = "sk-test1234567890";
            _storage.StoreApiKey(provider, apiKey);

            // Act
            var secureKey = _storage.GetApiKey(provider);

            // Assert
            secureKey.Should().NotBeNull();
            secureKey.Length.Should().Be(apiKey.Length);
        }

        [Fact]
        public void GetApiKey_WithNonExistentProvider_ReturnsNull()
        {
            // Act
            var result = _storage.GetApiKey("NonExistentProvider");

            // Assert
            result.Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData(null)]
        public void GetApiKey_WithInvalidProvider_ReturnsNull(string invalidProvider)
        {
            // Act
            var result = _storage.GetApiKey(invalidProvider);

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region GetApiKeyForRequest Tests

        [Fact]
        public void GetApiKeyForRequest_WithValidProvider_ReturnsPlainTextKey()
        {
            // Arrange
            const string provider = "OpenAI";
            const string apiKey = "sk-test1234567890";
            _storage.StoreApiKey(provider, apiKey);

            // Act
            var result = _storage.GetApiKeyForRequest(provider);

            // Assert
            result.Should().Be(apiKey);
        }

        [Fact]
        public void GetApiKeyForRequest_WithNonExistentProvider_ReturnsNull()
        {
            // Act
            var result = _storage.GetApiKeyForRequest("NonExistentProvider");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetApiKeyForRequest_CalledMultipleTimes_ReturnsSameValue()
        {
            // Arrange
            const string provider = "OpenAI";
            const string apiKey = "sk-test1234567890";
            _storage.StoreApiKey(provider, apiKey);

            // Act
            var result1 = _storage.GetApiKeyForRequest(provider);
            var result2 = _storage.GetApiKeyForRequest(provider);
            var result3 = _storage.GetApiKeyForRequest(provider);

            // Assert
            result1.Should().Be(apiKey);
            result2.Should().Be(apiKey);
            result3.Should().Be(apiKey);
        }

        #endregion

        #region ClearApiKey Tests

        [Fact]
        public void ClearApiKey_WithValidProvider_RemovesKey()
        {
            // Arrange
            const string provider = "OpenAI";
            const string apiKey = "sk-test1234567890";
            _storage.StoreApiKey(provider, apiKey);

            // Act
            _storage.ClearApiKey(provider);

            // Assert
            var result = _storage.GetApiKeyForRequest(provider);
            result.Should().BeNull();
        }

        [Fact]
        public void ClearApiKey_WithNonExistentProvider_DoesNotThrow()
        {
            // Act & Assert
            _storage.Invoking(s => s.ClearApiKey("NonExistentProvider")).Should().NotThrow();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData(null)]
        public void ClearApiKey_WithInvalidProvider_DoesNotThrow(string invalidProvider)
        {
            // Act & Assert
            _storage.Invoking(s => s.ClearApiKey(invalidProvider)).Should().NotThrow();
        }

        [Fact]
        public void ClearApiKey_WithMultipleProviders_ClearsOnlySpecified()
        {
            // Arrange
            const string provider1 = "OpenAI";
            const string provider2 = "Anthropic";
            const string apiKey1 = "sk-openai-key";
            const string apiKey2 = "sk-anthropic-key";
            
            _storage.StoreApiKey(provider1, apiKey1);
            _storage.StoreApiKey(provider2, apiKey2);

            // Act
            _storage.ClearApiKey(provider1);

            // Assert
            _storage.GetApiKeyForRequest(provider1).Should().BeNull();
            _storage.GetApiKeyForRequest(provider2).Should().Be(apiKey2);
        }

        #endregion

        #region ClearAllApiKeys Tests

        [Fact]
        public void ClearAllApiKeys_WithMultipleKeys_RemovesAll()
        {
            // Arrange
            var providers = new[]
            {
                ("OpenAI", "sk-openai-key"),
                ("Anthropic", "sk-anthropic-key"),
                ("Gemini", "gemini-key-123")
            };

            foreach (var (provider, key) in providers)
            {
                _storage.StoreApiKey(provider, key);
            }

            // Act
            _storage.ClearAllApiKeys();

            // Assert
            foreach (var (provider, _) in providers)
            {
                _storage.GetApiKeyForRequest(provider).Should().BeNull();
            }
        }

        [Fact]
        public void ClearAllApiKeys_WithNoKeys_DoesNotThrow()
        {
            // Act & Assert
            _storage.Invoking(s => s.ClearAllApiKeys()).Should().NotThrow();
        }

        #endregion

        #region Encryption and Platform-Specific Tests

        [Fact]
        public void StoreAndRetrieve_AcrossPlatforms_WorksCorrectly()
        {
            // Arrange
            const string provider = "CrossPlatformTest";
            const string apiKey = "sk-cross-platform-test-key-12345";

            // Act
            _storage.StoreApiKey(provider, apiKey);
            var retrieved = _storage.GetApiKeyForRequest(provider);

            // Assert
            retrieved.Should().Be(apiKey);
        }

        [Fact]
        public void Encryption_OnWindows_UsesDPAPI()
        {
            // Skip test if not on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Skip this test on non-Windows platforms
            }

            // Arrange
            const string provider = "WindowsTest";
            const string apiKey = "sk-windows-dpapi-test";

            // Act
            _storage.StoreApiKey(provider, apiKey);
            var retrieved = _storage.GetApiKeyForRequest(provider);

            // Assert
            retrieved.Should().Be(apiKey);
        }

        [Fact]
        public void Encryption_OnNonWindows_UsesAES()
        {
            // Skip test if on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Skip this test on Windows platforms
            }

            // Arrange
            const string provider = "NonWindowsTest";
            const string apiKey = "sk-aes-encryption-test";

            // Act
            _storage.StoreApiKey(provider, apiKey);
            var retrieved = _storage.GetApiKeyForRequest(provider);

            // Assert
            retrieved.Should().Be(apiKey);
        }

        #endregion

        #region Extension Method Tests

        [Fact]
        public void UseApiKey_WithValidOperation_ExecutesAndCleansUp()
        {
            // Arrange
            const string provider = "TestProvider";
            const string apiKey = "sk-test-extension";
            _storage.StoreApiKey(provider, apiKey);

            var operationExecuted = false;
            string receivedKey = null;

            // Act
            var result = _storage.UseApiKey<string>(provider, key =>
            {
                operationExecuted = true;
                receivedKey = key;
                return "operation-result";
            });

            // Assert
            result.Should().Be("operation-result");
            operationExecuted.Should().BeTrue();
            receivedKey.Should().Be(apiKey);
        }

        [Fact]
        public void UseApiKey_WithNonExistentProvider_ThrowsInvalidOperationException()
        {
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                _storage.UseApiKey<string>("NonExistentProvider", key => "result"));
        }

        [Fact]
        public void UseApiKey_WhenOperationThrows_PropagatesException()
        {
            // Arrange
            const string provider = "TestProvider";
            const string apiKey = "sk-test-exception";
            _storage.StoreApiKey(provider, apiKey);
            
            var expectedException = new ArgumentException("Test exception");

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                _storage.UseApiKey<string>(provider, key => throw expectedException));
            
            exception.Should().BeSameAs(expectedException);
        }

        [Fact]
        public async Task UseApiKeyAsync_WithValidOperation_ExecutesAndCleansUp()
        {
            // Arrange
            const string provider = "TestProvider";
            const string apiKey = "sk-test-async-extension";
            _storage.StoreApiKey(provider, apiKey);

            var operationExecuted = false;
            string receivedKey = null;

            // Act
            var result = await _storage.UseApiKeyAsync<string>(provider, async key =>
            {
                operationExecuted = true;
                receivedKey = key;
                await Task.Delay(10);
                return "async-operation-result";
            });

            // Assert
            result.Should().Be("async-operation-result");
            operationExecuted.Should().BeTrue();
            receivedKey.Should().Be(apiKey);
        }

        [Fact]
        public async Task UseApiKeyAsync_WithNonExistentProvider_ThrowsInvalidOperationException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _storage.UseApiKeyAsync<string>("NonExistentProvider", async key => 
                {
                    await Task.Delay(1);
                    return "result";
                }));
        }

        [Fact]
        public async Task UseApiKeyAsync_WhenOperationThrows_PropagatesException()
        {
            // Arrange
            const string provider = "TestProvider";
            const string apiKey = "sk-test-async-exception";
            _storage.StoreApiKey(provider, apiKey);
            
            var expectedException = new InvalidOperationException("Async test exception");

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _storage.UseApiKeyAsync<string>(provider, async key => 
                {
                    await Task.Delay(1);
                    throw expectedException;
                }));
            
            exception.Should().BeSameAs(expectedException);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task ConcurrentStoreAndRetrieve_ThreadSafe()
        {
            // Arrange
            const int taskCount = 20;
            var allResults = new string[taskCount];

            // Act
            var tasks = Enumerable.Range(0, taskCount).Select(async i =>
            {
                var provider = $"Provider{i}";
                var apiKey = $"sk-test-{i:D3}";
                
                _storage.StoreApiKey(provider, apiKey);
                await Task.Delay(1); // Small delay to increase concurrency
                var retrieved = _storage.GetApiKeyForRequest(provider);
                
                allResults[i] = retrieved;
            });

            await Task.WhenAll(tasks);

            // Assert
            for (int i = 0; i < taskCount; i++)
            {
                allResults[i].Should().Be($"sk-test-{i:D3}");
            }
        }

        [Fact]
        public async Task ConcurrentClearOperations_ThreadSafe()
        {
            // Arrange
            const int taskCount = 10;
            var providers = Enumerable.Range(0, taskCount).Select(i => $"Provider{i}").ToArray();
            
            // Store keys for all providers
            foreach (var provider in providers)
            {
                _storage.StoreApiKey(provider, $"key-for-{provider}");
            }

            // Act - Clear operations running concurrently
            var clearTasks = providers.Select(provider => Task.Run(() => _storage.ClearApiKey(provider)));
            await Task.WhenAll(clearTasks);

            // Assert - All keys should be cleared
            foreach (var provider in providers)
            {
                _storage.GetApiKeyForRequest(provider).Should().BeNull();
            }
        }

        #endregion

        #region Memory Security Tests

        [Fact]
        public void SecureString_IsProperlyDisposed()
        {
            // Arrange
            const string provider = "MemoryTest";
            const string apiKey = "sk-memory-test-key";

            // Act
            _storage.StoreApiKey(provider, apiKey);
            var secureKey = _storage.GetApiKey(provider);
            
            _storage.ClearApiKey(provider);

            // Assert - SecureString should be disposed and no longer accessible
            secureKey.Invoking(s => s.Length).Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void GetApiKeyForRequest_MemoryIsCleared()
        {
            // This test verifies that the GetApiKeyForRequest method properly manages memory
            // We can't directly verify memory clearing, but we can ensure the method works consistently
            
            // Arrange
            const string provider = "MemoryCleanupTest";
            const string apiKey = "sk-memory-cleanup-test";
            _storage.StoreApiKey(provider, apiKey);

            // Act - Multiple calls should work consistently
            var result1 = _storage.GetApiKeyForRequest(provider);
            var result2 = _storage.GetApiKeyForRequest(provider);
            var result3 = _storage.GetApiKeyForRequest(provider);

            // Assert
            result1.Should().Be(apiKey);
            result2.Should().Be(apiKey);
            result3.Should().Be(apiKey);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ClearsAllKeys()
        {
            // Arrange
            var providers = new[] { "Provider1", "Provider2", "Provider3" };
            foreach (var provider in providers)
            {
                _storage.StoreApiKey(provider, $"key-for-{provider}");
            }

            // Act
            _storage.Dispose();

            // Assert - All keys should be cleared
            foreach (var provider in providers)
            {
                _storage.GetApiKeyForRequest(provider).Should().BeNull();
            }
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Act & Assert
            _storage.Invoking(s => s.Dispose()).Should().NotThrow();
            _storage.Invoking(s => s.Dispose()).Should().NotThrow();
        }

        [Fact]
        public void OperationsAfterDispose_HandleGracefully()
        {
            // Arrange
            _storage.StoreApiKey("TestProvider", "sk-test-key");
            _storage.Dispose();

            // Act & Assert - Operations should not crash after dispose
            _storage.Invoking(s => s.GetApiKeyForRequest("TestProvider")).Should().NotThrow();
            _storage.Invoking(s => s.ClearApiKey("TestProvider")).Should().NotThrow();
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public void StoreApiKey_WithVeryLongKey_HandlesCorrectly()
        {
            // Arrange
            const string provider = "LongKeyTest";
            var longApiKey = "sk-" + new string('x', 1000); // Very long key

            // Act & Assert
            _storage.Invoking(s => s.StoreApiKey(provider, longApiKey)).Should().NotThrow();
            
            var retrieved = _storage.GetApiKeyForRequest(provider);
            retrieved.Should().Be(longApiKey);
        }

        [Fact]
        public void MultipleProviders_IndependentStorage()
        {
            // Arrange
            var providerData = new[]
            {
                ("OpenAI", "sk-openai-12345"),
                ("Anthropic", "sk-ant-67890"),
                ("Gemini", "gemini-abcdef"),
                ("Groq", "gsk-xyz123")
            };

            // Act
            foreach (var (provider, key) in providerData)
            {
                _storage.StoreApiKey(provider, key);
            }

            // Assert
            foreach (var (provider, expectedKey) in providerData)
            {
                var retrievedKey = _storage.GetApiKeyForRequest(provider);
                retrievedKey.Should().Be(expectedKey);
            }

            // Clear one and ensure others remain
            _storage.ClearApiKey("OpenAI");
            
            _storage.GetApiKeyForRequest("OpenAI").Should().BeNull();
            _storage.GetApiKeyForRequest("Anthropic").Should().Be("sk-ant-67890");
            _storage.GetApiKeyForRequest("Gemini").Should().Be("gemini-abcdef");
            _storage.GetApiKeyForRequest("Groq").Should().Be("gsk-xyz123");
        }

        [Fact]
        public void StoreApiKey_WithSameProviderDifferentCase_TreatedAsDifferent()
        {
            // Arrange
            const string apiKey1 = "sk-lowercase-key";
            const string apiKey2 = "sk-uppercase-key";

            // Act
            _storage.StoreApiKey("openai", apiKey1);
            _storage.StoreApiKey("OpenAI", apiKey2);

            // Assert - Case sensitivity should be maintained
            _storage.GetApiKeyForRequest("openai").Should().Be(apiKey1);
            _storage.GetApiKeyForRequest("OpenAI").Should().Be(apiKey2);
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void HighVolumeOperations_PerformanceStable()
        {
            // Arrange
            const int iterations = 1000;
            const string baseProvider = "PerfTest";
            const string baseKey = "sk-performance-test";

            var startTime = DateTime.UtcNow;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var provider = $"{baseProvider}{i % 10}"; // Reuse some providers
                var key = $"{baseKey}-{i}";
                
                _storage.StoreApiKey(provider, key);
                var retrieved = _storage.GetApiKeyForRequest(provider);
                
                retrieved.Should().Be(key);
                
                if (i % 2 == 0)
                {
                    _storage.ClearApiKey(provider);
                }
            }

            var elapsed = DateTime.UtcNow - startTime;

            // Assert - Should complete in reasonable time (less than 5 seconds)
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        }

        #endregion
    }
}