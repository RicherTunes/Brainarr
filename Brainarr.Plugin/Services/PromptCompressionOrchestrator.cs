using System;
using System.Collections.Generic;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;

namespace NzbDrone.Core.ImportLists.Brainarr.Services;

/// <summary>
/// Runs the prompt compression loop, drift detection, headroom guard, and cache invalidation.
/// Extracted from <see cref="LibraryAwarePromptBuilder"/> to isolate compression orchestration.
/// </summary>
internal sealed class PromptCompressionOrchestrator
{
    private readonly IPromptRenderer _renderer;
    private readonly IPlanCache _planCache;
    private readonly IMetrics _metrics;
    private readonly Logger _logger;

    private const double MaxDriftInvalidationRatio = 1.30;

    public PromptCompressionOrchestrator(
        IPromptRenderer renderer,
        IPlanCache planCache,
        IMetrics metrics,
        Logger logger)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _planCache = planCache ?? throw new ArgumentNullException(nameof(planCache));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public sealed record CompressionResult(
        string Prompt,
        PromptPlan FinalPlan,
        int BaselineTokens,
        int ReportedTokens,
        bool GuardTriggered,
        string? FallbackReason);

    public CompressionResult CompressAndValidate(
        PromptPlan plan,
        string initialPrompt,
        ITokenizer tokenizer,
        ModelPromptTemplate template,
        TokenBudgetResolver.PromptBudget budget,
        int clampedTargetTokens,
        IReadOnlyDictionary<string, string> metricTags,
        CancellationToken cancellationToken)
    {
        var baselineTokens = tokenizer.CountTokens(initialPrompt);
        var estimated = baselineTokens;
        var cacheInvalidated = false;
        var guardTriggered = false;
        string? fallbackReason = null;
        var prompt = initialPrompt;

        plan = plan with
        {
            EstimatedTokensPreCompression = baselineTokens,
            ContextWindow = budget.ContextTokens,
            HeadroomTokens = budget.HeadroomTokens
        };

        void AddFallbackTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            if (string.IsNullOrEmpty(fallbackReason))
            {
                fallbackReason = tag;
                return;
            }

            if (!fallbackReason.Contains(tag, StringComparison.Ordinal))
            {
                fallbackReason = $"{fallbackReason}|{tag}";
            }
        }

        void InvalidatePlanCache()
        {
            if (_planCache == null || cacheInvalidated)
            {
                return;
            }

            _planCache.InvalidateByFingerprint(plan.LibraryFingerprint);
            _planCache.TryRemove(plan.PlanCacheKey);
            cacheInvalidated = true;
        }

        void MarkHeadroomGuard()
        {
            guardTriggered = true;
            plan.Compression.MarkTrimmed();
            InvalidatePlanCache();
            AddFallbackTag("prompt_trimmed");
            AddFallbackTag("headroom_guard");
            _metrics.Record(MetricsNames.PromptHeadroomViolation, 1, metricTags);
        }

        // Compression loop: iteratively reduce prompt until within budget
        while (estimated > clampedTargetTokens && plan.Compression.TryCompress(plan.Sample))
        {
            cancellationToken.ThrowIfCancellationRequested();
            prompt = _renderer.Render(plan with { FromCache = false }, template, cancellationToken);
            estimated = tokenizer.CountTokens(prompt);
        }

        if (estimated > clampedTargetTokens)
        {
            AddFallbackTag("prompt_trimmed");
            plan.Compression.MarkTrimmed();
            InvalidatePlanCache();
        }

        var compressionRatio = baselineTokens > 0 ? (double)estimated / baselineTokens : (double?)null;
        var driftRatio = compressionRatio ?? 1.0;

        if (driftRatio > MaxDriftInvalidationRatio)
        {
            AddFallbackTag("token_drift");
            plan.Compression.MarkTrimmed();

            if (_logger.IsWarnEnabled)
            {
                _logger.Warn(
                    "prompt_plan drift_exceeded cache_key={PlanCacheKey} fingerprint={Fingerprint} ratio={DriftRatio:F3} pre={Pre} post={Post}",
                    plan.PlanCacheKey,
                    plan.LibraryFingerprint,
                    driftRatio,
                    baselineTokens,
                    estimated);
            }

            InvalidatePlanCache();
        }

        var reportedTokens = TokenBudgetGuard.Enforce(
            estimated,
            budget.ContextTokens,
            budget.HeadroomTokens,
            clampedTargetTokens,
            MarkHeadroomGuard);

        if (reportedTokens < estimated)
        {
            AddFallbackTag("prompt_trimmed");
        }

        plan = plan with
        {
            Compressed = plan.Compression.IsCompressed,
            TrimmedForBudget = plan.Compression.IsTrimmed,
            ActualPromptTokens = reportedTokens,
            CompressionRatio = compressionRatio,
            DriftRatio = driftRatio,
            ContextWindow = budget.ContextTokens,
            HeadroomTokens = budget.HeadroomTokens
        };

        // Record compression metrics
        _metrics.Record(MetricsNames.PromptActualTokens, reportedTokens, metricTags);

        if (plan.EstimatedTokensPreCompression > 0)
        {
            _metrics.Record(MetricsNames.PromptTokensPre, plan.EstimatedTokensPreCompression, metricTags);
        }

        _metrics.Record(MetricsNames.PromptTokensPost, reportedTokens, metricTags);
        _metrics.Record(MetricsNames.PromptCompressionRatio, plan.CompressionRatio ?? 1.0, metricTags);

        return new CompressionResult(
            prompt,
            plan,
            baselineTokens,
            reportedTokens,
            guardTriggered,
            fallbackReason);
    }
}
