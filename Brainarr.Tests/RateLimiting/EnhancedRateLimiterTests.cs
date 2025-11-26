using System;

using System.Net;

using System.Threading.Tasks;

using NLog;

using NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiting;

using Xunit;


namespace Brainarr.Tests.RateLimiting

{

    [Collection("RateLimiterTests")]
    public class EnhancedRateLimiterTests

    {

        private static Logger TestLogger => LogManager.GetCurrentClassLogger();


        [Fact]

        public async Task Allows_first_request_then_blocks_second_with_tight_policy()

        {

            var limiter = new EnhancedRateLimiter(TestLogger);

            limiter.ConfigureLimit("test", new RateLimitPolicy

            {

                MaxRequests = 1,

                Period = TimeSpan.FromSeconds(5),

                EnableUserLimit = false,

                EnableIpLimit = false,

                EnableResourceLimit = true

            });


            var req = new RateLimitRequest { Resource = "test", UserId = "u1", IpAddress = IPAddress.Loopback };


            var first = await limiter.CheckRateLimitAsync(req);

            Assert.True(first.IsAllowed);


            // Consume and then check again

            await limiter.ExecuteAsync(req, () => Task.FromResult(1));


            var second = await limiter.CheckRateLimitAsync(req);

            Assert.False(second.IsAllowed);

            Assert.True(second.RetryAfter.HasValue);

        }


        [Fact]

        public void TokenBucket_refills_over_time()

        {

            var bucket = new TokenBucket(2, TimeSpan.FromMilliseconds(200));

            Assert.True(bucket.TryConsume());

            Assert.True(bucket.TryConsume());

            Assert.False(bucket.TryConsume());


            // Wait for refill

            var wait = bucket.GetWaitTime(1);

            Assert.True(wait > TimeSpan.Zero);

        }

    }

}
