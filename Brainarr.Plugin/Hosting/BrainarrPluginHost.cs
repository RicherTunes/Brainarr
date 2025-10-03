using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Hosting;

/// <summary>
/// Streaming plugin entry point for the Plugin.Common bridge. This enables Brainarr to run
/// inside the modern Lidarr plugin host while reusing the same BrainarrSettings surface.
/// </summary>
public sealed class BrainarrPluginHost : StreamingPlugin<BrainarrModule, BrainarrSettings>
{
    private static readonly BrainarrSettingsValidator Validator = new();

    protected override IEnumerable<SettingDefinition> DescribeSettings()
    {
        return BrainarrSettingDefinitions.Describe();
    }

    protected override PluginValidationResult ValidateSettings(BrainarrSettings settings)
    {
        var result = Validator.Validate(settings);
        if (result.IsValid)
        {
            return PluginValidationResult.Success();
        }

        var errors = new List<string>();
        foreach (var failure in result.Errors)
        {
            if (!string.IsNullOrWhiteSpace(failure.ErrorMessage))
            {
                errors.Add(failure.ErrorMessage);
            }
        }

        return PluginValidationResult.Failure(errors);
    }

    protected override ValueTask<IIndexer?> CreateIndexerAsync(BrainarrSettings settings, IServiceProvider services, CancellationToken cancellationToken)
    {
        // Brainarr exposes an import list surface today, so the streaming plugin does not create an indexer.
        // This override keeps the bridge functional until a full indexer implementation lands.
        return ValueTask.FromResult<IIndexer?>(null);
    }
}
