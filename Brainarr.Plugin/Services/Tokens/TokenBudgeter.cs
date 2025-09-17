using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Tokens
{
    internal sealed class TokenBudgeter
    {
        private readonly ITokenCounter _counter;

        public TokenBudgeter(ITokenCounter counter)
        {
            _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        }

        internal sealed class Plan
        {
            public int AllowedInputTokens { get; init; }
            public int ReservedOutputTokens { get; init; }
            public int SamplingItems { get; init; }
            public int BatchSize { get; init; }
            public int Batches { get; init; }
            public string Rationale { get; init; } = string.Empty;
        }

        public async Task<Plan> BuildAsync(
            ModelRegistry.ProviderDescriptor provider,
            ModelRegistry.ModelDescriptor model,
            string? systemPrompt,
            string? toolJsonSchema,
            string? samplingPreview,
            int targetRecommendations,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var contextTokens = model.ContextTokens <= 0 ? 4096 : model.ContextTokens;
            contextTokens = Math.Max(1024, contextTokens);

            var reservedOutput = Math.Max(512, (int)Math.Round(contextTokens * 0.25, MidpointRounding.AwayFromZero));
            if (reservedOutput >= contextTokens)
            {
                reservedOutput = Math.Max(256, contextTokens / 2);
            }

            var allowedInput = Math.Max(256, contextTokens - reservedOutput);
            var fixedText = BuildFixedPrompt(systemPrompt, toolJsonSchema);
            var providerSlug = provider.Slug ?? string.Empty;
            var fixedTokens = await _counter.CountAsync(providerSlug, model.Id, fixedText, cancellationToken).ConfigureAwait(false);
            var remainingForSampling = Math.Max(0, allowedInput - fixedTokens);

            var samplingItems = 0;
            var perItemTokens = 0;

            var segments = SplitSamplingItems(samplingPreview);
            if (segments.Length > 0 && remainingForSampling > 0)
            {
                var sampleCount = Math.Min(segments.Length, 10);
                var sampleBuilder = new StringBuilder();
                for (var i = 0; i < sampleCount; i++)
                {
                    if (i > 0)
                    {
                        sampleBuilder.Append("\n\n");
                    }

                    sampleBuilder.Append(segments[i]);
                }

                var sampleTokens = await _counter.CountAsync(providerSlug, model.Id, sampleBuilder.ToString(), cancellationToken).ConfigureAwait(false);
                perItemTokens = Math.Max(1, sampleTokens / sampleCount);

                var estimatedItems = perItemTokens > 0 ? remainingForSampling / perItemTokens : remainingForSampling;
                estimatedItems = Math.Max(1, estimatedItems);
                var maxSampling = Math.Min(segments.Length, 400);
                var minSampling = Math.Min(segments.Length, 5);
                samplingItems = Math.Max(minSampling, Math.Min(maxSampling, estimatedItems));
            }

            var desiredRecommendations = Math.Max(1, targetRecommendations);
            var batchSize = desiredRecommendations;

            if (perItemTokens > 0)
            {
                var estimatedPrompt = fixedTokens + (samplingItems * perItemTokens);
                if (estimatedPrompt > allowedInput)
                {
                    var safe = Math.Max(1, allowedInput / Math.Max(1, perItemTokens * 2));
                    batchSize = Math.Min(desiredRecommendations, safe);
                }
            }

            batchSize = Math.Clamp(batchSize, 1, desiredRecommendations);
            var batches = (int)Math.Ceiling(desiredRecommendations / (double)batchSize);

            return new Plan
            {
                AllowedInputTokens = allowedInput,
                ReservedOutputTokens = reservedOutput,
                SamplingItems = samplingItems,
                BatchSize = batchSize,
                Batches = Math.Max(1, batches),
                Rationale = $"context={contextTokens}, fixed={fixedTokens}, perItem={perItemTokens}, remaining={remainingForSampling}"
            };
        }

        private static string BuildFixedPrompt(string? systemPrompt, string? toolJsonSchema)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                builder.Append(systemPrompt);
            }

            if (!string.IsNullOrWhiteSpace(toolJsonSchema))
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(toolJsonSchema);
            }

            return builder.ToString();
        }

        private static string[] SplitSamplingItems(string? samplingPreview)
        {
            if (string.IsNullOrWhiteSpace(samplingPreview))
            {
                return Array.Empty<string>();
            }

            return samplingPreview.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
