using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    public class MusicBrainzServiceCacheTests
    {
        private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler stub)
        {
            stub = new StubHttpMessageHandler(responder);
            return new HttpClient(stub, disposeHandler: true);
        }

        private static Logger CreateLogger() => LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task ValidateArtistAsync_PositiveResult_IsCached()
        {
            // Arrange
            var payload = "{\"count\":1,\"artists\":[{\"id\":\"x\",\"name\":\"a\"}]}";
            var client = CreateClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            }, out var stub);

            var svc = new MusicBrainzService(client, CreateLogger());

            // Act
            var ok1 = await svc.ValidateArtistAsync("Some Artist");
            var ok2 = await svc.ValidateArtistAsync("Some Artist");

            // Assert
            ok1.Should().BeTrue();
            ok2.Should().BeTrue();
            stub.CallCount.Should().Be(1); // second call served from cache
        }

        [Fact]
        public async Task SearchArtistAlbumAsync_NegativeResult_IsCached()
        {
            // Arrange: return NotFound to avoid retry; should be cached as negative
            var client = CreateClient(req => new HttpResponseMessage(HttpStatusCode.NotFound), out var stub);
            var svc = new MusicBrainzService(client, CreateLogger());

            // Act
            var res1 = await svc.SearchArtistAlbumAsync("A", "B");
            var res2 = await svc.SearchArtistAlbumAsync("A", "B");

            // Assert
            res1.Found.Should().BeFalse();
            res2.Found.Should().BeFalse();
            stub.CallCount.Should().Be(1); // second call hit negative cache
        }
    }
}
