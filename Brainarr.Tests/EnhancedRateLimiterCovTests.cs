using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiting;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for EnhancedRateLimiter.cs - tests uncovered paths.
    /// Source: Brainarr.Plugin/Services/RateLimiting/EnhancedRateLimiter.cs
    /// </summary>
    public class EnhancedRateLimiterCovTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        #region ConfigureLimit Validation (Lines 142-145)

        [Fact]
        public void ConfigureLimit_EmptyResource_ThrowsArgumentException_Line142()
        {
            // Arrange - Line 142: throw new ArgumentException("Resource name is required", nameof(resource));
            var limiter = new EnhancedRateLimiter(_logger);
            var policy = new RateLimitPolicy { MaxRequests = 10, Period = TimeSpan.FromSeconds(1) };

            // Act
            var act = () => limiter.ConfigureLimit("", policy);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("resource")
                .WithMessage("*Resource name is required*");
        }

        [Fact]
        public void ConfigureLimit_NullResource_ThrowsArgumentException_Line142()
        {
            // Arrange - Line 142: throw new ArgumentException("Resource name is required", nameof(resource));
            var limiter = new EnhancedRateLimiter(_logger);
            var policy = new RateLimitPolicy { MaxRequests = 10, Period = TimeSpan.FromSeconds(1) };

            // Act
            var act = () => limiter.ConfigureLimit(null!, policy);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("resource")
                .WithMessage("*Resource name is required*");
        }

        [Fact]
        public void ConfigureLimit_NullPolicy_ThrowsArgumentNullException_Line145()
        {
            // Arrange - Line 145: throw new ArgumentNullException(nameof(policy));
            var limiter = new EnhancedRateLimiter(_logger);

            // Act
            var act = () => limiter.ConfigureLimit("testResource", null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("policy");
        }

        #endregion

        #region ValidateRequest (Lines 212-215)

        [Fact]
        public async Task CheckRateLimitAsync_NullRequest_ThrowsArgumentNullException_Line212()
        {
            // Arrange - Line 212: throw new ArgumentNullException(nameof(request));
            var limiter = new EnhancedRateLimiter(_logger);

            // Act
            var act = async () => await limiter.CheckRateLimitAsync(null!);

            // Assert
            await act.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("request");
        }

        [Fact]
        public async Task CheckRateLimitAsync_EmptyResource_ThrowsArgumentException_Line215()
        {
            // Arrange - Line 215: throw new ArgumentException("Resource is required", nameof(request));
            var limiter = new EnhancedRateLimiter(_logger);
            var request = new RateLimitRequest { Resource = "", UserId = "user1" };

            // Act
            var act = async () => await limiter.CheckRateLimitAsync(request);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("request")
                .WithMessage("*Resource is required*");
        }

        [Fact]
        public async Task CheckRateLimitAsync_NullResource_ThrowsArgumentException_Line215()
        {
            // Arrange - Line 215: throw new ArgumentException("Resource is required", nameof(request));
            var limiter = new EnhancedRateLimiter(_logger);
            var request = new RateLimitRequest { Resource = null!, UserId = "user1" };

            // Act
            var act = async () => await limiter.CheckRateLimitAsync(request);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("request")
                .WithMessage("*Resource is required*");
        }

        [Fact]
        public async Task ExecuteAsync_NullRequest_ThrowsArgumentNullException_Line212()
        {
            // Arrange - Line 212: throw new ArgumentNullException(nameof(request));
            var limiter = new EnhancedRateLimiter(_logger);

            // Act
            var act = async () => await limiter.ExecuteAsync(null!, () => Task.FromResult(42));

            // Assert
            await act.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("request");
        }

        [Fact]
        public async Task ExecuteAsync_EmptyResource_ThrowsArgumentException_Line215()
        {
            // Arrange - Line 215: throw new ArgumentException("Resource is required", nameof(request));
            var limiter = new EnhancedRateLimiter(_logger);
            var request = new RateLimitRequest { Resource = "" };

            // Act
            var act = async () => await limiter.ExecuteAsync(request, () => Task.FromResult(42));

            // Assert
            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("request");
        }

        #endregion

        #region RateLimitExceededException (Line 116)

        [Fact]
        public async Task ExecuteAsync_RateLimitExceeded_ThrowsRateLimitExceededException_Line116()
        {
            // Arrange - Line 116: throw new RateLimitExceededException(result);
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("strict", new RateLimitPolicy
            {
                MaxRequests = 1,
                Period = TimeSpan.FromMinutes(1),
                EnableResourceLimit = true,
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            var request = new RateLimitRequest { Resource = "strict", Weight = 1 };

            // Consume the only token
            await limiter.ExecuteAsync(request, () => Task.FromResult("first"));

            // Act - this should throw
            var act = async () => await limiter.ExecuteAsync(request, () => Task.FromResult("second"));

            // Assert
            var exception = await act.Should().ThrowAsync<RateLimitExceededException>();
            exception.Which.Result.Should().NotBeNull("because the exception contains the rate limit result");
            exception.Which.Result.IsAllowed.Should().BeFalse("because rate limit was exceeded");
            exception.Which.Message.Should().Contain("Rate limit exceeded", "because the message describes the error");
        }

        #endregion

        #region GetStatistics

        [Fact]
        public void GetStatistics_WithoutResource_ReturnsAllResourceStatistics()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("statsTest", new RateLimitPolicy
            {
                MaxRequests = 100,
                Period = TimeSpan.FromSeconds(60),
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            // Act
            var stats = limiter.GetStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.AllResourceStatistics.Should().NotBeNull("because all resource stats dictionary is populated");
            stats.TotalRequests.Should().Be(0, "because no requests have been made");
            stats.RejectedRequests.Should().Be(0, "because no requests have been rejected");
        }

        [Fact]
        public void GetStatistics_WithResource_ReturnsSpecificResourceStatistics()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("specificResource", new RateLimitPolicy
            {
                MaxRequests = 50,
                Period = TimeSpan.FromSeconds(30)
            });

            // Act
            var stats = limiter.GetStatistics("specificResource");

            // Assert
            stats.Should().NotBeNull();
            stats.ResourceStatistics.Should().NotBeNull("because resource-specific stats are requested");
            stats.ResourceStatistics.Resource.Should().Be("specificResource", "because that's the requested resource");
        }

        [Fact]
        public void GetStatistics_NonExistentResource_ReturnsEmptyStatistics()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);

            // Act
            var stats = limiter.GetStatistics("nonExistent");

            // Assert
            stats.Should().NotBeNull();
            stats.ResourceStatistics.Should().NotBeNull();
            stats.ResourceStatistics.Resource.Should().Be("nonExistent", "because that's the requested resource name");
            stats.ResourceStatistics.AvailableTokens.Should().Be(0, "because no limiter exists for this resource");
        }

        #endregion

        #region Reset

        [Fact]
        public void Reset_WithResource_ResetsSpecificResource()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("resetTest", new RateLimitPolicy
            {
                MaxRequests = 1,
                Period = TimeSpan.FromMinutes(1),
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            // Act
            var act = () => limiter.Reset("resetTest");

            // Assert - should not throw
            act.Should().NotThrow("because resetting a configured resource is valid");
        }

        [Fact]
        public void Reset_WithNonExistentResource_DoesNotThrow()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);

            // Act
            var act = () => limiter.Reset("nonExistent");

            // Assert - should not throw even for non-existent resource
            act.Should().NotThrow("because resetting a non-existent resource is a no-op");
        }

        [Fact]
        public void Reset_All_ResetsEverything()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("toReset", new RateLimitPolicy
            {
                MaxRequests = 10,
                Period = TimeSpan.FromSeconds(30)
            });

            // Act
            var act = () => limiter.Reset();

            // Assert - should not throw
            act.Should().NotThrow("because resetting all limiters is valid");
        }

        #endregion

        #region Dispose

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);

            // Act
            var act = () => limiter.Dispose();

            // Assert
            act.Should().NotThrow("because Dispose should always succeed");
        }

        #endregion

        #region TokenBucket Coverage

        [Fact]
        public void TokenBucket_GetAvailableTokens_ReturnsInitialCapacity()
        {
            // Arrange
            var bucket = new TokenBucket(10, TimeSpan.FromSeconds(1));

            // Act
            var available = bucket.GetAvailableTokens();

            // Assert
            available.Should().Be(10, "because initial capacity is 10");
        }

        [Fact]
        public void TokenBucket_TryConsume_WithCount_ConsumesMultipleTokens()
        {
            // Arrange
            var bucket = new TokenBucket(10, TimeSpan.FromSeconds(1));

            // Act
            var result = bucket.TryConsume(5);
            var remaining = bucket.GetAvailableTokens();

            // Assert
            result.Should().BeTrue("because 5 tokens are available from 10");
            remaining.Should().Be(5, "because 5 tokens were consumed from 10");
        }

        [Fact]
        public void TokenBucket_TryConsume_WithExcessiveCount_ReturnsFalse()
        {
            // Arrange
            var bucket = new TokenBucket(3, TimeSpan.FromSeconds(1));

            // Act
            var result = bucket.TryConsume(5);

            // Assert
            result.Should().BeFalse("because 5 tokens exceeds capacity of 3");
        }

        [Fact]
        public void TokenBucket_GetWaitTime_WhenTokensInsufficient_ReturnsPositiveTimeSpan()
        {
            // Arrange
            var bucket = new TokenBucket(2, TimeSpan.FromMilliseconds(100));
            bucket.TryConsume(2); // Exhaust tokens

            // Act
            var waitTime = bucket.GetWaitTime(1);

            // Assert
            waitTime.Should().BeGreaterThan(TimeSpan.Zero, "because tokens need time to refill");
        }

        [Fact]
        public void TokenBucket_GetWaitTime_WhenTokensSufficient_ReturnsZero()
        {
            // Arrange
            var bucket = new TokenBucket(10, TimeSpan.FromSeconds(1));

            // Act
            var waitTime = bucket.GetWaitTime(5);

            // Assert
            waitTime.Should().Be(TimeSpan.Zero, "because 5 tokens are available from 10");
        }

        [Fact]
        public void TokenBucket_GetNextResetTime_ReturnsFutureTime()
        {
            // Arrange
            var bucket = new TokenBucket(10, TimeSpan.FromSeconds(30));
            var before = DateTime.UtcNow;

            // Act
            var resetTime = bucket.GetNextResetTime();

            // Assert
            resetTime.Should().BeAfter(before, "because reset time is in the future");
        }

        [Fact]
        public void TokenBucket_Reset_RestoresFullCapacity()
        {
            // Arrange
            var bucket = new TokenBucket(10, TimeSpan.FromSeconds(1));
            bucket.TryConsume(5);

            // Act
            bucket.Reset();
            var available = bucket.GetAvailableTokens();

            // Assert
            available.Should().Be(10, "because Reset restores full capacity");
        }

        #endregion

        #region RateLimitResult Factory Methods

        [Fact]
        public void RateLimitResult_Allowed_CreatesAllowedResult()
        {
            // Act
            var result = RateLimitResult.Allowed(50);

            // Assert
            result.IsAllowed.Should().BeTrue("because Allowed creates an allowed result");
            result.RemainingTokens.Should().Be(50, "because that's the specified remaining count");
            result.RetryAfter.Should().BeNull("because allowed results have no retry time");
        }

        [Fact]
        public void RateLimitResult_Denied_CreatesDeniedResult()
        {
            // Act
            var retryAfter = TimeSpan.FromSeconds(30);
            var result = RateLimitResult.Denied("Limit exceeded", retryAfter);

            // Assert
            result.IsAllowed.Should().BeFalse("because Denied creates a denied result");
            result.RemainingTokens.Should().Be(0, "because denied results have no remaining tokens");
            result.Reason.Should().Be("Limit exceeded", "because that's the specified reason");
            result.RetryAfter.Should().Be(retryAfter, "because that's the specified retry time");
        }

        #endregion

        #region RateLimitPolicy Static Properties

        [Fact]
        public void RateLimitPolicy_Default_HasCorrectValues()
        {
            // Act
            var policy = RateLimitPolicy.Default;

            // Assert
            policy.MaxRequests.Should().Be(60, "because Default policy allows 60 requests");
            policy.Period.Should().Be(TimeSpan.FromMinutes(1), "because Default period is 1 minute");
        }

        [Fact]
        public void RateLimitPolicy_LocalAI_HasCorrectValues()
        {
            // Act
            var policy = RateLimitPolicy.LocalAI;

            // Assert
            policy.MaxRequests.Should().Be(100, "because LocalAI allows 100 requests");
            policy.EnableIpLimit.Should().BeFalse("because local AI doesn't need IP limiting");
        }

        [Fact]
        public void RateLimitPolicy_CloudAI_HasCorrectValues()
        {
            // Act
            var policy = RateLimitPolicy.CloudAI;

            // Assert
            policy.MaxRequests.Should().Be(20, "because CloudAI allows 20 requests");
            policy.BurstSize.Should().Be(5, "because CloudAI has burst size of 5");
        }

        [Fact]
        public void RateLimitPolicy_MusicAPI_HasCorrectValues()
        {
            // Act
            var policy = RateLimitPolicy.MusicAPI;

            // Assert
            policy.MaxRequests.Should().Be(60, "because MusicAPI allows 60 requests");
            policy.BurstSize.Should().Be(2, "because MusicAPI has burst size of 2");
        }

        [Fact]
        public void RateLimitPolicy_Admin_HasCorrectValues()
        {
            // Act
            var policy = RateLimitPolicy.Admin;

            // Assert
            policy.MaxRequests.Should().Be(1000, "because Admin allows 1000 requests");
            policy.EnableIpLimit.Should().BeFalse("because admin operations don't need IP limiting");
        }

        #endregion

        #region RateLimitMetrics Coverage

        [Fact]
        public void RateLimitMetrics_RecordSuccess_IncrementsTotalRequests()
        {
            // Arrange
            var metrics = new RateLimitMetrics();

            // Act
            metrics.RecordSuccess("test", TimeSpan.FromMilliseconds(50));
            metrics.RecordSuccess("test", TimeSpan.FromMilliseconds(100));

            // Assert
            metrics.TotalRequests.Should().Be(2, "because two successful requests were recorded");
        }

        [Fact]
        public void RateLimitMetrics_RecordFailure_IncrementsTotalRequests()
        {
            // Arrange
            var metrics = new RateLimitMetrics();

            // Act
            metrics.RecordFailure("test");

            // Assert
            metrics.TotalRequests.Should().Be(1, "because one failed request was recorded");
        }

        [Fact]
        public void RateLimitMetrics_RecordRejection_IncrementsRejectedRequests()
        {
            // Arrange
            var metrics = new RateLimitMetrics();

            // Act
            metrics.RecordRejection("test", "user1", "127.0.0.1");

            // Assert
            metrics.RejectedRequests.Should().Be(1, "because one rejection was recorded");
        }

        [Fact]
        public void RateLimitMetrics_RecordRejection_WithUserId_TracksRejectedUsers()
        {
            // Arrange
            var metrics = new RateLimitMetrics();

            // Act
            metrics.RecordRejection("test", "user1", null);
            metrics.RecordRejection("test", "user1", null);
            metrics.RecordRejection("test", "user2", null);

            var topUsers = metrics.GetTopRejectedUsers(10);

            // Assert
            topUsers.Should().HaveCount(2, "because two distinct users were rejected");
            topUsers[0].Key.Should().Be("user1", "because user1 has most rejections");
            topUsers[0].Value.Should().Be(2, "because user1 was rejected twice");
        }

        [Fact]
        public void RateLimitMetrics_RecordRejection_WithIpAddress_TracksRejectedIps()
        {
            // Arrange
            var metrics = new RateLimitMetrics();

            // Act
            metrics.RecordRejection("test", null, "192.168.1.1");
            metrics.RecordRejection("test", null, "192.168.1.2");
            metrics.RecordRejection("test", null, "192.168.1.1");

            var topIps = metrics.GetTopRejectedIps(10);

            // Assert
            topIps.Should().HaveCount(2, "because two distinct IPs were rejected");
            topIps[0].Key.Should().Be("192.168.1.1", "because 192.168.1.1 has most rejections");
            topIps[0].Value.Should().Be(2, "because 192.168.1.1 was rejected twice");
        }

        [Fact]
        public void RateLimitMetrics_GetAverageResponseTime_ReturnsCorrectAverage()
        {
            // Arrange
            var metrics = new RateLimitMetrics();
            metrics.RecordSuccess("test", TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess("test", TimeSpan.FromMilliseconds(200));
            metrics.RecordSuccess("test", TimeSpan.FromMilliseconds(300));

            // Act
            var avg = metrics.GetAverageResponseTime();

            // Assert
            avg.Should().Be(200.0, "because average of 100, 200, 300 is 200");
        }

        [Fact]
        public void RateLimitMetrics_GetAverageResponseTime_WhenEmpty_ReturnsZero()
        {
            // Arrange
            var metrics = new RateLimitMetrics();

            // Act
            var avg = metrics.GetAverageResponseTime();

            // Assert
            avg.Should().Be(0.0, "because no requests have been recorded");
        }

        [Fact]
        public void RateLimitMetrics_Reset_ClearsAllCounters()
        {
            // Arrange
            var metrics = new RateLimitMetrics();
            metrics.RecordSuccess("test", TimeSpan.FromMilliseconds(100));
            metrics.RecordRejection("test", "user1", "127.0.0.1");

            // Act
            metrics.Reset();

            // Assert
            metrics.TotalRequests.Should().Be(0, "because Reset clears total requests");
            metrics.RejectedRequests.Should().Be(0, "because Reset clears rejected requests");
            metrics.GetTopRejectedUsers(10).Should().BeEmpty("because Reset clears rejected users");
            metrics.GetTopRejectedIps(10).Should().BeEmpty("because Reset clears rejected IPs");
        }

        #endregion

        #region ExecuteAsync Success/Failure Paths

        [Fact]
        public async Task ExecuteAsync_Success_RecordsMetrics()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("successTest", new RateLimitPolicy
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            var request = new RateLimitRequest { Resource = "successTest" };

            // Act
            var result = await limiter.ExecuteAsync(request, () => Task.FromResult("success"));

            // Assert
            result.Should().Be("success", "because the action returns 'success'");

            var stats = limiter.GetStatistics();
            stats.TotalRequests.Should().Be(1, "because one successful request was executed");
        }

        [Fact]
        public async Task ExecuteAsync_ActionThrows_RecordsFailureAndRethrows()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("failureTest", new RateLimitPolicy
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            var request = new RateLimitRequest { Resource = "failureTest" };

            // Act
            var act = async () => await limiter.ExecuteAsync(request, () => Task.FromException<string>(new InvalidOperationException("Action failed")));

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Action failed", "because the original exception is rethrown");
        }

        #endregion

        #region CheckRateLimitAsync with User/IP Limits

        [Fact]
        public async Task CheckRateLimitAsync_WithUserLimit_ChecksUserLimit()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("userTest", new RateLimitPolicy
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                EnableResourceLimit = false,
                EnableUserLimit = true,
                EnableIpLimit = false
            });

            var request = new RateLimitRequest { Resource = "userTest", UserId = "testUser" };

            // Act
            var result = await limiter.CheckRateLimitAsync(request);

            // Assert
            result.IsAllowed.Should().BeTrue("because user limit is not exceeded");
        }

        [Fact]
        public async Task CheckRateLimitAsync_WithIpLimit_ChecksIpLimit()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("ipTest", new RateLimitPolicy
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                EnableResourceLimit = false,
                EnableUserLimit = false,
                EnableIpLimit = true
            });

            var request = new RateLimitRequest { Resource = "ipTest", IpAddress = IPAddress.Loopback };

            // Act
            var result = await limiter.CheckRateLimitAsync(request);

            // Assert
            result.IsAllowed.Should().BeTrue("because IP limit is not exceeded");
        }

        [Fact]
        public async Task CheckRateLimitAsync_AllLimitsDisabled_ReturnsAllowed()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("noLimits", new RateLimitPolicy
            {
                MaxRequests = 10,
                Period = TimeSpan.FromMinutes(1),
                EnableResourceLimit = false,
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            var request = new RateLimitRequest { Resource = "noLimits", UserId = "user1", IpAddress = IPAddress.Loopback };

            // Act
            var result = await limiter.CheckRateLimitAsync(request);

            // Assert
            result.IsAllowed.Should().BeTrue("because all limits are disabled");
        }

        #endregion

        #region Request Weight

        [Fact]
        public async Task CheckRateLimitAsync_WithWeight_ConsumesMultipleTokens()
        {
            // Arrange
            var limiter = new EnhancedRateLimiter(_logger);
            limiter.ConfigureLimit("weightTest", new RateLimitPolicy
            {
                MaxRequests = 5,
                Period = TimeSpan.FromMinutes(1),
                EnableUserLimit = false,
                EnableIpLimit = false
            });

            var request = new RateLimitRequest { Resource = "weightTest", Weight = 3 };

            // Act
            var result = await limiter.CheckRateLimitAsync(request);

            // Assert
            result.IsAllowed.Should().BeTrue("because 3 tokens are available from 5");
            result.RemainingTokens.Should().Be(2, "because 5 - 3 = 2 remaining");
        }

        #endregion
    }
}
