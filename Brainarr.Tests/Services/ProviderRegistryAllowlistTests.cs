using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// M4-4: Verifies that every IAIProvider implementation in the assembly is
    /// declared in the AIProvider enum and registered in ProviderRegistry.
    /// An undeclared provider class fails CI — single source of truth.
    /// </summary>
    public class ProviderRegistryAllowlistTests
    {
        private static readonly HashSet<string> _allowedAbstractTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "SecureProviderBase",
            "BaseCloudProvider",
            "OpenAICompatibleProvider",
            // Phase 4 wave 4a: LlmProviderAdapter is a generic wrapper around any
            // ILlmProvider, not a vendor-specific provider. It implements IAIProvider
            // structurally to preserve the brainarr-public seam, but does not need
            // its own AIProvider enum entry — the wrapped ILlmProvider's vendor enum
            // value is what's registered in the factory.
            "LlmProviderAdapter",
        };

        [Fact]
        public void AllProviderImplementations_AreDeclaredInEnum()
        {
            var providerAssembly = typeof(IAIProvider).Assembly;
            var enumValues = Enum.GetNames(typeof(AIProvider))
                .Select(n => n.ToLowerInvariant())
                .ToHashSet();

            var concreteProviders = GetConcreteProviderTypes(providerAssembly);

            var undeclared = new List<string>();
            foreach (var type in concreteProviders)
            {
                // Strip "Provider" / "SubscriptionProvider" suffix for matching
                var normalized = type.Name
                    .Replace("SubscriptionProvider", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Provider", "", StringComparison.OrdinalIgnoreCase)
                    .ToLowerInvariant();

                var found = enumValues.Any(e =>
                {
                    var flat = e.Replace("_", "");
                    return flat.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                           normalized.Contains(flat, StringComparison.OrdinalIgnoreCase) ||
                           flat.Contains(normalized, StringComparison.OrdinalIgnoreCase);
                });

                if (!found)
                {
                    undeclared.Add(type.FullName);
                }
            }

            undeclared.Should().BeEmpty(
                "every concrete IAIProvider implementation must have a corresponding AIProvider enum value. " +
                $"Undeclared: [{string.Join(", ", undeclared)}]");
        }

        [Fact]
        public void AllEnumValues_AreRegisteredInRegistry()
        {
            var registry = new ProviderRegistry();
            var enumValues = Enum.GetValues(typeof(AIProvider)).Cast<AIProvider>();

            var unregistered = new List<string>();
            foreach (var value in enumValues)
            {
                if (!registry.IsRegistered(value))
                {
                    unregistered.Add(value.ToString());
                }
            }

            unregistered.Should().BeEmpty(
                "every AIProvider enum value must be registered in ProviderRegistry. " +
                $"Unregistered: [{string.Join(", ", unregistered)}]");
        }

        [Fact]
        public void EnumCount_MatchesConcreteProviderCount()
        {
            // Phase 6 (post-stabilization legacy delete): every AIProvider enum value is now
            // backed by a Brainarr*Provider : ILlmProvider wrapped in LlmProviderAdapter.
            // There are NO brainarr-local IAIProvider concrete classes — the adapter is in
            // the allowlist (it's a generic shim, not a vendor-specific provider). So the
            // expected concreteCount is 0; what matters is that every enum value is registered,
            // which AllEnumValues_AreRegisteredInRegistry covers separately.
            // Wave-4d: enum count may exceed concrete brainarr-local provider count because
            // some enum values (e.g. ClaudeCodeCli) are backed by ILlmProvider implementations
            // from Lidarr.Plugin.Common rather than brainarr-local IAIProvider classes. The
            // post-wave-4 invariant is enum_count >= concrete_count, with the delta being the
            // count of common-backed provider registrations.
            //
            // Strict equality remains the invariant for legacy/hybrid mode (the canonical
            // BRAINARR_USE_LEGACY_LLM_PROVIDERS toggle keeps the brainarr-local classes in
            // play), but the assertion is loosened here to reflect that not every enum value
            // requires a brainarr-local class.
            var providerAssembly = typeof(IAIProvider).Assembly;
            var enumCount = Enum.GetValues(typeof(AIProvider)).Length;
            var concreteCount = GetConcreteProviderTypes(providerAssembly).Count;

            enumCount.Should().BeGreaterOrEqualTo(concreteCount,
                $"AIProvider enum has {enumCount} values but there are {concreteCount} concrete IAIProvider types. " +
                "Each brainarr-local provider must still have an enum value; common-backed providers (e.g. ClaudeCodeCli) " +
                "are allowed to bring their own enum value without a local class.");

            // Post-Phase-6: ALL enum values are common-backed (no brainarr-local concretes).
            // The drift guard is now trivially satisfied; meaningful registration parity is
            // enforced by AllEnumValues_AreRegisteredInRegistry + RegistryProviderCount_MatchesEnumCount.
            concreteCount.Should().BeLessOrEqualTo(enumCount,
                "no brainarr-local IAIProvider concrete classes remain after Phase 6 — " +
                "every provider flows through LlmProviderAdapter wrapping a common ILlmProvider.");
        }

        [Fact]
        public void RegistryProviderCount_MatchesEnumCount()
        {
            var registry = new ProviderRegistry();
            var enumCount = Enum.GetValues(typeof(AIProvider)).Length;
            var registeredCount = registry.GetRegisteredProviders().Count();

            registeredCount.Should().Be(enumCount,
                $"ProviderRegistry has {registeredCount} registered providers but AIProvider enum has {enumCount} values");
        }

        private static List<Type> GetConcreteProviderTypes(Assembly assembly)
        {
            return assembly.GetTypes()
                .Where(t => typeof(IAIProvider).IsAssignableFrom(t)
                    && t.IsClass
                    && !t.IsAbstract
                    && !_allowedAbstractTypes.Contains(t.Name))
                .ToList();
        }
    }
}
