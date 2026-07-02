using System.Collections.Generic;
using System.Text.Json;
using Brainarr.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Hosting;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrModuleBootstrapTests
    {
        [Fact]
        public void BuildServiceProvider_ResolvesOrchestrator()
        {
            var module = new BrainarrModule();
            var httpClient = new Mock<IHttpClient>();
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            var mediaFileService = new Mock<IMediaFileService>();
            var audioTagService = new Mock<IAudioTagService>();
            var logger = TestLogger.CreateNullLogger();

            using var provider = module.BuildServiceProvider(services =>
            {
                services.AddSingleton(logger);
                services.AddSingleton(httpClient.Object);
                services.AddSingleton(artistService.Object);
                services.AddSingleton(albumService.Object);
                services.AddSingleton(mediaFileService.Object);
                services.AddSingleton(audioTagService.Object);
            });

            var orchestrator = provider.GetRequiredService<IBrainarrOrchestrator>();
            Assert.NotNull(orchestrator);
        }

        [Fact]
        public void ConfigureServices_RegistersSingletonPlanCache()
        {
            var services = new ServiceCollection();
            var httpClient = new Mock<IHttpClient>();
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            var mediaFileService = new Mock<IMediaFileService>();
            var audioTagService = new Mock<IAudioTagService>();
            var logger = TestLogger.CreateNullLogger();

            services.AddSingleton(logger);
            services.AddSingleton(httpClient.Object);
            services.AddSingleton(artistService.Object);
            services.AddSingleton(albumService.Object);
            services.AddSingleton(mediaFileService.Object);
            services.AddSingleton(audioTagService.Object);

            BrainarrOrchestratorFactory.ConfigureServices(services);

            using var provider = services.BuildServiceProvider();

            var cache1 = provider.GetRequiredService<IPlanCache>();
            var cache2 = provider.GetRequiredService<IPlanCache>();
            Assert.Same(cache1, cache2);

            var promptBuilder = provider.GetRequiredService<ILibraryAwarePromptBuilder>();
            Assert.NotNull(promptBuilder);
        }

        [Fact]
        public void Create_WithHostServices_RoutesHealerActions()
        {
            var orchestrator = BrainarrOrchestratorFactory.Create(
                TestLogger.CreateNullLogger(),
                Mock.Of<IHttpClient>(),
                Mock.Of<IArtistService>(),
                Mock.Of<IAlbumService>(),
                Mock.Of<IMediaFileService>(),
                Mock.Of<IAudioTagService>());

            var result = orchestrator.HandleAction(
                "healer/getfindings",
                new Dictionary<string, string>(),
                new BrainarrSettings());
            var json = JsonSerializer.Serialize(result);

            Assert.Contains("\"items\"", json);
            Assert.DoesNotContain("not available", json);
        }
    }
}
