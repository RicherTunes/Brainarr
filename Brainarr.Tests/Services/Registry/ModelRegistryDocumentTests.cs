using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Xunit;

namespace Brainarr.Tests.Services.Registry
{
    public class ModelRegistryDocumentTests
    {
        [Fact]
        public void ProvidersFlexibleConverter_should_accept_dictionary_shape()
        {
            const string json = @"{
  ""version"": ""1"",
  ""providers"": {
    ""openai"": {
      ""name"": ""OpenAI"",
      ""slug"": ""openai"",
      ""endpoint"": ""https://api.example.com"",
      ""auth"": { ""type"": ""bearer"", ""env"": ""OPENAI_KEY"" },
      ""models"": [
        { ""id"": ""gpt-test"", ""context_tokens"": 4096 }
      ]
    }
  }
}";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document.Should().NotBeNull();
            document!.Providers.Should().ContainKey("openai");
            var provider = document.Providers["openai"];
            provider.Models.Should().ContainSingle(m => m.Id == "gpt-test");
            provider.Models[0].ContextTokens.Should().Be(4096);
        }

        [Fact]
        public void ProvidersFlexibleConverter_should_merge_duplicate_providers()
        {
            const string json = @"{
  ""providers"": [
    {
      ""name"": ""OpenAI"",
      ""slug"": ""openai"",
      ""models"": [ { ""id"": ""gpt-a"", ""context_tokens"": 4000 } ]
    },
    {
      ""name"": ""OpenAI Alt"",
      ""slug"": ""openai"",
      ""models"": [ { ""id"": ""gpt-b"", ""context_tokens"": 8000 } ]
    }
  ]
}";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document.Should().NotBeNull();
            document!.Providers.Should().ContainKey("openai");
            var provider = document.Providers["openai"];
            provider.Models.Should().HaveCount(2);
            provider.Models.Select(m => m.Id).Should().BeEquivalentTo(new[] { "gpt-a", "gpt-b" });
        }
    }
}
