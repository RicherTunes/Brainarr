using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Calibration parameters for adjusting provider-specific confidence scores
    /// to a common scale. Applies: calibrated = clamp(raw * Scale + Bias, 0, 1).
    /// </summary>
    internal sealed record ProviderCalibrationProfile(
        string ProviderName,
        double Scale,
        double Bias,
        double QualityTier)
    {
        /// <summary>Default profile for uncalibrated or unknown providers.</summary>
        public static readonly ProviderCalibrationProfile Default =
            new("Unknown", Scale: 1.0, Bias: 0.0, QualityTier: 0.5);

        /// <summary>
        /// Apply calibration to a raw confidence score.
        /// Returns a value clamped to [0.0, 1.0].
        /// </summary>
        public double Calibrate(double rawConfidence)
        {
            return Math.Clamp(rawConfidence * Scale + Bias, 0.0, 1.0);
        }

        /// <summary>
        /// Whether this profile applies a meaningful adjustment (not identity).
        /// </summary>
        public bool IsIdentity => Math.Abs(Scale - 1.0) < 0.001 && Math.Abs(Bias) < 0.001;
    }

    /// <summary>
    /// Registry mapping each AIProvider to its calibration profile.
    /// Profiles are based on observed confidence distribution characteristics
    /// per provider family.
    /// </summary>
    internal static class ProviderCalibrationRegistry
    {
        private static readonly Dictionary<AIProvider, ProviderCalibrationProfile> Profiles = new()
        {
            // Cloud API providers — well-calibrated, large model families
            [AIProvider.OpenAI] = new("OpenAI", Scale: 1.0, Bias: 0.0, QualityTier: 0.9),
            [AIProvider.Anthropic] = new("Anthropic", Scale: 1.0, Bias: 0.0, QualityTier: 0.9),
            [AIProvider.Gemini] = new("Gemini", Scale: 0.95, Bias: 0.0, QualityTier: 0.85),
            [AIProvider.DeepSeek] = new("DeepSeek", Scale: 0.92, Bias: 0.0, QualityTier: 0.80),
            [AIProvider.Perplexity] = new("Perplexity", Scale: 0.90, Bias: 0.0, QualityTier: 0.75),
            [AIProvider.Groq] = new("Groq", Scale: 0.88, Bias: 0.02, QualityTier: 0.70),

            // Router — depends on backend model, use conservative defaults
            [AIProvider.OpenRouter] = new("OpenRouter", Scale: 0.95, Bias: 0.0, QualityTier: 0.80),

            // Local providers — smaller models, less calibrated confidence
            [AIProvider.Ollama] = new("Ollama", Scale: 0.80, Bias: 0.05, QualityTier: 0.55),
            [AIProvider.LMStudio] = new("LM Studio", Scale: 0.80, Bias: 0.05, QualityTier: 0.55),

            // Subscription providers — same backends as API equivalents
            [AIProvider.ClaudeCodeSubscription] = new("Claude Code", Scale: 1.0, Bias: 0.0, QualityTier: 0.9),
            [AIProvider.OpenAICodexSubscription] = new("OpenAI Codex", Scale: 1.0, Bias: 0.0, QualityTier: 0.9),
        };

        /// <summary>
        /// Get the calibration profile for a provider.
        /// Returns the default identity profile if provider is unknown.
        /// </summary>
        public static ProviderCalibrationProfile GetProfile(AIProvider provider)
        {
            return Profiles.TryGetValue(provider, out var profile)
                ? profile
                : ProviderCalibrationProfile.Default;
        }

        /// <summary>
        /// Get the calibration profile for a provider, or null if provider is not specified.
        /// </summary>
        public static ProviderCalibrationProfile? GetProfileOrNull(AIProvider? provider)
        {
            return provider.HasValue ? GetProfile(provider.Value) : null;
        }

        /// <summary>
        /// Get all registered provider profiles (for diagnostics/testing).
        /// </summary>
        public static IReadOnlyDictionary<AIProvider, ProviderCalibrationProfile> GetAllProfiles()
        {
            return Profiles;
        }
    }
}
