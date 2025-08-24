using System;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using Xunit;

namespace Brainarr.Tests.Utils
{
    [Trait("Category", "Unit")]
    public class SafeAsyncHelperTests
    {
        [Fact]
        public void RunSafeSync_WithSuccessfulTask_ReturnsResult()
        {
            // Arrange
            var expectedResult = "test result";
            
            // Act
            var result = SafeAsyncHelper.RunSafeSync(async () =>
            {
                await Task.Delay(10);
                return expectedResult;
            });
            
            // Assert
            result.Should().Be(expectedResult);
        }

        [Fact]
        public void RunSafeSync_WithTimeout_ThrowsTimeoutException()
        {
            // Act & Assert
            Assert.Throws<TimeoutException>(() =>
                SafeAsyncHelper.RunSafeSync(async () =>
                {
                    await Task.Delay(2000); // 2 seconds
                    return "result";
                }, 100)); // 100ms timeout
        }

        [Fact]
        public void RunSafeSync_VoidTask_CompletesSuccessfully()
        {
            // Arrange
            var completed = false;
            
            // Act
            SafeAsyncHelper.RunSafeSync(async () =>
            {
                await Task.Delay(10);
                completed = true;
            });
            
            // Assert
            completed.Should().BeTrue();
        }

        [Fact]
        public void RunSyncWithTimeout_WithTimeout_ReturnsDefault()
        {
            // Act
            var result = SafeAsyncHelper.RunSyncWithTimeout(
                Task.Delay(2000).ContinueWith(_ => "result"), // 2 seconds
                100); // 100ms timeout
            
            // Assert
            result.Should().BeNull(); // default(string) is null
        }

        [Fact]
        public void RunSyncWithTimeout_WithSuccess_ReturnsResult()
        {
            // Arrange
            var expectedResult = "success";
            
            // Act
            var result = SafeAsyncHelper.RunSyncWithTimeout(
                Task.FromResult(expectedResult),
                1000);
            
            // Assert
            result.Should().Be(expectedResult);
        }

        [Fact]
        public void RunSafeSync_WithException_PropagatesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            
            // Act & Assert
            var thrownException = Assert.Throws<InvalidOperationException>(() =>
                SafeAsyncHelper.RunSafeSync<string>(async () =>
                {
                    await Task.Delay(10);
                    throw expectedException;
                }));
            
            thrownException.Message.Should().Be("Test exception");
        }
    }
}