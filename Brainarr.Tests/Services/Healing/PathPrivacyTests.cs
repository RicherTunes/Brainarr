using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class PathPrivacyTests
{
    [Fact]
    public void Redact_ShouldReturnBasenameAndStableShortHash()
    {
        var first = PathPrivacy.Redact(@"D:\Music\Artist\Album\track01.flac");
        var second = PathPrivacy.Redact(@"D:\Music\Artist\Album\track01.flac");

        first.Should().StartWith("track01.flac#");
        first.Should().HaveLength("track01.flac#".Length + 12);
        second.Should().Be(first);
        first.Should().NotContain("Artist");
        first.Should().NotContain("Album");
    }

    [Fact]
    public void Redact_ShouldTreatSlashAndBackslashAsPathSeparators()
    {
        PathPrivacy.Redact(@"D:\Music\Artist\Album\track01.flac")
            .Should()
            .StartWith("track01.flac#");
        PathPrivacy.Redact("/mnt/music/Artist/Album/track02.flac")
            .Should()
            .StartWith("track02.flac#");
    }

    [Fact]
    public void Redact_ShouldHandleBlankPathWithoutThrowing()
    {
        PathPrivacy.Redact(null).Should().Be("<missing>#000000000000");
        PathPrivacy.Redact("").Should().Be("<missing>#000000000000");
        PathPrivacy.Redact("   ").Should().Be("<missing>#000000000000");
    }

    [Fact]
    public void HashPath_ShouldPreserveCaseSensitivity()
    {
        PathPrivacy.HashPath(@"D:\Music\A.FLAC")
            .Should()
            .NotBe(PathPrivacy.HashPath(@"d:\music\a.flac"));
    }

    [Fact]
    public void RedactMessage_ShouldRemoveKnownAndLikelyPaths()
    {
        var message = @"Cannot read D:\Music\Private Artist\Album\track01.m4a from /mnt/music/Private Artist/file.flac";

        var redacted = PathPrivacy.RedactMessage(message, @"D:\Music\Private Artist\Album\track01.m4a");

        redacted.Should().NotContain("Private Artist");
        redacted.Should().NotContain("/mnt/music");
        redacted.Should().Contain("track01.m4a#");
        redacted.Should().Contain("<path>");
    }
}
