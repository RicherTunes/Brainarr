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

            // Sanity: the gap should be small and explainable. As of wave 4d the only allowed
            // enum-without-local-class is ClaudeCodeCli; this guards against accidental
            // unbounded drift.
            (enumCount - concreteCount).Should().BeLessOrEqualTo(1,
                "only ClaudeCodeCli is currently expected to lack a brainarr-local IAIProvider class. " +
                "If you've added another common-backed provider, update this test.");
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
