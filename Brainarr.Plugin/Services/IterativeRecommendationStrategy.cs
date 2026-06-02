using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Implements an iterative strategy for obtaining music recommendations from AI providers.
    /// This strategy handles duplicate filtering, library deduplication, and iterative
    /// refinement to achieve the target number of unique recommendations.
    /// </summary>
    /// <remarks>
    /// The strategy works in multiple iterations:
    /// 1. Request initial batch of recommendations
    /// 2. Filter out existing library items and duplicates
    /// 3. If insufficient unique items, request more with context about rejected items
    /// 4. Continue until target count reached or max iterations exhausted
    ///
    /// This approach significantly improves the quality and uniqueness of recommendations
    /// by providing the AI with feedback about what was already suggested or exists.
    /// </remarks>
    public class IterativeRecommendationStrategy
    {
        private static readonly System.Text.RegularExpressions.Regex WhitespaceRx =
            new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly Logger _logger;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;
        private readonly IProviderInvoker _providerInvoker;

        // Maximum number of iterations to prevent infinite loops
        // Default minimum success rate to continue iterations; tuned dynamically per sampling strategy
        private const double DEFAULT_MIN_SUCCESS_RATE = 0.7;

        // Hard ceiling on total iterations once discovery-mode escalation starts extending the budget,
        // so a genuinely stuck provider can't loop indefinitely even under aggressive top-up.
        private const int ESCALATION_ITERATION_CEILING = 20;

        public IterativeRecommendationStrategy(Logger logger, ILibraryAwarePromptBuilder promptBuilder, IProviderInvoker providerInvoker = null)
        {
            _logger = logger;
            _promptBuilder = promptBuilder;
            _providerInvoker = providerInvoker ?? new ProviderInvoker();
        }

        /// <summary>
        /// Executes the iterative recommendation strategy to obtain unique music recommendations.
        /// </summary>
        /// <param name="provider">The AI provider to use for generating recommendations</param>
        /// <param name="profile">The user's library profile containing genre and artist preferences</param>
        /// <param name="allArtists">All artists in the user's library for context</param>
        /// <param name="allAlbums">All albums in the library to avoid duplicates</param>
        /// <param name="settings">Configuration settings including target recommendation count</param>
        /// <returns>A list of unique, validated recommendations not in the existing library</returns>
        /// <remarks>
        /// This method implements a feedback loop where each iteration learns from previous
        /// rejections to improve recommendation quality and reduce duplicates.
        /// </remarks>
        public async Task<List<Recommendation>> GetIterativeRecommendationsAsync(
            IAIProvider provider,
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            bool shouldRecommendArtists = false,
            Dictionary<string, int>? validationReasons = null,
            List<Recommendation>? rejectedExamplesFromValidation = null,
            bool aggressiveGuarantee = false,
            IReadOnlyList<Recommendation>? alreadyProvided = null,
            CancellationToken cancellationToken = default)
        {
            var existingKeys = shouldRecommendArtists ?
                BuildExistingArtistsSet(allArtists) :
                BuildExistingAlbumsSet(allAlbums);
            var allRecommendations = new List<Recommendation>();
            var rejectedAlbums = new HashSet<string>();
            var rejectedNames = new List<string>();

            // T1: seed the dedup baseline AND the prompt avoid-list with recommendations already
            // delivered to the user in this run (the initial batch). Without this, the top-up prompt
            // never tells the model what it already produced, so a saturated provider re-emits delivered
            // artists; the strategy then counts them "new" (they are not in the LIBRARY) and they are
            // only dropped post-hoc — wasting the iteration. Seeding existingKeys makes the strategy's
            // own dedup recognize any that slip through; seeding rejectedNames puts them into the
            // iteration-1 [[SYSTEM_AVOID]] marker so the model is told up front. The delivered set is
            // bounded by MaxRecommendations and the prompt avoid-list is independently capped (Take 50).
            if (alreadyProvided != null)
            {
                foreach (var rec in alreadyProvided)
                {
                    if (rec is null || string.IsNullOrWhiteSpace(rec.Artist)) continue;
                    var key = shouldRecommendArtists
                        ? NormalizeArtistKey(rec.Artist)
                        : NormalizeAlbumKey(rec.Artist, rec.Album);
                    existingKeys.Add(key);
                    var nm = (!shouldRecommendArtists && !string.IsNullOrWhiteSpace(rec.Album))
                        ? $"{rec.Artist.Trim()} - {rec.Album.Trim()}"
                        : rec.Artist.Trim();
                    if (!string.IsNullOrWhiteSpace(nm) && !rejectedNames.Contains(nm, StringComparer.OrdinalIgnoreCase))
                    {
                        rejectedNames.Add(nm);
                    }
                }
            }

            var targetCount = settings.MaxRecommendations;
            var ip = settings.GetIterationProfile();
            var iteration = 1; var zeroSuccessStreak = 0; var lowSuccessStreak = 0; var maxIterations = ip.MaxIterations <= 0 ? 0 : ip.MaxIterations; var zeroStop = ip.ZeroStop <= 0 ? 0 : ip.ZeroStop; var lowStop = ip.LowStop <= 0 ? 0 : ip.LowStop; var cooldownMs = ip.CooldownMs < 0 ? 0 : ip.CooldownMs;
            // Bump iterations automatically for comprehensive sampling to better reach targets on large libraries
            if (settings.SamplingStrategy == SamplingStrategy.Comprehensive && maxIterations < 5)
            {
                maxIterations = 5;
            }

            // Dynamic gating: relax success-rate expectations for comprehensive sampling on large, duplicate-heavy libraries
            var minSuccessRate = settings.SamplingStrategy switch
            {
                SamplingStrategy.Minimal => DEFAULT_MIN_SUCCESS_RATE,           // 0.70
                SamplingStrategy.Balanced => 0.5,
                SamplingStrategy.Comprehensive => 0.2,
                _ => DEFAULT_MIN_SUCCESS_RATE
            };

            // Aggregate diagnostics
            var tokenEstimates = new List<int>();
            int totalSuggested = 0;
            int totalUnique = 0;
            int lastRequest = 0;

            // Discovery-mode escalation: as the library/history grows, top-up iterations increasingly
            // re-suggest the same cluster (novelty collapse), so the run stalls under target. Rather
            // than stop, widen the effective discovery mode one step toward Exploratory to break out of
            // the saturated neighbourhood. Tracked locally; the original settings value is never mutated
            // outside the per-prompt scope below.
            //
            // Gated on the CALLER's aggressive request (captured before the iteration-1 auto-enable
            // below): escalation is a "fill the target" behavior, active in the top-up path
            // (TopUpPlanner passes aggressiveGuarantee:true) where attainment matters — not a change to
            // the base strategy's early-stop semantics when a caller just wants a quick single pass.
            var callerRequestedAggressive = aggressiveGuarantee;
            var currentDiscovery = settings.DiscoveryMode;

            while (allRecommendations.Count < targetCount && iteration <= maxIterations)
            {
                _logger.Info($"Iteration {iteration}: Need {targetCount - allRecommendations.Count} more recommendations");

                // Dynamically adjust request size based on iteration and remaining needs
                // Later iterations request more to account for expected duplicates
                var requestSize = CalculateIterationRequestSize(targetCount - allRecommendations.Count, iteration, settings);
                if (aggressiveGuarantee)
                {
                    requestSize = Math.Min(requestSize * 3 / 2, requestSize + 20);
                }
                lastRequest = requestSize;

                _logger.Debug($"Iteration {iteration}: Requesting {requestSize} recommendations from AI provider");

                // Build context-aware prompt that includes:
                // - Previous rejections to avoid repeats
                // - Already accepted recommendations for diversity
                // - Library context for better personalization
                // Render the prompt under the (possibly escalated) discovery mode. The scope restores
                // settings.DiscoveryMode immediately after so nothing else observes the override.
                string prompt;
                string? modelKey;
                using (SettingScope.Apply(
                    getter: () => settings.DiscoveryMode,
                    setter: v => settings.DiscoveryMode = v,
                    newValue: currentDiscovery))
                {
                    prompt = BuildIterativePrompt(
                        profile,
                        allArtists,
                        allAlbums,
                        settings,
                        requestSize,
                        rejectedAlbums,
                        allRecommendations,
                        rejectedNames,
                        out modelKey,
                        shouldRecommendArtists,
                        validationReasons,
                        rejectedExamplesFromValidation);
                }
                try { tokenEstimates.Add(_promptBuilder.EstimateTokens(prompt, modelKey)); }
                catch (OperationCanceledException) { throw; } // Propagate cancellation
                catch (Exception) { /* Token estimation failure is non-critical */ }

                try
                {
                    // Get recommendations from AI
                    _logger.Debug($"Iteration {iteration}: Attempting to connect to AI provider for recommendations...");
                    var recommendations = await _providerInvoker.InvokeAsync(provider, prompt, _logger, cancellationToken, operationLabel: "TopUp.Provider.GetRecommendations");
                    _logger.Debug($"Iteration {iteration}: Received {recommendations?.Count ?? 0} recommendations from AI provider");

                    if (!recommendations.Any())
                    {
                        _logger.Warn($"Iteration {iteration}: No recommendations received");
                        break;
                    }

                    // Filter out duplicates and track rejections
                    var (uniqueRecs, duplicates) = FilterAndTrackDuplicates(
                        recommendations, existingKeys, allRecommendations, rejectedAlbums, shouldRecommendArtists);

                    allRecommendations.AddRange(uniqueRecs);
                    totalSuggested += recommendations.Count;
                    totalUnique += uniqueRecs.Count;

                    try
                    {
                        if (settings.LogPerItemDecisions)
                        {
                            // Rejections with reason
                            foreach (var rr in duplicates)
                            {
                                var name = string.IsNullOrWhiteSpace(rr.Album) ? rr.Artist : $"{rr.Artist} — {rr.Album}";
                                _logger.Info($"[Brainarr] Iteration {iteration} Rejected: {name}");
                            }
                            // Accepted shown only when debug is enabled
                            if (settings.EnableDebugLogging)
                            {
                                foreach (var r in uniqueRecs)
                                {
                                    var name = string.IsNullOrWhiteSpace(r.Album) ? r.Artist : $"{r.Artist} — {r.Album}";
                                    _logger.Info($"[Brainarr Debug] Iteration {iteration} Accepted: {name}");
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* Per-item logging failure is non-critical */ }

                    // Accumulate explicit rejected names for next-iteration blocklist
                    try
                    {
                        foreach (var rr in duplicates)
                        {
                            var nm = string.IsNullOrWhiteSpace(rr.Album) ? rr.Artist : $"{rr.Artist} — {rr.Album}";
                            if (!string.IsNullOrWhiteSpace(nm) && !rejectedNames.Contains(nm, StringComparer.OrdinalIgnoreCase))
                            {
                                rejectedNames.Add(nm);
                                if (rejectedNames.Count > 200) rejectedNames.RemoveAt(0);
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* Rejected names tracking failure is non-critical */ }

                    // Log iteration results
                    var successRate = recommendations.Any() ? (double)uniqueRecs.Count / recommendations.Count : 0;
                    _logger.Info($"Iteration {iteration}: {uniqueRecs.Count}/{recommendations.Count} new " +
                                $"(not already in library/delivered/rejected) (success rate: {successRate:P1})");
                    // Correlate tokens to success rate (compact summary)
                    if (settings.EnableDebugLogging)
                    {
                        try
                        {
                            var limit = _promptBuilder.GetEffectiveTokenLimit(settings.SamplingStrategy, settings.Provider);
                            var est = _promptBuilder.EstimateTokens(prompt, modelKey);
                            _logger.Info($"[Brainarr Debug] Iteration Summary => SuccessRate={successRate:P1}, Tokens≈{est}/{limit}, Requested={requestSize}, Unique={uniqueRecs.Count}");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { /* Debug summary logging failure is non-critical */ }
                    }
                    // Hysteresis controls: Stop early if results are repeatedly rejected
                    if (uniqueRecs.Count == 0) zeroSuccessStreak++; else zeroSuccessStreak = 0;
                    if (successRate < minSuccessRate) lowSuccessStreak++; else lowSuccessStreak = 0;
                    if ((zeroStop > 0 && zeroSuccessStreak >= zeroStop) || (lowStop > 0 && lowSuccessStreak >= lowStop))
                    {
                        // Saturated this cluster, but still under target — try widening discovery before
                        // giving up. Resetting the streaks gives the broader mode a fair shot; escalation
                        // is bounded (Similar→Adjacent→Exploratory) so this can't loop forever, and the
                        // outer maxIterations guard still applies.
                        if (callerRequestedAggressive &&
                            allRecommendations.Count < targetCount &&
                            TryEscalateDiscoveryMode(currentDiscovery, out var widerMode))
                        {
                            _logger.Info($"Dedup saturation (zero={zeroSuccessStreak}, low={lowSuccessStreak}) under target ({allRecommendations.Count}/{targetCount}); escalating discovery {currentDiscovery}→{widerMode} to widen the search instead of stopping.");
                            currentDiscovery = widerMode;
                            zeroSuccessStreak = 0;
                            lowSuccessStreak = 0;
                            // Give the wider mode enough budget to actually run before it too can
                            // saturate — the aggressive/top-up caller explicitly wants the target filled.
                            // Bounded: at most 2 escalations (the mode ladder) and a hard ceiling so a
                            // pathological stuck provider can't loop indefinitely.
                            maxIterations = Math.Min(
                                ESCALATION_ITERATION_CEILING,
                                Math.Max(maxIterations, iteration + Math.Max(1, Math.Max(zeroStop, lowStop))));
                            iteration++;
                            continue;
                        }

                        _logger.Warn($"Stopping iterations early due to low/zero success streak (zero={zeroSuccessStreak}, low={lowSuccessStreak})");
                        // Small cool-down for local providers to reduce churn. Honor the run token:
                        // a cancelled run must not block here for the full cooldown (the OCE propagates
                        // through the line-309 guard as run-cancellation, not a silent partial return).
                        if (settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio)
                        {
                            if (cooldownMs > 0) await Task.Delay(cooldownMs, cancellationToken);
                        }
                        break;
                    }

                    // Check if we should continue
                    // Allow one extra iteration if aggressively trying to hit target. Bounded by the
                    // same ceiling escalation uses, so a provider that dribbles uniques just above the
                    // min-success rate (never tripping the streak-stop) can't grow maxIterations without
                    // limit — making ESCALATION_ITERATION_CEILING a real ceiling, not just an
                    // escalation-path one. Termination no longer depends solely on the outer timeout.
                    if (aggressiveGuarantee && allRecommendations.Count < targetCount &&
                        iteration >= maxIterations && maxIterations < ESCALATION_ITERATION_CEILING)
                    {
                        maxIterations++;
                    }

                    if (iteration == 1 && successRate < 0.20 && !aggressiveGuarantee)
                    {
                        aggressiveGuarantee = true;
                        if (maxIterations < 10) maxIterations = 10;
                        _logger.Info("Low unique rate on first iteration; escalating to Aggressive backfill (guarantee target)");
                    }

                    if (ShouldContinueIterating(successRate, allRecommendations.Count, targetCount, iteration, maxIterations, aggressiveGuarantee, minSuccessRate))
                    {
                        iteration++;
                        continue;
                    }

                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The RUN was cancelled (host abort / linked overall-budget token) — propagate so
                    // the cancellation-aware orchestrator path surfaces it as a cancelled fetch rather
                    // than swallowing it here and returning a partial list as if the run succeeded.
                    // NOTE: a provider's OWN request timeout throws OperationCanceled/TaskCanceled with
                    // our token NOT cancelled; that falls through to the catch below and is treated as a
                    // recoverable per-iteration failure (break + return partial), which is intended.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Iteration {iteration} failed: {ex.Message}");
                    _logger.Debug($"Iteration {iteration}: Exception details - Type: {ex.GetType().Name}, Message: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        _logger.Debug($"Iteration {iteration}: Inner exception - {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                    break;
                }
            }

            _logger.Info($"Iterative strategy completed: {allRecommendations.Count}/{targetCount} recommendations after {iteration} iterations");
            try
            {
                if (tokenEstimates.Count > 0 && totalSuggested > 0)
                {
                    var avgTokens = (int)System.Linq.Enumerable.Average(tokenEstimates);
                    var overallRate = (double)totalUnique / totalSuggested;
                    _logger.Info($"[Brainarr] Iteration Summary => Iterations={iteration}, OverallUnique={totalUnique}/{totalSuggested} ({overallRate:P1}), AvgTokens≈{avgTokens}, LastRequest={lastRequest}");
                }
            }
            catch (Exception) { /* Final summary logging failure is non-critical */ }

            return allRecommendations.Take(targetCount).ToList();
        }

        /// <summary>
        /// Widens a discovery mode one step toward Exploratory (Similar→Adjacent→Exploratory). Returns
        /// false at Exploratory (already the widest), leaving <paramref name="next"/> unchanged. Pure +
        /// monotonic so the escalation loop in the top-up strategy is guaranteed to terminate.
        /// </summary>
        internal static bool TryEscalateDiscoveryMode(DiscoveryMode current, out DiscoveryMode next)
        {
            switch (current)
            {
                case DiscoveryMode.Similar:
                    next = DiscoveryMode.Adjacent;
                    return true;
                case DiscoveryMode.Adjacent:
                    next = DiscoveryMode.Exploratory;
                    return true;
                default:
                    next = current;
                    return false;
            }
        }

        private HashSet<string> BuildExistingAlbumsSet(List<Album> allAlbums)
        {
            return allAlbums
                .Where(a => a.ArtistMetadata?.Value?.Name != null && a.Title != null)
                .Select(a => NormalizeAlbumKey(a.ArtistMetadata.Value.Name, a.Title))
                .ToHashSet();
        }

        private HashSet<string> BuildExistingArtistsSet(List<Artist> allArtists)
        {
            return allArtists
                .Where(a => a.Name != null)
                .Select(a => NormalizeArtistKey(a.Name))
                .ToHashSet();
        }

        private int CalculateIterationRequestSize(int needed, int iteration, BrainarrSettings settings)
        {
            // Request more than needed to account for duplicates, with diminishing over-request
            var multiplier = iteration switch
            {
                1 => 1.5, // 50% more on first try
                2 => 2.0, // 100% more on second try (AI should learn)
                3 => 3.0, // 200% more on final try (desperate)
                _ => 1.0
            };
            // Dynamic cap based on sampling strategy and provider capability
            var cap = (settings.SamplingStrategy == SamplingStrategy.Comprehensive)
                ? ((settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio) ? 150 : 120)
                : ((settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio) ? 100 : 80);

            return Math.Min(cap, Math.Max(needed, (int)(needed * multiplier)));
        }

        private string BuildIterativePrompt(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations,
            List<string> rejectedNames,
            out string? modelKey,
            bool shouldRecommendArtists = false,
            Dictionary<string, int>? validationReasons = null,
            List<Recommendation>? rejectedExamplesFromValidation = null)
        {
            // Use the base library-aware prompt with recommendation mode (with metrics)
            LibraryPromptResult baseRes = null;
            string basePrompt = null;
            try
            {
                baseRes = _promptBuilder?.BuildLibraryAwarePromptWithMetrics(profile, allArtists, allAlbums, settings, shouldRecommendArtists);
                basePrompt = baseRes?.Prompt;
            }
            catch
            {
                baseRes = null;
            }

            // Surface the resolved model key (e.g. "zaicoding:glm-5.1") the metrics path computed so
            // the top-up's token-estimate diagnostics resolve the SAME model-specific tokenizer the
            // initial prompt used — rather than passing null and silently falling back to the
            // "<default>" tokenizer (which both diverges the estimate and emits a misleading
            // "no tokenizer registered for <default>" WARN). Null/empty when unresolved; callers
            // pass it straight through to EstimateTokens, whose registry treats blank as <default>.
            modelKey = baseRes?.BudgetModelKey;

            // Backwards-compat fallback for tests/mocks that only implement BuildLibraryAwarePrompt
            if (string.IsNullOrWhiteSpace(basePrompt))
            {
                basePrompt = _promptBuilder?.BuildLibraryAwarePrompt(profile, allArtists, allAlbums, settings, shouldRecommendArtists) ?? string.Empty;
                baseRes = baseRes ?? new LibraryPromptResult
                {
                    Prompt = basePrompt,
                    SampledArtists = 0,
                    SampledAlbums = 0,
                    EstimatedTokens = _promptBuilder != null ? _promptBuilder.EstimateTokens(basePrompt) : 0
                };
            }

            // Add iteration-specific context
            var iterativeContext = BuildIterativeContext(requestSize, rejectedAlbums, existingRecommendations, rejectedNames, shouldRecommendArtists, validationReasons, rejectedExamplesFromValidation);
            var prompt = basePrompt + System.Environment.NewLine + System.Environment.NewLine + iterativeContext;

            // Prepend a system-avoid marker for provider to elevate to system role
            if (rejectedNames != null && rejectedNames.Any())
            {
                var avoidList = string.Join("|", rejectedNames.Take(50).Select(n => n.Replace("\n", " ").Trim()));
                var marker = $"[[SYSTEM_AVOID:{avoidList}]]" + System.Environment.NewLine;
                prompt = marker + prompt;
            }

            // Emit diagnostics (tokens + sampled sizes) if debug logging is enabled
            if (settings.EnableDebugLogging && _promptBuilder != null)
            {
                try
                {
                    var limit = _promptBuilder.GetEffectiveTokenLimit(settings.SamplingStrategy, settings.Provider);
                    var est = _promptBuilder.EstimateTokens(prompt, modelKey);
                    _logger.Info($"[Brainarr Debug] Iteration Tokens => Strategy={settings.SamplingStrategy}, Provider={settings.Provider}, Limit≈{limit}, EstimatedUsed≈{est}, Sampled: {baseRes.SampledArtists} artists, {baseRes.SampledAlbums} albums, Request={requestSize}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* Token diagnostic logging failure is non-critical */ }
            }

            return prompt;
        }

        private string BuildIterativeContext(
            int requestSize,
            HashSet<string> rejectedAlbums,
            List<Recommendation> existingRecommendations,
            List<string> rejectedNames,
            bool shouldRecommendArtists = false,
            Dictionary<string, int>? validationReasons = null,
            List<Recommendation>? rejectedExamplesFromValidation = null)
        {
            var contextBuilder = new System.Text.StringBuilder();

            contextBuilder.AppendLine("🔄 ITERATIVE REQUEST CONTEXT:");
            contextBuilder.AppendLine($"• Requesting {requestSize} recommendations");
            contextBuilder.AppendLine(shouldRecommendArtists
                ? "• Mode: ARTISTS ONLY — do not include album fields"
                : "• Mode: SPECIFIC ALBUMS — include album title and year; do not return artist-only entries");

            if (rejectedAlbums.Any())
            {
                contextBuilder.AppendLine($"• Previously rejected {rejectedAlbums.Count} duplicates - avoid these patterns");

                // Show some examples of rejected albums to help AI learn
                var rejectedExamples = rejectedAlbums.Take(10).ToList();
                if (rejectedExamples.Any())
                {
                    contextBuilder.AppendLine($"• Recent duplicates to avoid: {string.Join(", ", rejectedExamples)}");
                }
            }
            if (rejectedNames != null && rejectedNames.Any())
            {
                var show = rejectedNames.Take(25).ToList();
                contextBuilder.AppendLine($"• Avoid these (previously rejected): {string.Join(", ", show)}");
            }
            // Include validation feedback to steer the model
            if (validationReasons != null && validationReasons.Any())
            {
                contextBuilder.AppendLine("• Validation rejections last pass:");
                foreach (var kv in validationReasons.OrderByDescending(kv => kv.Value).Take(3))
                {
                    contextBuilder.AppendLine($"  - {kv.Key}: {kv.Value}");
                }
            }
            if (rejectedExamplesFromValidation != null && rejectedExamplesFromValidation.Any())
            {
                var samples = rejectedExamplesFromValidation
                    .Select(r => shouldRecommendArtists ? r.Artist : $"{r.Artist} — {r.Album}")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .Take(5)
                    .ToList();
                if (samples.Any())
                {
                    contextBuilder.AppendLine($"• Recently rejected examples: {string.Join(", ", samples)}");
                }
            }

            if (existingRecommendations.Any())
            {
                contextBuilder.AppendLine($"• Already recommended {existingRecommendations.Count} albums in this session");

                // Show already recommended artists to encourage diversity
                var recommendedArtists = existingRecommendations
                    .Select(r => r.Artist)
                    .Distinct()
                    .Take(15)
                    .ToList();

                if (recommendedArtists.Any())
                {
                    contextBuilder.AppendLine($"• Already recommended artists: {string.Join(", ", recommendedArtists)}");
                    contextBuilder.AppendLine("• Try to diversify with different artists where possible");
                }
            }

            contextBuilder.AppendLine();
            contextBuilder.AppendLine("💡 OPTIMIZATION HINTS:");
            contextBuilder.AppendLine("• Focus on lesser-known albums by known artists");
            contextBuilder.AppendLine("• Consider B-sides, live albums, or collaborations");
            contextBuilder.AppendLine("• Explore different eras of the same artists");

            return contextBuilder.ToString();
        }

        private (List<Recommendation> unique, List<Recommendation> duplicates) FilterAndTrackDuplicates(
            List<Recommendation> recommendations,
            HashSet<string> existingKeys,
            List<Recommendation> alreadyRecommended,
            HashSet<string> rejectedAlbums,
            bool shouldRecommendArtists = false)
        {
            var unique = new List<Recommendation>();
            var duplicates = new List<Recommendation>();

            var alreadyRecommendedKeys = alreadyRecommended
                .Select(r => NormalizeAlbumKey(r.Artist, r.Album))
                .ToHashSet();

            foreach (var rec in recommendations)
            {
                // Always require artist name
                if (string.IsNullOrWhiteSpace(rec.Artist)) { duplicates.Add(rec); continue; }

                // In album mode, require both artist and album
                // In artist mode, allow artist-only recommendations
                if (!shouldRecommendArtists && string.IsNullOrWhiteSpace(rec.Album)) { duplicates.Add(rec); continue; }

                // For artist mode, use artist as key; for album mode, use artist+album
                var albumKey = shouldRecommendArtists ?
                    NormalizeArtistKey(rec.Artist) :
                    NormalizeAlbumKey(rec.Artist, rec.Album);

                // Check if it's a duplicate of existing library
                if (existingKeys.Contains(albumKey)) { rejectedAlbums.Add(albumKey); duplicates.Add(rec); continue; }

                // Check if already recommended in this session
                if (alreadyRecommendedKeys.Contains(albumKey)) { duplicates.Add(rec); continue; }

                unique.Add(rec);
                alreadyRecommendedKeys.Add(albumKey);
            }

            return (unique, duplicates);
        }

        private bool ShouldContinueIterating(double successRate, int currentCount, int targetCount, int iteration, int maxIterations, bool aggressiveGuarantee, double minSuccessRate)
        {
            // Don't continue if we have enough recommendations
            if (currentCount >= targetCount)
                return false;

            // Don't exceed max iterations
            if (iteration >= maxIterations)
                return false;

            // Continue if we're significantly short of target
            var completionRate = (double)currentCount / targetCount;
            if (aggressiveGuarantee)
            {
                // In aggressive mode, ignore success-rate gating; rely on hysteresis (zero/low streak)
                // and the maxIterations cap to prevent runaway loops.
                return true;
            }
            if (completionRate < 0.8)
            {
                // If aggressively trying to hit target, ignore success-rate gating
                if (successRate < minSuccessRate && iteration > 1)
                    return false;
                return true;
            }

            return false;
        }

        private string NormalizeAlbumKey(string artist, string album)
        {
            // Consistent normalization for duplicate detection
            var normalizedArtist = artist?.Trim().ToLowerInvariant() ?? "";
            var normalizedAlbum = album?.Trim().ToLowerInvariant() ?? "";

            // Remove common variations that might cause false negatives
            normalizedArtist = WhitespaceRx.Replace(normalizedArtist, " ");
            normalizedAlbum = WhitespaceRx.Replace(normalizedAlbum, " ");

            return $"{normalizedArtist}_{normalizedAlbum}";
        }

        private string NormalizeArtistKey(string artist)
        {
            // Consistent normalization for artist-only recommendations
            var normalizedArtist = artist?.Trim().ToLowerInvariant() ?? "";

            // Remove common variations that might cause false negatives
            normalizedArtist = WhitespaceRx.Replace(normalizedArtist, " ");

            return $"artist_{normalizedArtist}";
        }
    }
}
