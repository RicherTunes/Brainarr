using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using Xunit;

namespace Brainarr.Tests.Services.Enrichment
{
    public class ArtistMbidResolverBestEffortTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{}")
                };
                return Task.FromResult(response);
            }
        }

        [Fact]
        public async Task EnrichArtistsAsync_PreservesOriginalRecommendation_WhenLookupFails()
        {
            // Arrange
            var httpClient = new HttpClient(new StubHandler());
            var resolver = new ArtistMbidResolver(TestLogger.CreateNullLogger(), httpClient);
            var rec = new Recommendation { Artist = "Radiohead", Confidence = 0.9 };

            // Act
            var result = await resolver.EnrichArtistsAsync(new List<Recommendation> { rec }, CancellationToken.None);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Radiohead");
            result[0].ArtistMusicBrainzId.Should().BeNull();
        }
    }
}
