using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Brainarr.Providers.OpenAI.Tests.Contract;

public sealed class OpenAiChatFixtureContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void MinimalChatFixture_IsCopiedAndKeepsExpectedResponseShape()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Contract",
            "TestAssets",
            "openai.chat.min.json");

        Assert.True(File.Exists(fixturePath), "OpenAI contract fixture must be copied to the test output directory.");

        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;

        Assert.Equal("chat.completion", root.GetProperty("object").GetString());
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());

        var choice = Assert.Single(root.GetProperty("choices").EnumerateArray());
        Assert.Equal("assistant", choice.GetProperty("message").GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(choice.GetProperty("message").GetProperty("content").GetString()));

        var usage = root.GetProperty("usage");
        Assert.True(usage.GetProperty("prompt_tokens").GetInt32() > 0);
        Assert.True(usage.GetProperty("completion_tokens").GetInt32() > 0);
        Assert.Equal(
            usage.GetProperty("prompt_tokens").GetInt32() + usage.GetProperty("completion_tokens").GetInt32(),
            usage.GetProperty("total_tokens").GetInt32());
    }
}
