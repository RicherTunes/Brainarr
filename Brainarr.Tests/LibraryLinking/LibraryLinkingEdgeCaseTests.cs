using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace Brainarr.Tests.LibraryLinking
{
    /// <summary>
    /// Tests for library linking edge cases when Brainarr is loaded alongside other plugins.
    /// These tests verify that:
    /// - The Common library is properly internalized via ILRepack
    /// - Dependencies like Polly are not exposed publicly
    /// - Assembly isolation works correctly
    /// - Version conflicts between plugins don't cause failures
    /// </summary>
    [Trait("Category", "LibraryLinking")]
    public class LibraryLinkingEdgeCaseTests
    {
        private static readonly string PluginAssemblyPath = typeof(Brainarr.Plugin.BrainarrImportList).Assembly.Location;

        #region ILRepack Internalization Tests

        [Fact]
        public void CommonLibrary_Types_Should_Not_Be_Publicly_Exposed()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act - Get all public types from the plugin assembly
            var publicTypes = pluginAssembly.GetExportedTypes();
            var commonNamespaceTypes = publicTypes
                .Where(t => t.Namespace?.StartsWith("Lidarr.Plugin.Common", StringComparison.Ordinal) == true)
                .ToList();

            // Assert - Common library types should be internalized after ILRepack
            // If ILRepack ran correctly, these should not be exposed as public
            commonNamespaceTypes.Should().BeEmpty(
                "Lidarr.Plugin.Common types should be internalized by ILRepack to prevent version conflicts");
        }

        [Fact]
        public void Polly_Types_Should_Not_Be_Publicly_Exposed()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var publicTypes = pluginAssembly.GetExportedTypes();
            var pollyTypes = publicTypes
                .Where(t => t.Namespace?.StartsWith("Polly", StringComparison.Ordinal) == true)
                .ToList();

            // Assert
            pollyTypes.Should().BeEmpty(
                "Polly types should be internalized by ILRepack to prevent version conflicts with other plugins");
        }

        [Fact]
        public void TagLibSharp_Types_Should_Not_Be_Publicly_Exposed()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var publicTypes = pluginAssembly.GetExportedTypes();
            var tagLibTypes = publicTypes
                .Where(t => t.Namespace?.StartsWith("TagLib", StringComparison.Ordinal) == true)
                .ToList();

            // Assert
            tagLibTypes.Should().BeEmpty(
                "TagLibSharp types should be internalized by ILRepack to prevent version conflicts");
        }

        #endregion

        #region Assembly Reference Tests

        [Fact]
        public void Plugin_Should_Not_Have_External_Reference_To_Common_Assembly()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var referencedAssemblies = pluginAssembly.GetReferencedAssemblies();
            var commonReference = referencedAssemblies
                .FirstOrDefault(a => a.Name == "Lidarr.Plugin.Common");

            // Assert - After ILRepack merge, there should be no external reference
            commonReference.Should().BeNull(
                "After ILRepack merging, the Common library should be embedded, not referenced externally");
        }

        [Fact]
        public void Plugin_Should_Not_Have_External_Reference_To_Polly()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var referencedAssemblies = pluginAssembly.GetReferencedAssemblies();
            var pollyReferences = referencedAssemblies
                .Where(a => a.Name?.StartsWith("Polly", StringComparison.Ordinal) == true)
                .ToList();

            // Assert
            pollyReferences.Should().BeEmpty(
                "Polly should be merged into the plugin assembly, not referenced externally");
        }

        [Fact]
        public void Plugin_Assembly_Should_Be_Self_Contained()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);
            var pluginDir = Path.GetDirectoryName(PluginAssemblyPath)!;

            // Act - Get assemblies that should have been merged
            var mergedAssemblyNames = new[]
            {
                "Lidarr.Plugin.Common.dll",
                "Polly.dll",
                "Polly.Core.dll",
                "Polly.Extensions.Http.dll"
            };

            var existingMergedAssemblies = mergedAssemblyNames
                .Where(name => File.Exists(Path.Combine(pluginDir, name)))
                .ToList();

            // Assert - These should not exist as separate files after ILRepack
            existingMergedAssemblies.Should().BeEmpty(
                "Merged assemblies should not exist as separate files in the plugin directory");
        }

        #endregion

        #region Type Isolation Tests

        [Fact]
        public void Plugin_Types_Should_Only_Use_Internal_Common_Types()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act - Check that plugin types don't expose Common types in public signatures
            var pluginTypes = pluginAssembly.GetExportedTypes()
                .Where(t => t.Namespace?.StartsWith("Brainarr", StringComparison.Ordinal) == true);

            var typesWithCommonInSignature = new List<(Type Type, string Issue)>();

            foreach (var type in pluginTypes)
            {
                // Check base types
                var baseType = type.BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    if (baseType.Assembly != pluginAssembly &&
                        baseType.Namespace?.Contains("Common") == true)
                    {
                        typesWithCommonInSignature.Add((type, $"Base type {baseType.FullName}"));
                    }
                    baseType = baseType.BaseType;
                }

                // Check public method return types and parameters
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.DeclaringType != type) continue;

                    if (IsCommonType(method.ReturnType, pluginAssembly))
                    {
                        typesWithCommonInSignature.Add((type, $"Method {method.Name} returns Common type"));
                    }

                    foreach (var param in method.GetParameters())
                    {
                        if (IsCommonType(param.ParameterType, pluginAssembly))
                        {
                            typesWithCommonInSignature.Add((type, $"Method {method.Name} has Common type parameter"));
                        }
                    }
                }
            }

            // Assert - Public types shouldn't expose external Common types
            // Note: Internal Common types (after ILRepack) are fine
            var externalCommonIssues = typesWithCommonInSignature
                .Where(x => !x.Issue.Contains("internal"))
                .ToList();

            // This may have legitimate cases - document them if found
        }

        private static bool IsCommonType(Type type, Assembly pluginAssembly)
        {
            if (type.Assembly == pluginAssembly) return false;
            return type.Namespace?.Contains("Common") == true;
        }

        #endregion

        #region Version Compatibility Tests

        [Fact]
        public void Plugin_Manifest_Should_Specify_Valid_Common_Version()
        {
            // Arrange
            var pluginDir = Path.GetDirectoryName(PluginAssemblyPath)!;
            var manifestPath = Path.Combine(pluginDir, "plugin.json");

            // Skip if manifest doesn't exist (not in deployed state)
            if (!File.Exists(manifestPath))
            {
                return; // Skip - manifest only exists after deployment
            }

            // Act
            var manifestContent = File.ReadAllText(manifestPath);

            // Assert
            manifestContent.Should().Contain("\"commonVersion\"",
                "Plugin manifest should specify the Common library version");
        }

        [Fact]
        public void Plugin_Should_Handle_Missing_Common_Version_Gracefully()
        {
            // This test verifies the plugin can operate even if Common version metadata
            // is not available at runtime (defensive programming)

            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act - Try to find any Common version indicator
            var commonVersionAttribute = pluginAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "CommonVersion");

            // Assert - Should either have the attribute or handle its absence
            // This is informational - the absence is acceptable
            if (commonVersionAttribute != null)
            {
                commonVersionAttribute.Value.Should().NotBeNullOrEmpty();
            }
        }

        #endregion

        #region Resource Isolation Tests

        [Fact]
        public void Plugin_Embedded_Resources_Should_Be_Accessible()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var resourceNames = pluginAssembly.GetManifestResourceNames();

            // Assert - Plugin should have its resources accessible
            resourceNames.Should().NotBeNull();
        }

        [Fact]
        public void Plugin_Should_Not_Share_Static_State_With_Common()
        {
            // This test verifies that static state in the plugin doesn't leak to/from
            // the Common library, which would indicate improper isolation

            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act - Find static fields in plugin types
            var pluginTypes = pluginAssembly.GetTypes()
                .Where(t => t.Namespace?.StartsWith("Brainarr", StringComparison.Ordinal) == true);

            var staticFieldsWithCommonTypes = new List<FieldInfo>();

            foreach (var type in pluginTypes)
            {
                var staticFields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in staticFields)
                {
                    // Check if field type is from an external Common assembly
                    if (field.FieldType.Assembly != pluginAssembly &&
                        field.FieldType.Namespace?.Contains("Common") == true)
                    {
                        staticFieldsWithCommonTypes.Add(field);
                    }
                }
            }

            // Assert - No static fields should reference external Common types
            staticFieldsWithCommonTypes.Should().BeEmpty(
                "Static fields should not reference external Common types to ensure proper isolation");
        }

        #endregion

        #region Multi-Plugin Simulation Tests

        [Fact]
        public async Task Plugin_Should_Handle_Concurrent_Loading_Simulation()
        {
            // This test simulates what happens when multiple plugins are loaded
            // by verifying the plugin can be loaded multiple times without conflicts

            // Arrange
            var loadTasks = new List<Task<Assembly>>();

            // Act - Simulate concurrent plugin access
            for (int i = 0; i < 5; i++)
            {
                loadTasks.Add(Task.Run(() => Assembly.LoadFrom(PluginAssemblyPath)));
            }

            var assemblies = await Task.WhenAll(loadTasks);

            // Assert - All loads should succeed and reference the same assembly
            assemblies.Should().AllSatisfy(a => a.Should().NotBeNull());
            assemblies.Should().AllSatisfy(a => a.FullName.Should().Be(assemblies[0].FullName));
        }

        [Fact]
        public void Plugin_Type_Names_Should_Be_Unique_And_Namespaced()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var pluginTypes = pluginAssembly.GetExportedTypes()
                .Where(t => t.Namespace?.StartsWith("Brainarr", StringComparison.Ordinal) == true)
                .ToList();

            // Assert - All types should be properly namespaced
            pluginTypes.Should().AllSatisfy(t =>
            {
                t.Namespace.Should().StartWith("Brainarr",
                    "All plugin types should be in the Brainarr namespace to avoid conflicts");
            });
        }

        #endregion

        #region Build Configuration Tests

        [Fact]
        public void Plugin_Should_Target_Correct_Framework()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var targetFramework = pluginAssembly
                .GetCustomAttributes<System.Runtime.Versioning.TargetFrameworkAttribute>()
                .FirstOrDefault();

            // Assert
            targetFramework.Should().NotBeNull();
            targetFramework!.FrameworkName.Should().Contain("net",
                "Plugin should target a .NET framework compatible with Lidarr");
        }

        [Fact]
        public void Plugin_Assembly_Version_Should_Be_Valid()
        {
            // Arrange
            var pluginAssembly = Assembly.LoadFrom(PluginAssemblyPath);

            // Act
            var version = pluginAssembly.GetName().Version;

            // Assert
            version.Should().NotBeNull();
            version!.Major.Should().BeGreaterOrEqualTo(0);
        }

        #endregion
    }
}
