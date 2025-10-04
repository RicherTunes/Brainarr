using Brainarr.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Hosting;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
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
            var logger = TestLogger.CreateNullLogger();

            using var provider = module.BuildServiceProvider(services =>
            {
                services.AddSingleton(logger);
                services.AddSingleton(httpClient.Object);
                services.AddSingleton(artistService.Object);
                services.AddSingleton(albumService.Object);
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
            var logger = TestLogger.CreateNullLogger();

            services.AddSingleton(logger);
            services.AddSingleton(httpClient.Object);
            services.AddSingleton(artistService.Object);
            services.AddSingleton(albumService.Object);

            BrainarrOrchestratorFactory.ConfigureServices(services);

            using var provider = services.BuildServiceProvider();

            var cache1 = provider.GetRequiredService<IPlanCache>();
            var cache2 = provider.GetRequiredService<IPlanCache>();
            Assert.Same(cache1, cache2);

            var promptBuilder = provider.GetRequiredService<ILibraryAwarePromptBuilder>();
            Assert.NotNull(promptBuilder);
        }
    }
}
