using System;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Security;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NLog;
using Xunit;

namespace Brainarr.Tests.Services.Security
{
    public class SecureApiKeyStorageTests : IDisposable
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();
        private readonly SecureApiKeyStorage _storage;

        public SecureApiKeyStorageTests()
        {
            _storage = new SecureApiKeyStorage(_logger);
        }

        [Fact]
        public void Store_And_GetApiKey_ForRequest_Works()
        {
            _storage.StoreApiKey("TestProvider", "sk-test-123");

            var key = _storage.GetApiKeyForRequest("TestProvider");
            key.Should().Be("sk-test-123");
        }

        [Fact]
        public void UseApiKey_ExecutesOperation_AndThrowsWhenMissing()
        {
            _storage.StoreApiKey("P1", "abc");

            var result = _storage.UseApiKey("P1", k => $"tok:{k}");
            result.Should().Be("tok:abc");

            Action act = () => _storage.UseApiKey("missing", k => k);
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public async Task UseApiKeyAsync_ExecutesOperation()
        {
            _storage.StoreApiKey("AsyncProv", "xyz");

            var result = await _storage.UseApiKeyAsync("AsyncProv", async k =>
            {
                await Task.Yield();
                return k + "!";
            });

            result.Should().Be("xyz!");
        }

        [Fact]
        public void Clear_RemovesKey()
        {
            _storage.StoreApiKey("P2", "123");
            _storage.ClearApiKey("P2");
            _storage.GetApiKeyForRequest("P2").Should().BeNull();
        }

        public void Dispose()
        {
            _storage.Dispose();
        }
    }
}
