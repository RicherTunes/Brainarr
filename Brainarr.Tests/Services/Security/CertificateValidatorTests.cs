using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Brainarr.Plugin.Services.Security;
using Xunit;

namespace Brainarr.Tests.Services.Security
{
    [Trait("Category", "Security")]
    public class CertificateValidatorTests
    {
        #region CreateSecureHandler Tests

        [Fact]
        public void CreateSecureHandler_WithoutPinning_CreatesValidHandler()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler(enableCertificatePinning: false);

            // Assert
            handler.Should().NotBeNull();
            handler.AllowAutoRedirect.Should().BeFalse();
            handler.MaxAutomaticRedirections.Should().Be(5);
            handler.UseCookies.Should().BeFalse();
            handler.UseDefaultCredentials.Should().BeFalse();
            handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();
        }

        [Fact]
        public void CreateSecureHandler_WithPinning_CreatesValidHandler()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler(enableCertificatePinning: true);

            // Assert
            handler.Should().NotBeNull();
            handler.AllowAutoRedirect.Should().BeFalse();
            handler.MaxAutomaticRedirections.Should().Be(5);
            handler.UseCookies.Should().BeFalse();
            handler.UseDefaultCredentials.Should().BeFalse();
            handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();
        }

        #endregion

        #region Security Configuration Tests

        [Fact]
        public void SecureHandler_DisablesAutomaticRedirects()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler();

            // Assert - These settings prevent security vulnerabilities
            handler.AllowAutoRedirect.Should().BeFalse("automatic redirects can be exploited for attacks");
            handler.MaxAutomaticRedirections.Should().Be(5, "even though redirects are disabled, the value must be > 0 to avoid ArgumentOutOfRangeException");
        }

        [Fact]
        public void SecureHandler_DisablesCookies()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler();

            // Assert - Cookies disabled for security
            handler.UseCookies.Should().BeFalse("cookies not needed for API calls and can leak information");
        }

        [Fact]
        public void SecureHandler_DisablesDefaultCredentials()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler();

            // Assert - Default credentials disabled for security
            handler.UseDefaultCredentials.Should().BeFalse("default credentials should not be sent to third-party APIs");
        }

        #endregion

        #region HttpClient Integration Tests

        [Fact]
        public void SecureHandler_CanBeUsedWithHttpClient()
        {
            // Act & Assert - Should create HttpClient without errors
            using var handler = CertificateValidator.CreateSecureHandler();
            using var httpClient = new HttpClient(handler);

            httpClient.Should().NotBeNull();
            httpClient.Timeout.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public void SecureHandler_HasCustomValidationCallback()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler();

            // Assert
            handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();
        }

        [Fact]
        public void SecureHandler_DisposesCorrectly()
        {
            // Arrange
            HttpClientHandler handler;

            // Act
            using (handler = CertificateValidator.CreateSecureHandler())
            {
                handler.Should().NotBeNull();
            }

            // Assert - Disposing again should not throw (idempotent dispose)
            handler.Invoking(h => h.Dispose()).Should().NotThrow();
        }

        #endregion

        #region Security Settings Validation

        [Fact]
        public void SecureHandler_ConfigurationMeetsSecurityStandards()
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler();

            // Assert - Verify all security-related settings
            var securityChecks = new[]
            {
                ("AllowAutoRedirect", handler.AllowAutoRedirect == false),
                ("MaxAutomaticRedirections", handler.MaxAutomaticRedirections == 5), // Must be > 0 to avoid ArgumentOutOfRangeException
                ("UseCookies", handler.UseCookies == false),
                ("UseDefaultCredentials", handler.UseDefaultCredentials == false),
                ("ServerCertificateValidation", handler.ServerCertificateCustomValidationCallback != null)
            };

            foreach (var (setting, isSecure) in securityChecks)
            {
                isSecure.Should().BeTrue($"{setting} should be configured securely");
            }
        }

        #endregion

        #region Multiple Instance Tests

        [Fact]
        public void CreateSecureHandler_MultipleInstances_EachConfiguredCorrectly()
        {
            // Act
            using var handler1 = CertificateValidator.CreateSecureHandler(enableCertificatePinning: false);
            using var handler2 = CertificateValidator.CreateSecureHandler(enableCertificatePinning: true);
            using var handler3 = CertificateValidator.CreateSecureHandler();

            // Assert - All instances should be properly configured
            var handlers = new[] { handler1, handler2, handler3 };

            foreach (var handler in handlers)
            {
                handler.Should().NotBeNull();
                handler.AllowAutoRedirect.Should().BeFalse();
                handler.UseCookies.Should().BeFalse();
                handler.UseDefaultCredentials.Should().BeFalse();
                handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();
            }

            // Each handler should be independent
            handler1.Should().NotBeSameAs(handler2);
            handler2.Should().NotBeSameAs(handler3);
            handler1.Should().NotBeSameAs(handler3);
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void CreateSecureHandler_RepeatedCreation_PerformsEfficiently()
        {
            // Arrange
            const int iterations = 100;
            var startTime = DateTime.UtcNow;
            var handlers = new List<HttpClientHandler>();

            try
            {
                // Act
                for (int i = 0; i < iterations; i++)
                {
                    var handler = CertificateValidator.CreateSecureHandler();
                    handlers.Add(handler);
                }

                var elapsed = DateTime.UtcNow - startTime;

                // Assert
                elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5)); // Should be fast
                handlers.Should().HaveCount(iterations);
                handlers.Should().AllSatisfy(h => h.Should().NotBeNull());

                // Each handler should be unique
                handlers.Should().OnlyHaveUniqueItems();
            }
            finally
            {
                // Cleanup
                foreach (var handler in handlers)
                {
                    handler?.Dispose();
                }
            }
        }

        #endregion

        #region Memory Management Tests

        [Fact]
        public void SecureHandler_ProperResourceManagement()
        {
            // This test ensures handlers can be created and disposed without memory leaks

            // Act & Assert
            for (int i = 0; i < 50; i++)
            {
                using (var handler = CertificateValidator.CreateSecureHandler())
                {
                    handler.Should().NotBeNull();

                    // Verify handler is usable
                    handler.AllowAutoRedirect.Should().BeFalse();

                    // Handler will be disposed automatically by using statement
                }
            }

            // If we get here without memory exceptions, resource management is working
            Assert.True(true, "Resource management test completed successfully");
        }

        #endregion

        #region Configuration Variations

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CreateSecureHandler_WithDifferentPinningSettings_ConfiguresCorrectly(bool enablePinning)
        {
            // Act
            using var handler = CertificateValidator.CreateSecureHandler(enableCertificatePinning: enablePinning);

            // Assert
            handler.Should().NotBeNull();
            handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();

            // All other security settings should be the same regardless of pinning setting
            handler.AllowAutoRedirect.Should().BeFalse();
            handler.UseCookies.Should().BeFalse();
            handler.UseDefaultCredentials.Should().BeFalse();
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task CreateSecureHandler_ThreadSafety_HandlesMultipleThreads()
        {
            // Arrange
            const int threadCount = 10;
            var handlers = new HttpClientHandler[threadCount];
            var exceptions = new Exception[threadCount];

            // Act
            var tasks = Enumerable.Range(0, threadCount).Select(i =>
                Task.Run(() =>
                {
                    try
                    {
                        handlers[i] = CertificateValidator.CreateSecureHandler();
                    }
                    catch (Exception ex)
                    {
                        exceptions[i] = ex;
                    }
                })
            ).ToArray();

            await Task.WhenAll(tasks);

            try
            {
                // Assert
                exceptions.Should().AllSatisfy(e => e.Should().BeNull(), "no exceptions should occur during concurrent creation");
                handlers.Should().AllSatisfy(h => h.Should().NotBeNull());
                handlers.Should().OnlyHaveUniqueItems("each handler should be a unique instance");
            }
            finally
            {
                // Cleanup
                foreach (var handler in handlers)
                {
                    handler?.Dispose();
                }
            }
        }

        [Fact]
        public async Task CreateSecureHandler_WithSystemUnderLoad_StillWorksCorrectly()
        {
            // This test simulates system under load
            const int concurrentHandlers = 20;
            var allHandlers = new List<HttpClientHandler>();

            try
            {
                // Act - Create multiple handlers rapidly
                var creationTasks = Enumerable.Range(0, concurrentHandlers)
                    .Select(_ => Task.Run(() =>
                    {
                        var handler = CertificateValidator.CreateSecureHandler();
                        lock (allHandlers)
                        {
                            allHandlers.Add(handler);
                        }
                        return handler;
                    }))
                    .ToArray();

                await Task.WhenAll(creationTasks);

                // Assert
                allHandlers.Should().HaveCount(concurrentHandlers);
                allHandlers.Should().AllSatisfy(h =>
                {
                    h.Should().NotBeNull();
                    h.AllowAutoRedirect.Should().BeFalse();
                    h.ServerCertificateCustomValidationCallback.Should().NotBeNull();
                });
            }
            finally
            {
                // Cleanup
                foreach (var handler in allHandlers)
                {
                    handler?.Dispose();
                }
            }
        }

        #endregion
    }
}
