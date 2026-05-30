using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for BrainarrSettings.Discovery.cs — default values,
    /// property setters, computed BaseUrl, and the EnableAutoDetection proxy.
    /// </summary>
    public class BrainarrSettingsDiscoveryCovTests
    {
        private BrainarrSettings Create() => new BrainarrSettings();

        #region Constructor Defaults — Discovery Enums

        // Source line 30: DiscoveryMode = DiscoveryMode.Adjacent;
        // Proof: grep -n "DiscoveryMode" Brainarr.Plugin/BrainarrSettings.cs
        //   30:            DiscoveryMode = DiscoveryMode.Adjacent;
        [Fact]
        public void DiscoveryMode_Default_IsAdjacent()
        {
            var s = Create();
            s.DiscoveryMode.Should().Be(DiscoveryMode.Adjacent,
                "because the constructor sets DiscoveryMode to Adjacent (line 30)");
        }

        // Source line 31: SamplingStrategy = SamplingStrategy.Balanced;
        // Proof: grep -n "SamplingStrategy" Brainarr.Plugin/BrainarrSettings.cs
        //   31:            SamplingStrategy = SamplingStrategy.Balanced;
        [Fact]
        public void SamplingStrategy_Default_IsBalanced()
        {
            var s = Create();
            s.SamplingStrategy.Should().Be(SamplingStrategy.Balanced,
                "because the constructor sets SamplingStrategy to Balanced (line 31)");
        }

        // Source line 32: RecommendationMode = RecommendationMode.SpecificAlbums;
        // Proof: grep -n "RecommendationMode" Brainarr.Plugin/BrainarrSettings.cs
        //   32:            RecommendationMode = RecommendationMode.SpecificAlbums;
        [Fact]
        public void RecommendationMode_Default_IsSpecificAlbums()
        {
            var s = Create();
            s.RecommendationMode.Should().Be(RecommendationMode.SpecificAlbums,
                "because the constructor sets RecommendationMode to SpecificAlbums (line 32)");
        }

        // Source line 36: BackfillStrategy = BackfillStrategy.Standard;
        // Proof: grep -n "BackfillStrategy" Brainarr.Plugin/BrainarrSettings.cs
        //   36:            BackfillStrategy = BackfillStrategy.Standard;
        [Fact]
        public void BackfillStrategy_Default_IsStandard()
        {
            var s = Create();
            s.BackfillStrategy.Should().Be(BackfillStrategy.Standard,
                "because the constructor sets BackfillStrategy to Standard (line 36)");
        }

        [Fact]
        public void BackfillStrategy_FieldLabelAndHelp_MatchActualDefault()
        {
            // Drift guard: the UI label once advertised "Default: Aggressive" while the constructor
            // (and the test above) set Standard — a user-facing contradiction. Pin the label/help-text
            // "(Default)" annotation to the ACTUAL default so the two can never diverge again.
            var defaultStrategy = Create().BackfillStrategy.ToString();
            var attr = typeof(BrainarrSettings)
                .GetProperty(nameof(BrainarrSettings.BackfillStrategy))!
                .GetCustomAttributes(typeof(NzbDrone.Core.Annotations.FieldDefinitionAttribute), false)
                .Cast<NzbDrone.Core.Annotations.FieldDefinitionAttribute>()
                .Single();

            attr.Label.Should().Contain($"Default: {defaultStrategy}",
                "the field label must name the actual constructor default");
            attr.HelpText.Should().Contain($"{defaultStrategy} (Default)",
                "the help text must mark the actual default option as (Default)");
            foreach (var other in System.Enum.GetNames(typeof(BackfillStrategy)).Where(n => n != defaultStrategy))
            {
                attr.HelpText.Should().NotContain($"{other} (Default)",
                    $"a non-default option ({other}) must not be marked (Default)");
            }
        }

        #endregion

        #region Constructor Defaults — Core Numeric & Bool Fields

        // Source line 33: AutoDetectModel = true;
        // Proof: grep -n "AutoDetectModel = true" Brainarr.Plugin/BrainarrSettings.cs
        //   33:            AutoDetectModel = true;
        [Fact]
        public void AutoDetectModel_Default_IsTrue()
        {
            var s = Create();
            s.AutoDetectModel.Should().BeTrue("because the constructor sets AutoDetectModel to true (line 33)");
        }

        // Source line 29: MaxRecommendations = BrainarrConstants.DefaultRecommendations;
        // Proof: grep -n "DefaultRecommendations" Brainarr.Plugin/Configuration/Constants.cs
        //   33:        public const int DefaultRecommendations = 20;
        [Fact]
        public void MaxRecommendations_Default_Is20()
        {
            var s = Create();
            s.MaxRecommendations.Should().Be(20,
                "because DefaultRecommendations = 20 (Constants.cs:33)");
        }

        // Source line 35: EnableIterativeRefinement = true;
        // Proof: grep -n "EnableIterativeRefinement = true" Brainarr.Plugin/BrainarrSettings.cs
        //   35:            EnableIterativeRefinement = true;
        [Fact]
        public void EnableIterativeRefinement_Default_IsTrue()
        {
            var s = Create();
            s.EnableIterativeRefinement.Should().BeTrue(
                "because the constructor enables iterative refinement for local default provider (line 35)");
        }

        // Source line 41: public int MaxTopUpIterations { get; set; } = 0;
        // Proof: grep -n "MaxTopUpIterations" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   41:        public int MaxTopUpIterations { get; set; } = 0;
        [Fact]
        public void MaxTopUpIterations_Default_IsZero()
        {
            var s = Create();
            s.MaxTopUpIterations.Should().Be(0,
                "because MaxTopUpIterations defaults to 0 (Discovery.cs:41)");
        }

        #endregion

        #region Defaults — AI, Timeout, and Throttling

        // Source line 64: CacheDuration = TimeSpan.FromHours(BrainarrConstants.MinRefreshIntervalHours);
        // Proof: grep -n "MinRefreshIntervalHours" Brainarr.Plugin/Configuration/Constants.cs
        //   83:        public const int MinRefreshIntervalHours = 6;
        [Fact]
        public void CacheDuration_Default_Is6Hours()
        {
            var s = Create();
            s.CacheDuration.Should().Be(TimeSpan.FromHours(6),
                "because MinRefreshIntervalHours = 6 (Constants.cs:83)");
        }

        // Source line 102: AIRequestTimeoutSeconds = BrainarrConstants.DefaultAITimeout;
        // Proof: grep -n "DefaultAITimeout" Brainarr.Plugin/Configuration/Constants.cs
        //   37:        public const int DefaultAITimeout = 30;
        [Fact]
        public void AIRequestTimeoutSeconds_Default_Is30()
        {
            var s = Create();
            s.AIRequestTimeoutSeconds.Should().Be(30,
                "because DefaultAITimeout = 30 (Constants.cs:37)");
        }

        // Source line 74: IterativeMaxIterations { get; set; } = 3;
        // Proof: grep -n "IterativeMaxIterations" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   74:        public int IterativeMaxIterations { get; set; } = 3;
        [Fact]
        public void IterativeMaxIterations_Default_Is3()
        {
            var s = Create();
            s.IterativeMaxIterations.Should().Be(3, "because the default is 3 (Discovery.cs:74)");
        }

        // Source line 79: IterativeZeroSuccessStopThreshold { get; set; } = 1;
        // Proof: grep -n "IterativeZeroSuccessStopThreshold" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   79:        public int IterativeZeroSuccessStopThreshold { get; set; } = 1;
        [Fact]
        public void IterativeZeroSuccessStopThreshold_Default_Is1()
        {
            var s = Create();
            s.IterativeZeroSuccessStopThreshold.Should().Be(1,
                "because the default is 1 (Discovery.cs:79)");
        }

        // Source line 84: IterativeLowSuccessStopThreshold { get; set; } = 2;
        // Proof: grep -n "IterativeLowSuccessStopThreshold" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   84:        public int IterativeLowSuccessStopThreshold { get; set; } = 2;
        [Fact]
        public void IterativeLowSuccessStopThreshold_Default_Is2()
        {
            var s = Create();
            s.IterativeLowSuccessStopThreshold.Should().Be(2,
                "because the default is 2 (Discovery.cs:84)");
        }

        // Source line 89: IterativeCooldownMs { get; set; } = 1000;
        // Proof: grep -n "IterativeCooldownMs" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   89:        public int IterativeCooldownMs { get; set; } = 1000;
        [Fact]
        public void IterativeCooldownMs_Default_Is1000()
        {
            var s = Create();
            s.IterativeCooldownMs.Should().Be(1000, "because the default is 1000 (Discovery.cs:89)");
        }

        // Source line 96: TopUpStopSensitivity { get; set; } = StopSensitivity.Lenient;
        // Proof: grep -n "TopUpStopSensitivity" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   96:        public StopSensitivity TopUpStopSensitivity { get; set; } = StopSensitivity.Lenient;
        [Fact]
        public void TopUpStopSensitivity_Default_IsLenient()
        {
            var s = Create();
            s.TopUpStopSensitivity.Should().Be(StopSensitivity.Lenient,
                "because the default is Lenient (Discovery.cs:96)");
        }

        // Source line 148: EnableAdaptiveThrottling { get; set; } = false;
        // Proof: grep -n "EnableAdaptiveThrottling" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   148:        public bool EnableAdaptiveThrottling { get; set; } = false;
        [Fact]
        public void EnableAdaptiveThrottling_Default_IsFalse()
        {
            var s = Create();
            s.EnableAdaptiveThrottling.Should().BeFalse(
                "because the default is false (Discovery.cs:148)");
        }

        // Source line 152: AdaptiveThrottleSeconds { get; set; } = 60;
        // Proof: grep -n "AdaptiveThrottleSeconds" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   152:        public int AdaptiveThrottleSeconds { get; set; } = 60;
        [Fact]
        public void AdaptiveThrottleSeconds_Default_Is60()
        {
            var s = Create();
            s.AdaptiveThrottleSeconds.Should().Be(60, "because the default is 60 (Discovery.cs:152)");
        }

        #endregion

        #region Defaults — Model, Fallback, and Validation

        // Source line 112: LMStudioTemperature { get; set; } = 0.5;
        // Proof: grep -n "LMStudioTemperature" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   112:        public double LMStudioTemperature { get; set; } = 0.5;
        [Fact]
        public void LMStudioTemperature_Default_IsPoint5()
        {
            var s = Create();
            s.LMStudioTemperature.Should().Be(0.5, "because the default is 0.5 (Discovery.cs:112)");
        }

        // Source line 121: CustomFilterPatterns { get; set; } = string.Empty;
        // Proof: grep -n "CustomFilterPatterns" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   121:        public string CustomFilterPatterns { get; set; } = string.Empty;
        [Fact]
        public void CustomFilterPatterns_Default_IsEmpty()
        {
            var s = Create();
            s.CustomFilterPatterns.Should().BeEmpty(
                "because the default is string.Empty (Discovery.cs:121)");
        }

        // Source line 125: EnableStrictValidation { get; set; }
        // Proof: grep -n "EnableStrictValidation" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   125:        public bool EnableStrictValidation { get; set; }
        [Fact]
        public void EnableStrictValidation_Default_IsFalse()
        {
            var s = Create();
            s.EnableStrictValidation.Should().BeFalse(
                "because bool defaults to false (Discovery.cs:125)");
        }

        // Source line 129: EnableDebugLogging { get; set; }
        // Proof: grep -n "EnableDebugLogging" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   129:        public bool EnableDebugLogging { get; set; }
        [Fact]
        public void EnableDebugLogging_Default_IsFalse()
        {
            var s = Create();
            s.EnableDebugLogging.Should().BeFalse(
                "because bool defaults to false (Discovery.cs:129)");
        }

        // Source line 134: LogPerItemDecisions { get; set; } = true;
        // Proof: grep -n "LogPerItemDecisions" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   134:        public bool LogPerItemDecisions { get; set; } = true;
        [Fact]
        public void LogPerItemDecisions_Default_IsTrue()
        {
            var s = Create();
            s.LogPerItemDecisions.Should().BeTrue("because the default is true (Discovery.cs:134)");
        }

        #endregion

        #region Defaults — Nullable Concurrency & Throttle Caps

        // Source line 139: MaxConcurrentPerModelCloud { get; set; }
        // Proof: grep -n "MaxConcurrentPerModelCloud" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   139:        public int? MaxConcurrentPerModelCloud { get; set; }
        [Fact]
        public void MaxConcurrentPerModelCloud_Default_IsNull()
        {
            var s = Create();
            s.MaxConcurrentPerModelCloud.Should().BeNull(
                "because nullable int defaults to null (Discovery.cs:139)");
        }

        // Source line 143: MaxConcurrentPerModelLocal { get; set; }
        // Proof: grep -n "MaxConcurrentPerModelLocal" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   143:        public int? MaxConcurrentPerModelLocal { get; set; }
        [Fact]
        public void MaxConcurrentPerModelLocal_Default_IsNull()
        {
            var s = Create();
            s.MaxConcurrentPerModelLocal.Should().BeNull(
                "because nullable int defaults to null (Discovery.cs:143)");
        }

        // Source line 156: AdaptiveThrottleCloudCap { get; set; }
        // Proof: grep -n "AdaptiveThrottleCloudCap" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   156:        public int? AdaptiveThrottleCloudCap { get; set; }
        [Fact]
        public void AdaptiveThrottleCloudCap_Default_IsNull()
        {
            var s = Create();
            s.AdaptiveThrottleCloudCap.Should().BeNull(
                "because nullable int defaults to null (Discovery.cs:156)");
        }

        // Source line 180: AdaptiveThrottleLocalCap { get; set; }
        // Proof: grep -n "AdaptiveThrottleLocalCap" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   180:        public int? AdaptiveThrottleLocalCap { get; set; }
        [Fact]
        public void AdaptiveThrottleLocalCap_Default_IsNull()
        {
            var s = Create();
            s.AdaptiveThrottleLocalCap.Should().BeNull(
                "because nullable int defaults to null (Discovery.cs:180)");
        }

        #endregion

        #region Defaults — Styles & Token Budget

        // Source line 163: StyleFilters { get; set; } = Array.Empty<string>();
        // Proof: grep -n "StyleFilters" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   163:        public IEnumerable<string> StyleFilters { get; set; } = Array.Empty<string>();
        [Fact]
        public void StyleFilters_Default_IsEmpty()
        {
            var s = Create();
            s.StyleFilters.Should().NotBeNull("because it is initialized to Array.Empty<string>");
            s.StyleFilters.Should().HaveCount(0,
                "because the default is an empty array (Discovery.cs:163)");
        }

        // Source line 168: MaxSelectedStyles { get; set; } = 10;
        // Proof: grep -n "MaxSelectedStyles" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   168:        public int MaxSelectedStyles { get; set; } = 10;
        [Fact]
        public void MaxSelectedStyles_Default_Is10()
        {
            var s = Create();
            s.MaxSelectedStyles.Should().Be(10, "because the default is 10 (Discovery.cs:168)");
        }

        // Source line 172: ComprehensiveTokenBudgetOverride { get; set; }
        // Proof: grep -n "ComprehensiveTokenBudgetOverride" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   172:        public int? ComprehensiveTokenBudgetOverride { get; set; }
        [Fact]
        public void ComprehensiveTokenBudgetOverride_Default_IsNull()
        {
            var s = Create();
            s.ComprehensiveTokenBudgetOverride.Should().BeNull(
                "because nullable int defaults to null (Discovery.cs:172)");
        }

        // Source line 176: RelaxStyleMatching { get; set; } = false;
        // Proof: grep -n "RelaxStyleMatching" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   176:        public bool RelaxStyleMatching { get; set; } = false;
        [Fact]
        public void RelaxStyleMatching_Default_IsFalse()
        {
            var s = Create();
            s.RelaxStyleMatching.Should().BeFalse("because the default is false (Discovery.cs:176)");
        }

        #endregion

        #region Defaults — Safety Gates & Completion

        // Source line 186: MinConfidence { get; set; } = 0.7;
        // Proof: grep -n "MinConfidence" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   186:        public double MinConfidence { get; set; } = 0.7;
        [Fact]
        public void MinConfidence_Default_IsPoint7()
        {
            var s = Create();
            s.MinConfidence.Should().Be(0.7, "because the default is 0.7 (Discovery.cs:186)");
        }

        // Source line 191: RequireMbids { get; set; } = true;
        // Proof: grep -n "RequireMbids" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   191:        public bool RequireMbids { get; set; } = true;
        [Fact]
        public void RequireMbids_Default_IsTrue()
        {
            var s = Create();
            s.RequireMbids.Should().BeTrue("because the default is true (Discovery.cs:191)");
        }

        // Source line 197: GuaranteeExactTarget { get; set; } = false;
        // Proof: grep -n "GuaranteeExactTarget" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   197:        public bool GuaranteeExactTarget { get; set; } = false;
        [Fact]
        public void GuaranteeExactTarget_Default_IsFalse()
        {
            var s = Create();
            s.GuaranteeExactTarget.Should().BeFalse(
                "because the default is false (Discovery.cs:197)");
        }

        // Source line 203: QueueBorderlineItems { get; set; } = true;
        // Proof: grep -n "QueueBorderlineItems" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   203:        public bool QueueBorderlineItems { get; set; } = true;
        [Fact]
        public void QueueBorderlineItems_Default_IsTrue()
        {
            var s = Create();
            s.QueueBorderlineItems.Should().BeTrue("because the default is true (Discovery.cs:203)");
        }

        #endregion

        #region Defaults — Review Queue

        // Source line 213: ReviewApproveKeys { get; set; } = Array.Empty<string>();
        // Proof: grep -n "ReviewApproveKeys" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   213:        public IEnumerable<string> ReviewApproveKeys { get; set; } = Array.Empty<string>();
        [Fact]
        public void ReviewApproveKeys_Default_IsEmpty()
        {
            var s = Create();
            s.ReviewApproveKeys.Should().NotBeNull("because it is initialized to Array.Empty<string>");
            s.ReviewApproveKeys.Should().HaveCount(0,
                "because the default is an empty array (Discovery.cs:213)");
        }

        // Source line 221: ReviewSummary { get; set; } = Array.Empty<string>();
        // Proof: grep -n "ReviewSummary" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   221:        public IEnumerable<string> ReviewSummary { get; set; } = Array.Empty<string>();
        [Fact]
        public void ReviewSummary_Default_IsEmpty()
        {
            var s = Create();
            s.ReviewSummary.Should().NotBeNull("because it is initialized to Array.Empty<string>");
            s.ReviewSummary.Should().HaveCount(0,
                "because the default is an empty array (Discovery.cs:221)");
        }

        // Source line 226: EnableAutoReviewTriageActions { get; set; } = false;
        // Proof: grep -n "EnableAutoReviewTriageActions" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   226:        public bool EnableAutoReviewTriageActions { get; set; } = false;
        [Fact]
        public void EnableAutoReviewTriageActions_Default_IsFalse()
        {
            var s = Create();
            s.EnableAutoReviewTriageActions.Should().BeFalse(
                "because the default is false (Discovery.cs:226)");
        }

        // Source line 231: MaxAutoReviewActionsPerRun { get; set; } = 25;
        // Proof: grep -n "MaxAutoReviewActionsPerRun" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   231:        public int MaxAutoReviewActionsPerRun { get; set; } = 25;
        [Fact]
        public void MaxAutoReviewActionsPerRun_Default_Is25()
        {
            var s = Create();
            s.MaxAutoReviewActionsPerRun.Should().Be(25,
                "because the default is 25 (Discovery.cs:231)");
        }

        // Source line 236: ReviewActionCooldownMinutes { get; set; } = 15;
        // Proof: grep -n "ReviewActionCooldownMinutes" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   236:        public int ReviewActionCooldownMinutes { get; set; } = 15;
        [Fact]
        public void ReviewActionCooldownMinutes_Default_Is15()
        {
            var s = Create();
            s.ReviewActionCooldownMinutes.Should().Be(15,
                "because the default is 15 (Discovery.cs:236)");
        }

        // Source line 241: EnableProviderCalibration { get; set; } = true;
        // Proof: grep -n "EnableProviderCalibration" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   241:        public bool EnableProviderCalibration { get; set; } = true;
        [Fact]
        public void EnableProviderCalibration_Default_IsTrue()
        {
            var s = Create();
            s.EnableProviderCalibration.Should().BeTrue(
                "because the default is true (Discovery.cs:241)");
        }

        #endregion

        #region Defaults — Observability

        // Source line 250: ObservabilityPreview { get; set; } = Array.Empty<string>();
        // Proof: grep -n "ObservabilityPreview" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   250:        public IEnumerable<string> ObservabilityPreview { get; set; } = Array.Empty<string>();
        [Fact]
        public void ObservabilityPreview_Default_IsEmpty()
        {
            var s = Create();
            s.ObservabilityPreview.Should().NotBeNull("because it is initialized to Array.Empty<string>");
            s.ObservabilityPreview.Should().HaveCount(0,
                "because the default is an empty array (Discovery.cs:250)");
        }

        // Source line 255: ObservabilityProviderFilter { get; set; }
        // Proof: grep -n "ObservabilityProviderFilter" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   255:        public string ObservabilityProviderFilter { get; set; }
        [Fact]
        public void ObservabilityProviderFilter_Default_IsNull()
        {
            var s = Create();
            s.ObservabilityProviderFilter.Should().BeNull(
                "because string defaults to null (Discovery.cs:255)");
        }

        // Source line 259: ObservabilityModelFilter { get; set; }
        // Proof: grep -n "ObservabilityModelFilter" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   259:        public string ObservabilityModelFilter { get; set; }
        [Fact]
        public void ObservabilityModelFilter_Default_IsNull()
        {
            var s = Create();
            s.ObservabilityModelFilter.Should().BeNull(
                "because string defaults to null (Discovery.cs:259)");
        }

        // Source line 264: EnableObservabilityPreview { get; set; } = true;
        // Proof: grep -n "EnableObservabilityPreview" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   264:        public bool EnableObservabilityPreview { get; set; } = true;
        [Fact]
        public void EnableObservabilityPreview_Default_IsTrue()
        {
            var s = Create();
            s.EnableObservabilityPreview.Should().BeTrue(
                "because the default is true (Discovery.cs:264)");
        }

        #endregion

        #region Defaults — DetectedModels List

        // Source line 51: DetectedModels { get; set; } = new List<string>();
        // Proof: grep -n "DetectedModels" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   51:        public List<string> DetectedModels { get; set; } = new List<string>();
        [Fact]
        public void DetectedModels_Default_IsEmptyList()
        {
            var s = Create();
            s.DetectedModels.Should().NotBeNull("because it is initialized to new List<string>");
            s.DetectedModels.Should().HaveCount(0,
                "because the default is an empty list (Discovery.cs:51)");
        }

        #endregion

        #region BaseUrl — Computed Property

        // Source lines 44-48: BaseUrl returns OllamaUrl or LMStudioUrl based on Provider
        // Proof: grep -n "BaseUrl" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   44:        public string BaseUrl
        //   45:        {
        //   46:            get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
        [Fact]
        public void BaseUrl_WhenProviderIsOllama_ReturnsOllamaUrl()
        {
            var s = Create();
            // Constructor defaults Provider to Ollama
            s.Provider.Should().Be(AIProvider.Ollama, "because the constructor defaults to Ollama");
            s.BaseUrl.Should().Be("http://localhost:11434",
                "because BaseUrl returns OllamaUrl when Provider is Ollama (Discovery.cs:46)");
        }

        // Source line 46: get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
        [Fact]
        public void BaseUrl_WhenProviderIsLMStudio_ReturnsLMStudioUrl()
        {
            var s = Create();
            s.Provider = AIProvider.LMStudio;
            s.BaseUrl.Should().Be("http://localhost:1234",
                "because BaseUrl returns LMStudioUrl when Provider is not Ollama (Discovery.cs:46)");
        }

        // Source line 46: also covers OpenAI (non-Ollama, non-LMStudio)
        // For OpenAI, BaseUrl getter falls to LMStudioUrl since the ternary only checks Ollama
        [Fact]
        public void BaseUrl_WhenProviderIsOpenAI_ReturnsLMStudioUrl()
        {
            var s = Create();
            s.Provider = AIProvider.OpenAI;
            // The ternary only checks Ollama; everything else returns LMStudioUrl
            s.BaseUrl.Should().Be("http://localhost:1234",
                "because the ternary only distinguishes Ollama vs else (Discovery.cs:46)");
        }

        // Source line 47-48: set { /* Handled by provider-specific URLs */ }
        // Proof: grep -n "Handled by provider-specific" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   47:            set { /* Handled by provider-specific URLs */ }
        [Fact]
        public void BaseUrl_Setter_IsNoOp()
        {
            var s = Create();
            var before = s.BaseUrl;
            s.BaseUrl = "http://should-not-change";
            s.BaseUrl.Should().Be(before,
                "because the setter is a no-op comment (Discovery.cs:47)");
        }

        #endregion

        #region EnableAutoDetection — Proxy Property

        // Source lines 54-58: EnableAutoDetection proxies to AutoDetectModel
        // Proof: grep -n "EnableAutoDetection" Brainarr.Plugin/BrainarrSettings.Discovery.cs
        //   54:        public bool EnableAutoDetection
        //   55:        {
        //   56:            get => AutoDetectModel;
        //   57:            set => AutoDetectModel = value;
        //   58:        }
        [Fact]
        public void EnableAutoDetection_Get_ProxiesToAutoDetectModel()
        {
            var s = Create();
            s.EnableAutoDetection.Should().Be(s.AutoDetectModel,
                "because EnableAutoDetection proxies to AutoDetectModel (Discovery.cs:56)");
        }

        [Fact]
        public void EnableAutoDetection_Set_UpdatesAutoDetectModel()
        {
            var s = Create();
            s.EnableAutoDetection = false;
            s.AutoDetectModel.Should().BeFalse(
                "because setting EnableAutoDetection propagates to AutoDetectModel (Discovery.cs:57)");
            s.EnableAutoDetection = true;
            s.AutoDetectModel.Should().BeTrue(
                "because setting EnableAutoDetection=true propagates to AutoDetectModel (Discovery.cs:57)");
        }

        #endregion

        #region Property Setters — Round-trip Verification

        [Fact]
        public void DiscoveryMode_CanBeSetToAllValues()
        {
            var s = Create();
            foreach (DiscoveryMode mode in Enum.GetValues(typeof(DiscoveryMode)))
            {
                s.DiscoveryMode = mode;
                s.DiscoveryMode.Should().Be(mode,
                    $"because DiscoveryMode setter should accept {mode}");
            }
        }

        [Fact]
        public void SamplingStrategy_CanBeSetToAllValues()
        {
            var s = Create();
            foreach (SamplingStrategy strategy in Enum.GetValues(typeof(SamplingStrategy)))
            {
                s.SamplingStrategy = strategy;
                s.SamplingStrategy.Should().Be(strategy,
                    $"because SamplingStrategy setter should accept {strategy}");
            }
        }

        [Fact]
        public void RecommendationMode_CanBeSetToAllValues()
        {
            var s = Create();
            foreach (RecommendationMode mode in Enum.GetValues(typeof(RecommendationMode)))
            {
                s.RecommendationMode = mode;
                s.RecommendationMode.Should().Be(mode,
                    $"because RecommendationMode setter should accept {mode}");
            }
        }

        [Fact]
        public void BackfillStrategy_CanBeSetToAllValues()
        {
            var s = Create();
            foreach (BackfillStrategy strategy in Enum.GetValues(typeof(BackfillStrategy)))
            {
                s.BackfillStrategy = strategy;
                s.BackfillStrategy.Should().Be(strategy,
                    $"because BackfillStrategy setter should accept {strategy}");
            }
        }

        [Fact]
        public void StopSensitivity_CanBeSetToAllValues()
        {
            var s = Create();
            foreach (StopSensitivity sensitivity in Enum.GetValues(typeof(StopSensitivity)))
            {
                s.TopUpStopSensitivity = sensitivity;
                s.TopUpStopSensitivity.Should().Be(sensitivity,
                    $"because TopUpStopSensitivity setter should accept {sensitivity}");
            }
        }

        // Source line 121: CustomFilterPatterns setter
        [Fact]
        public void CustomFilterPatterns_CanBeSet()
        {
            var s = Create();
            s.CustomFilterPatterns = "(demo), (radio mix)";
            s.CustomFilterPatterns.Should().Be("(demo), (radio mix)",
                "because CustomFilterPatterns should round-trip (Discovery.cs:121)");
        }

        // Source line 163: StyleFilters setter
        [Fact]
        public void StyleFilters_CanBeSet()
        {
            var s = Create();
            s.StyleFilters = new[] { "Rock", "Jazz" };
            s.StyleFilters.Should().HaveCount(2,
                "because StyleFilters was set to 2 items (Discovery.cs:163)");
            s.StyleFilters.Should().Contain(new[] { "Rock", "Jazz" },
                "because those are the values we set");
        }

        // Source line 139: MaxConcurrentPerModelCloud setter
        [Fact]
        public void MaxConcurrentPerModelCloud_CanBeSet()
        {
            var s = Create();
            s.MaxConcurrentPerModelCloud = 5;
            s.MaxConcurrentPerModelCloud.Should().Be(5,
                "because we set it to 5 (Discovery.cs:139)");
        }

        // Source line 143: MaxConcurrentPerModelLocal setter
        [Fact]
        public void MaxConcurrentPerModelLocal_CanBeSet()
        {
            var s = Create();
            s.MaxConcurrentPerModelLocal = 3;
            s.MaxConcurrentPerModelLocal.Should().Be(3,
                "because we set it to 3 (Discovery.cs:143)");
        }

        // Source line 186: MinConfidence setter
        [Fact]
        public void MinConfidence_CanBeSet()
        {
            var s = Create();
            s.MinConfidence = 0.9;
            s.MinConfidence.Should().Be(0.9,
                "because we set MinConfidence to 0.9 (Discovery.cs:186)");
        }

        // Source line 51: DetectedModels setter
        [Fact]
        public void DetectedModels_CanBeSet()
        {
            var s = Create();
            s.DetectedModels = new List<string> { "model-a", "model-b" };
            s.DetectedModels.Should().HaveCount(2,
                "because we set 2 models (Discovery.cs:51)");
            s.DetectedModels[0].Should().Be("model-a", "because that is the first model we added");
            s.DetectedModels[1].Should().Be("model-b", "because that is the second model we added");
        }

        // Source line 168: MaxSelectedStyles setter
        [Fact]
        public void MaxSelectedStyles_CanBeSet()
        {
            var s = Create();
            s.MaxSelectedStyles = 5;
            s.MaxSelectedStyles.Should().Be(5,
                "because we set MaxSelectedStyles to 5 (Discovery.cs:168)");
        }

        // Source line 172: ComprehensiveTokenBudgetOverride setter
        [Fact]
        public void ComprehensiveTokenBudgetOverride_CanBeSet()
        {
            var s = Create();
            s.ComprehensiveTokenBudgetOverride = 4096;
            s.ComprehensiveTokenBudgetOverride.Should().Be(4096,
                "because we set ComprehensiveTokenBudgetOverride to 4096 (Discovery.cs:172)");
        }

        // Source line 213: ReviewApproveKeys setter
        [Fact]
        public void ReviewApproveKeys_CanBeSet()
        {
            var s = Create();
            s.ReviewApproveKeys = new[] { "key1", "key2", "key3" };
            s.ReviewApproveKeys.Should().HaveCount(3,
                "because we set 3 keys (Discovery.cs:213)");
        }

        // Source line 255: ObservabilityProviderFilter setter
        [Fact]
        public void ObservabilityProviderFilter_CanBeSet()
        {
            var s = Create();
            s.ObservabilityProviderFilter = "openai";
            s.ObservabilityProviderFilter.Should().Be("openai",
                "because we set ObservabilityProviderFilter to 'openai' (Discovery.cs:255)");
        }

        // Source line 259: ObservabilityModelFilter setter
        [Fact]
        public void ObservabilityModelFilter_CanBeSet()
        {
            var s = Create();
            s.ObservabilityModelFilter = "gpt-4o-mini";
            s.ObservabilityModelFilter.Should().Be("gpt-4o-mini",
                "because we set ObservabilityModelFilter to 'gpt-4o-mini' (Discovery.cs:259)");
        }

        #endregion
    }
}
