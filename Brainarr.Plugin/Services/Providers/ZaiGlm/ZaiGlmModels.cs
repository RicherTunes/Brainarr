using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ZaiGlm
{
    /// <summary>
    /// Curated list of Z.AI GLM models with their string IDs and tier information.
    /// </summary>
    public static class ZaiGlmModels
    {
        /// <summary>
        /// Default model ID for Z.AI GLM (free tier).
        /// </summary>
        public const string DefaultModelId = "glm-4.7-flash";

        /// <summary>
        /// Free tier models - no cost, good for testing and light usage.
        /// </summary>
        public static class FreeTier
        {
            public const string Glm47Flash = "glm-4.7-flash";
        }

        /// <summary>
        /// Economy tier models - low cost, good balance of quality and price.
        /// </summary>
        public static class EconomyTier
        {
            public const string Glm47FlashX = "glm-4.7-flashx";
            public const string Glm46VFlashX = "glm-4.6v-flashx";  // Vision capable
        }

        /// <summary>
        /// Standard tier models - better quality, moderate cost.
        /// </summary>
        public static class StandardTier
        {
            public const string Glm45Air = "glm-4.5-air";
            public const string Glm45 = "glm-4.5";
        }

        /// <summary>
        /// Premium tier models - highest quality, higher cost.
        /// </summary>
        public static class PremiumTier
        {
            public const string Glm46 = "glm-4.6";
            public const string Glm47 = "glm-4.7";  // Flagship model
        }

        /// <summary>
        /// All available model IDs in order of recommendation (cheapest/fastest first).
        /// </summary>
        public static readonly IReadOnlyList<string> AllModelIds = new[]
        {
            // Free tier
            FreeTier.Glm47Flash,
            // Economy tier
            EconomyTier.Glm47FlashX,
            EconomyTier.Glm46VFlashX,
            // Standard tier
            StandardTier.Glm45Air,
            StandardTier.Glm45,
            // Premium tier
            PremiumTier.Glm46,
            PremiumTier.Glm47
        };

        /// <summary>
        /// Validates if a model ID is in the curated list.
        /// </summary>
        /// <param name="modelId">The model ID to validate.</param>
        /// <returns>True if the model is recognized.</returns>
        public static bool IsValidModelId(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return false;

            var normalized = modelId.Trim().ToLowerInvariant();
            foreach (var known in AllModelIds)
            {
                if (known.Equals(normalized, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Maps UI enum or legacy values to raw model ID.
        /// </summary>
        /// <param name="input">UI label, enum name, or raw model ID.</param>
        /// <returns>The normalized raw model ID.</returns>
        public static string ToRawId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return DefaultModelId;

            var trimmed = input.Trim();
            var lower = trimmed.Replace('_', '-').ToLowerInvariant();

            // Map UI enum names to raw IDs
            return trimmed switch
            {
                "Glm47_Flash" => FreeTier.Glm47Flash,
                "Glm47_FlashX" => EconomyTier.Glm47FlashX,
                "Glm46V_FlashX" => EconomyTier.Glm46VFlashX,
                "Glm45_Air" => StandardTier.Glm45Air,
                "Glm45" => StandardTier.Glm45,
                "Glm46" => PremiumTier.Glm46,
                "Glm47" => PremiumTier.Glm47,
                // Raw IDs pass through
                _ when lower.StartsWith("glm-4.7-flash") && !lower.Contains("flashx") => FreeTier.Glm47Flash,
                _ when lower.StartsWith("glm-4.7-flashx") => EconomyTier.Glm47FlashX,
                _ when lower.StartsWith("glm-4.6v-flashx") => EconomyTier.Glm46VFlashX,
                _ when lower.StartsWith("glm-4.5-air") => StandardTier.Glm45Air,
                _ when lower == "glm-4.5" => StandardTier.Glm45,
                _ when lower == "glm-4.6" => PremiumTier.Glm46,
                _ when lower == "glm-4.7" => PremiumTier.Glm47,
                // Pass through unknown values (custom models)
                _ => trimmed
            };
        }
    }
}
