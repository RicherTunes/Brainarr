using System;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace NzbDrone.Core.ImportLists.Brainarr.Tests.Services.Support
{
    /// <summary>
    /// R2-08: credential JSON carries an untrusted expiresAt/expires_at epoch. A malformed/overflowing value
    /// (e.g. long.MaxValue) must not throw out of the credential-load path — EpochExpiry returns null instead.
    /// </summary>
    [Trait("Category", "Unit")]
    public class EpochExpiryTests
    {
        private static JsonElement Element(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        [Fact]
        public void FromMilliseconds_Valid_ReturnsInstant()
        {
            EpochExpiry.FromMilliseconds(Element("1700000000000")).Should().Be(
                DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
        }

        [Fact]
        public void FromSeconds_Valid_ReturnsInstant()
        {
            EpochExpiry.FromSeconds(Element("1700000000")).Should().Be(
                DateTimeOffset.FromUnixTimeSeconds(1700000000));
        }

        [Theory]
        [InlineData("9223372036854775807")]  // long.MaxValue
        [InlineData("-9223372036854775808")] // long.MinValue
        public void FromMilliseconds_Overflow_ReturnsNull_DoesNotThrow(string json)
        {
            EpochExpiry.FromMilliseconds(Element(json)).Should().BeNull();
        }

        [Theory]
        [InlineData("9223372036854775807")]
        [InlineData("-9223372036854775808")]
        public void FromSeconds_Overflow_ReturnsNull_DoesNotThrow(string json)
        {
            EpochExpiry.FromSeconds(Element(json)).Should().BeNull();
        }

        [Theory]
        [InlineData("\"not a number\"")]
        [InlineData("true")]
        [InlineData("null")]
        public void NonNumeric_ReturnsNull(string json)
        {
            EpochExpiry.FromMilliseconds(Element(json)).Should().BeNull();
            EpochExpiry.FromSeconds(Element(json)).Should().BeNull();
        }
    }
}
