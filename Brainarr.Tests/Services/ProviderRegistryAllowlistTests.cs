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
            var providerAssembly = typeof(IAIProvider).Assembly;
            var enumCount = Enum.GetValues(typeof(AIProvider)).Length;
            var concreteCount = GetConcreteProviderTypes(providerAssembly).Count;

            enumCount.Should().Be(concreteCount,
                $"AIProvider enum has {enumCount} values but there are {concreteCount} concrete IAIProvider types. " +
                "These must stay in sync.");
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
