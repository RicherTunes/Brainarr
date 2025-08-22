using System;
using Xunit;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace Brainarr.Tests.Services.Core
{
    public class ServiceConfigurationTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<Logger> _loggerMock;
        private readonly ServiceConfiguration _serviceConfiguration;

        public ServiceConfigurationTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _loggerMock = new Mock<Logger>();
            _serviceConfiguration = new ServiceConfiguration(_httpClientMock.Object, _loggerMock.Object);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ServiceConfiguration(null, _loggerMock.Object));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ServiceConfiguration(_httpClientMock.Object, null));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ModelDetection_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.ModelDetection;
            var instance2 = _serviceConfiguration.ModelDetection;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Cache_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.Cache;
            var instance2 = _serviceConfiguration.Cache;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void HealthMonitor_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.HealthMonitor;
            var instance2 = _serviceConfiguration.HealthMonitor;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void RetryPolicy_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.RetryPolicy;
            var instance2 = _serviceConfiguration.RetryPolicy;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void RateLimiter_ShouldReturnConfiguredInstance()
        {
            var rateLimiter = _serviceConfiguration.RateLimiter;

            Assert.NotNull(rateLimiter);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ProviderFactory_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.ProviderFactory;
            var instance2 = _serviceConfiguration.ProviderFactory;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void PromptBuilder_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.PromptBuilder;
            var instance2 = _serviceConfiguration.PromptBuilder;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void IterativeStrategy_ShouldReturnSameInstance_WhenCalledMultipleTimes()
        {
            var instance1 = _serviceConfiguration.IterativeStrategy;
            var instance2 = _serviceConfiguration.IterativeStrategy;

            Assert.NotNull(instance1);
            Assert.Same(instance1, instance2);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CreateProvider_ShouldThrowArgumentNullException_WhenSettingsIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _serviceConfiguration.CreateProvider(null));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CreateProvider_ShouldReturnNull_WhenProviderNotSupported()
        {
            var settings = new BrainarrSettings
            {
                Provider = (AIProvider)999
            };

            var provider = _serviceConfiguration.CreateProvider(settings);

            Assert.Null(provider);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ConfigureRateLimiter_ShouldNotThrow_WhenRateLimiterIsNull()
        {
            var exception = Record.Exception(() => _serviceConfiguration.ConfigureRateLimiter(null));
            
            Assert.Null(exception);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void AllServices_ShouldBeInitializedCorrectly()
        {
            Assert.NotNull(_serviceConfiguration.ModelDetection);
            Assert.NotNull(_serviceConfiguration.Cache);
            Assert.NotNull(_serviceConfiguration.HealthMonitor);
            Assert.NotNull(_serviceConfiguration.RetryPolicy);
            Assert.NotNull(_serviceConfiguration.RateLimiter);
            Assert.NotNull(_serviceConfiguration.ProviderFactory);
            Assert.NotNull(_serviceConfiguration.PromptBuilder);
            Assert.NotNull(_serviceConfiguration.IterativeStrategy);
        }
    }
}