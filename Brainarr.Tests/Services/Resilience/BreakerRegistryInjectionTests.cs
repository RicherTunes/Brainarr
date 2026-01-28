using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Resilience
{
    /// <summary>
    /// Shallow tests proving DI wiring for IBreakerRegistry is functional.
    /// These tests verify the seam exists and can be injected, not full breaker behavior.
    /// </summary>
    public sealed class BreakerRegistryInjectionTests
    {
        [Fact]
        public void Factory_Registers_IBreakerRegistry()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton(LogManager.GetCurrentClassLogger());
            services.AddSingleton(Mock.Of<IHttpClient>());
            services.AddSingleton(Mock.Of<IArtistService>());
            services.AddSingleton(Mock.Of<IAlbumService>());

            // Act
            BrainarrOrchestratorFactory.ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            // Assert
            var registry = provider.GetService<IBreakerRegistry>();
            registry.Should().NotBeNull("IBreakerRegistry should be registered by factory");
            registry.Should().BeOfType<CommonBreakerRegistry>();
        }

        [Fact]
        public void Orchestrator_Accepts_Injected_BreakerRegistry()
        {
            // Arrange
            var mockRegistry = new Mock<IBreakerRegistry>();
            var mockBreaker = new Mock<ICircuitBreaker>();
            mockBreaker.Setup(b => b.State).Returns(CircuitState.Closed);
            mockRegistry
                .Setup(r => r.Get(It.IsAny<ModelKey>(), It.IsAny<Logger>(), It.IsAny<CircuitBreakerOptions?>()))
                .Returns(mockBreaker.Object);

            var services = new ServiceCollection();
            services.AddSingleton(LogManager.GetCurrentClassLogger());
            services.AddSingleton(Mock.Of<IHttpClient>());
            services.AddSingleton(Mock.Of<IArtistService>());
            services.AddSingleton(Mock.Of<IAlbumService>());

            // Replace default registry with mock
            services.AddSingleton<IBreakerRegistry>(mockRegistry.Object);

            // Act
            BrainarrOrchestratorFactory.ConfigureServices(services);
            var provider = services.BuildServiceProvider();
            var orchestrator = provider.GetService<IBrainarrOrchestrator>();

            // Assert
            orchestrator.Should().NotBeNull("Orchestrator should be constructable with injected registry");

            // Verify the mock registry is the one that got used (not a default)
            var resolvedRegistry = provider.GetService<IBreakerRegistry>();
            resolvedRegistry.Should().BeSameAs(mockRegistry.Object);
        }

        [Fact]
        public void DI_Resolves_Same_Registry_Instance_For_Multiple_Orchestrators()
        {
            // IMPORTANT: IBreakerRegistry must be singleton to preserve breaker state across
            // orchestrator instances. If transient, breaker history would reset per instance.
            var services = new ServiceCollection();
            services.AddSingleton(LogManager.GetCurrentClassLogger());
            services.AddSingleton(Mock.Of<IHttpClient>());
            services.AddSingleton(Mock.Of<IArtistService>());
            services.AddSingleton(Mock.Of<IAlbumService>());

            BrainarrOrchestratorFactory.ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            // Act - resolve registry twice
            var registry1 = provider.GetRequiredService<IBreakerRegistry>();
            var registry2 = provider.GetRequiredService<IBreakerRegistry>();

            // Assert - must be same instance (singleton)
            registry1.Should().BeSameAs(registry2, "IBreakerRegistry must be singleton to preserve state");
        }

        [Fact]
        public void Orchestrator_Has_No_Static_BreakerRegistry_Field()
        {
            // Verify we removed the static Lazy<IBreakerRegistry> field.
            // This prevents hidden cross-test contamination and ensures PR3 deletion is clean.
            var orchestratorType = typeof(BrainarrOrchestrator);
            var staticFields = orchestratorType
                .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Where(f => f.FieldType == typeof(IBreakerRegistry) ||
                           (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Lazy<>) &&
                            f.FieldType.GetGenericArguments()[0] == typeof(IBreakerRegistry)))
                .ToList();

            staticFields.Should().BeEmpty("No static IBreakerRegistry or Lazy<IBreakerRegistry> fields should exist");
        }
    }
}
