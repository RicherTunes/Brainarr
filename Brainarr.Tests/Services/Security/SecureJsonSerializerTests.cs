using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Security;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Services.Security
{
    public class SecureJsonSerializerTests
    {
        private class Poco
        {
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        [Fact]
        public void Serialize_And_Deserialize_Roundtrip_Works()
        {
            var obj = new Poco { Name = "Test", Value = 7 };

            var json = SecureJsonSerializer.Serialize(obj);
            json.Should().NotBeNullOrEmpty();
            json.Should().Contain("\"name\":"); // camelCase

            var back = SecureJsonSerializer.Deserialize<Poco>(json);
            back.Should().NotBeNull();
            back!.Name.Should().Be("Test");
            back.Value.Should().Be(7);
        }

        [Fact]
        public void Serialize_Null_ReturnsLiteralNull()
        {
            var json = SecureJsonSerializer.Serialize<Poco>(null);
            json.Should().Be("null");
        }

        [Fact]
        public void TryDeserialize_Failure_ReturnsFalse_AndError()
        {
            var malicious = "{\"script\":\"eval(1)\"}"; // blocked by validation
            var ok = SecureJsonSerializer.TryDeserialize<Poco>(malicious, out var result, out var error);
            ok.Should().BeFalse();
            result.Should().BeNull();
            error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void ParseDocument_Throws_OnSuspiciousContent()
        {
            var json = "{\"onload\":\"alert(1)\"}";
            Action act = () => SecureJsonSerializer.ParseDocument(json);
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void ParseDocumentRelaxed_Allows_ScriptMarkers()
        {
            var json = "{\"content\":\"<script>alert(1)</script>\"}";
            using var doc = SecureJsonSerializer.ParseDocumentRelaxed(json);
            doc.RootElement.GetProperty("content").GetString().Should().Contain("<script>");
        }

        [Fact]
        public async Task Async_Serialize_And_Deserialize_Stream_Works()
        {
            var obj = new Poco { Name = "Streamy", Value = 101 };
            using var ms = new MemoryStream();

            await SecureJsonSerializer.SerializeAsync(ms, obj);
            ms.Position = 0;
            var back = await SecureJsonSerializer.DeserializeAsync<Poco>(ms);

            back.Should().NotBeNull();
            back!.Name.Should().Be("Streamy");
            back.Value.Should().Be(101);
        }

        [Fact]
        public void CreateOptions_RespectsParameters()
        {
            var opts = SecureJsonSerializer.CreateOptions(maxDepth: 3, caseInsensitive: false, writeIndented: true);
            opts.MaxDepth.Should().Be(3);
            opts.PropertyNameCaseInsensitive.Should().BeFalse();
            opts.WriteIndented.Should().BeTrue();
            opts.AllowTrailingCommas.Should().BeFalse();
        }

        [Fact]
        public async Task SerializeAsync_Null_WritesNullLiteral()
        {
            using var ms = new MemoryStream();
            await SecureJsonSerializer.SerializeAsync<Poco>(ms, null);
            var text = Encoding.UTF8.GetString(ms.ToArray());
            text.Should().Be("null");
        }
    }
}
