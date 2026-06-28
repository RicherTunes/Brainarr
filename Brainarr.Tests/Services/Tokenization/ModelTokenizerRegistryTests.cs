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
    [Collection("TokenizerFallbackGate")]
    public class ModelTokenizerRegistryTests
    {
        public ModelTokenizerRegistryTests()
        {
            // The tokenizer-fallback warn gate is process-wide static (F1 fix). Reset it before each
            // test so warn/metric-count assertions are deterministic regardless of suite ordering.
            // The fallback keys used here ("openai:gpt-test", "f1procwide:*", empty/"<default>") are
            // exclusive to this file, so no cross-collection test can race the gate during these.
            ModelTokenizerRegistry.ResetFallbackWarnStateForTests();
        }

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
        public void FallbackWarn_FiresOnce_AcrossRegistryInstances()
        {
            // Real-usage finding (F1): the tokenizer-fallback WARN ("no tokenizer registered for
            // zaicoding:glm-4.5-air") re-fired on every run. Lidarr re-instantiates BrainarrImportList
            // — and thus a fresh DI ServiceProvider + a fresh ModelTokenizerRegistry — per operation,
            // so an instance-scoped WarnOnce gate reset every run. The gate must be process-wide
            // (WarnOnce's documented private-static usage) so the WARN fires once and subsequent
            // fallbacks (this run or a later one) drop to Debug.
            var target = new MemoryTarget { Layout = "${level}|${message}" };
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Warn, LogLevel.Warn, target, "tokenizer-procwide");
            using var factory = new LogFactory(config);
            var logger = factory.GetLogger("tokenizer-procwide");

            const string key = "f1procwide:glm-4.5-air"; // unique to this test — no cross-test collision

            // Two separate registries (simulating two per-run instantiations) both fall back for the
            // same model key. The WARN must fire exactly ONCE across both instances, not once per instance.
            new ModelTokenizerRegistry(logger: logger).Get(key);
            new ModelTokenizerRegistry(logger: logger).Get(key);

            Assert.Single(target.Logs); // exactly one WARN across both instances (xUnit2013)
            Assert.Contains(target.Logs, log => log.Contains(key));
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
