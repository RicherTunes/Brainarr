using System.Collections.Generic;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Tokenization
{
    public class ModelTokenizerRegistryTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Tokenization")]
        public void Logs_Warning_When_Falling_Back_To_Default_Tokenizer()
        {
            var target = new MemoryTarget { Layout = "${level}|${message}" };
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Warn, LogLevel.Warn, target, "tokenizer-tests");
            using var factory = new LogFactory(config);
            var logger = factory.GetLogger("tokenizer-tests");

            var registry = new ModelTokenizerRegistry(logger: logger);

            registry.Get("openai:gpt-test");
            registry.Get("openai:gpt-test");
            registry.Get(null);

            Assert.Equal(2, target.Logs.Count);
            Assert.Contains(target.Logs, log => log.Contains("openai:gpt-test"));
            Assert.Contains(target.Logs, log => log.Contains("<default>"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Tokenization")]
        public void Records_Metric_When_Falling_Back()
        {
            var metrics = new RecordingMetrics();
            var logger = LogManager.GetLogger("tokenizer-metrics-tests");
            var registry = new ModelTokenizerRegistry(logger: logger, metrics: metrics);

            registry.Get("openai:gpt-test");
            registry.Get("openai:gpt-test");
            registry.Get(null);

            Assert.Equal(2, metrics.Records.Count);
            Assert.Contains(metrics.Records, r => r.Name == MetricsNames.TokenizerFallback && r.Tags["model"] == "openai:gpt-test" && r.Tags["reason"] == "default-fallback");
            Assert.Contains(metrics.Records, r => r.Tags["model"] == "<default>" && r.Tags["reason"] == "empty-model-key");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Tokenization")]
        public void Uses_Override_When_Provided()
        {
            var overrides = new Dictionary<string, ITokenizer>
            {
                ["openai"] = new StubTokenizer(99)
            };

            var registry = new ModelTokenizerRegistry(overrides);

            var tokenizer = registry.Get("openai:gpt-test");

            Assert.Equal(99, tokenizer.CountTokens("ignored"));
        }

        private sealed class RecordingMetrics : IMetrics
        {
            public List<(string Name, double Value, IReadOnlyDictionary<string, string> Tags)> Records { get; } = new();

            public void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
            {
                Records.Add((name, value, tags ?? new Dictionary<string, string>()));
            }
        }

        private sealed class StubTokenizer : ITokenizer
        {
            private readonly int _value;

            public StubTokenizer(int value)
            {
                _value = value;
            }

            public int CountTokens(string text)
            {
                return _value;
            }
        }
    }
}
