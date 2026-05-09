using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.TechDebt;
using Xunit;

namespace Brainarr.Tests.TechDebt
{
    [Trait("Category", "TechDebt")]
    public class TechDebtRemediationServiceTests
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private TechDebtRemediationService Sut() => new TechDebtRemediationService(_logger);

        // ── SafeExecuteSync ─────────────────────────────────────────

        [Fact]
        public void SafeExecuteSync_Generic_ReturnsResult()
        {
            var result = Sut().SafeExecuteSync(() => Task.FromResult(42));
            result.Should().Be(42);
        }

        [Fact]
        public void SafeExecuteSync_Void_RunsTask()
        {
            var ran = false;
            Sut().SafeExecuteSync(() => { ran = true; return Task.CompletedTask; });
            ran.Should().BeTrue();
        }

        // ── ExecuteWithStandardErrorHandling ────────────────────────

        [Fact]
        public async Task ExecuteWithStandardErrorHandling_HappyPath_ReturnsResult()
        {
            var result = await Sut().ExecuteWithStandardErrorHandling(() => Task.FromResult("ok"), "op", "fallback");
            result.Should().Be("ok");
        }

        [Fact]
        public async Task ExecuteWithStandardErrorHandling_TaskCanceled_ReturnsDefault()
        {
            Func<Task<int>> op = () => throw new TaskCanceledException();
            var result = await Sut().ExecuteWithStandardErrorHandling(op, "op", -1);
            result.Should().Be(-1);
        }

        [Fact]
        public async Task ExecuteWithStandardErrorHandling_Timeout_ReturnsDefault()
        {
            Func<Task<int>> op = () => throw new TimeoutException();
            var result = await Sut().ExecuteWithStandardErrorHandling(op, "op", 99);
            result.Should().Be(99);
        }

        [Fact]
        public async Task ExecuteWithStandardErrorHandling_Generic_ReturnsDefault()
        {
            Func<Task<int>> op = () => throw new InvalidOperationException("boom");
            var result = await Sut().ExecuteWithStandardErrorHandling(op, "op", 7);
            result.Should().Be(7);
        }

        // ── StandardizeResponseParsing ──────────────────────────────

        [Fact]
        public void StandardizeResponseParsing_NullOrEmpty_ReturnsEmptyList()
        {
            var sut = Sut();
            sut.StandardizeResponseParsing(null!, AIProvider.OpenAI).Should().BeEmpty();
            sut.StandardizeResponseParsing("", AIProvider.OpenAI).Should().BeEmpty();
            sut.StandardizeResponseParsing("   ", AIProvider.OpenAI).Should().BeEmpty();
        }

        [Fact]
        public void StandardizeResponseParsing_JsonArray_Parses()
        {
            var json = """[{"Artist":"  Foo  ","Album":"**Bar**","Year":2020,"Confidence":0.0}]""";
            var result = Sut().StandardizeResponseParsing(json, AIProvider.OpenAI);
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Foo");
            result[0].Album.Should().Be("Bar");
            result[0].Year.Should().Be(2020);
            // Default confidence applied when input was 0
            result[0].Confidence.Should().Be(0.5);
            result[0].Provider.Should().Be("OpenAI");
            result[0].Source.Should().Be("OpenAI");
        }

        [Fact]
        public void StandardizeResponseParsing_JsonArray_WithMarkdownFence_Parses()
        {
            var json = "```json\n[{\"Artist\":\"X\",\"Album\":\"Y\",\"Confidence\":0.8}]\n```";
            var result = Sut().StandardizeResponseParsing(json, AIProvider.Anthropic);
            result.Should().HaveCount(1);
            result[0].Confidence.Should().Be(0.8);
        }

        [Fact]
        public void StandardizeResponseParsing_JsonObject_ParsesAsSingle()
        {
            var json = """{"Artist":"Solo","Album":"Album","Confidence":0.7}""";
            var result = Sut().StandardizeResponseParsing(json, AIProvider.Gemini);
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Solo");
        }

        [Fact]
        public void StandardizeResponseParsing_TextFallback_ParsesArtistAlbumYear()
        {
            // Malformed JSON array triggers text-parse fallback (JSON throws inside try).
            var text = "[ this is not valid json\n1. Foo - Bar (2021)\n2. Baz - Qux";
            var result = Sut().StandardizeResponseParsing(text, AIProvider.Ollama);
            // Text parser regex is greedy but liberal; verify it finds the artist/album/year
            // patterns in the lines that match.
            result.Should().NotBeEmpty();
            result.Should().Contain(r => r.Artist == "Foo" && r.Album == "Bar (2021)" || r.Artist == "Foo" && r.Album == "Bar" && r.Year == 2021);
        }

        [Fact]
        public void StandardizeResponseParsing_NonJsonNonMatchingPrefix_ReturnsEmpty()
        {
            // Input neither starts with [ nor { — JSON path returns empty without throwing,
            // and text fallback is not triggered.
            var result = Sut().StandardizeResponseParsing("plain text no json", AIProvider.Ollama);
            result.Should().BeEmpty();
        }

        [Fact]
        public void StandardizeResponseParsing_NormalizesWhitespaceAndArtifacts()
        {
            var json = """[{"Artist":"  A    B  ","Album":"**Alb**um","Genre":"  Pop  *Rock*  ","Confidence":0.9}]""";
            var result = Sut().StandardizeResponseParsing(json, AIProvider.OpenAI);
            result[0].Artist.Should().Be("A B");
            result[0].Album.Should().Be("Album");
            result[0].Genre.Should().Be("Pop Rock");
        }

        // ── TechDebtExtensions ──────────────────────────────────────

        [Fact]
        public void SafeGetResult_RoundTripsValue()
        {
            var task = Task.FromResult(123);
            task.SafeGetResult().Should().Be(123);
        }

        [Fact]
        public void SafeWait_CompletesWithoutThrow()
        {
            var act = () => Task.CompletedTask.SafeWait();
            act.Should().NotThrow();
        }

        [Fact]
        public async Task WithStandardErrorHandling_ReturnsDefaultOnThrow()
        {
            var task = Task.Run(new Func<int>(() => throw new InvalidOperationException("x")));
            var result = await task.WithStandardErrorHandling("op", -5);
            result.Should().Be(-5);
        }
    }
}
