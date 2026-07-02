using System.Text.Json;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Hosting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LibraryHealerReadOnlyArchitectureTests
{
    private static readonly string[] ForbiddenMutationOrDecisioningTokens =
    {
        "WriteTags(",
        "SyncTags(",
        "RemoveMusicBrainzTags(",
        ".UpdateMediaInfo(",
        ".Update(",
        ".DeleteMany(",
        ".Delete(",
        "IManageCommandQueue",
        "RefreshArtistCommand",
        "BulkRefreshArtistCommand",
        "RescanFoldersCommand",
        "AlbumSearchCommand",
        "IAIProvider",
        "IProviderFactory",
        "IProviderInvoker",
        "ILibraryAwarePromptBuilder",
    };

    [Fact]
    public void HealingProductionCode_ShouldNotReferenceForbiddenMutationApis()
    {
        var root = FindRepoRoot();
        var healingDir = Path.Combine(root, "Brainarr.Plugin", "Services", "Healing");

        var files = Directory.GetFiles(healingDir, "*.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();

        var offenders = files
            .SelectMany(file => ForbiddenMutationOrDecisioningTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => file + " contains " + token))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void HealerActionRouting_ShouldNotReferenceForbiddenMutationApis()
    {
        var root = FindRepoRoot();
        var orchestrator = Path.Combine(root, "Brainarr.Plugin", "Services", "Core", "BrainarrOrchestrator.cs");
        var source = File.ReadAllText(orchestrator);
        var healerBranchStart = source.IndexOf("if (isHealerAction)", StringComparison.Ordinal);

        healerBranchStart.Should().BeGreaterThanOrEqualTo(0);

        var healerRoutingSource = ExtractBlock(source, healerBranchStart);
        healerRoutingSource.Should().Contain("_healerActionHandler.Handle");
        var offenders = ForbiddenMutationOrDecisioningTokens
            .Where(token => healerRoutingSource.Contains(token, StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void HealingProductionCode_ShouldNotUseMediaFileWriteOperations()
    {
        var root = FindRepoRoot();
        var healingDir = Path.Combine(root, "Brainarr.Plugin", "Services", "Healing");
        var forbidden = new[]
        {
            "File.Move(",
            "File.Delete(",
            "File.Copy(",
            "Directory.Move(",
            "Directory.Delete(",
            ".LastWriteTime =",
            ".LastWriteTimeUtc =",
            "SetLastWriteTime",
        };

        var offenders = Directory.GetFiles(healingDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => forbidden
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => file + " contains " + token))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void BrainarrOrchestratorFactory_ShouldRegisterHealerServices()
    {
        var services = CreateHostServices();

        BrainarrOrchestratorFactory.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        provider.GetService<LibraryHealerActionHandler>().Should().NotBeNull();
        provider.GetService<ILibraryHealerScanRunner>().Should().NotBeNull();
    }

    [Fact]
    public void BrainarrModuleProvider_ShouldRouteHealerActions_WhenHostServicesProvided()
    {
        var module = new BrainarrModule();

        using var provider = module.BuildServiceProvider(services =>
        {
            AddHostServices(services);
        });

        var orchestrator = provider.GetRequiredService<IBrainarrOrchestrator>();
        var result = orchestrator.HandleAction(
            "healer/getfindings",
            new Dictionary<string, string>(),
            new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"items\"");
        json.Should().NotContain("not available");
    }

    [Fact]
    public void Brainarr_ShouldRouteHealerActions_WhenConstructedWithHostServices()
    {
        using var brainarr = new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
            Mock.Of<IHttpClient>(),
            Mock.Of<IImportListStatusService>(),
            Mock.Of<IConfigService>(),
            Mock.Of<IParsingService>(),
            Mock.Of<IArtistService>(),
            Mock.Of<IAlbumService>(),
            Mock.Of<IMediaFileService>(),
            Mock.Of<IAudioTagService>(),
            TestLogger.CreateNullLogger());

        var result = brainarr.RequestAction(
            "healer/getfindings",
            new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"items\"");
        json.Should().NotContain("not available");
    }

    private static ServiceCollection CreateHostServices()
    {
        var services = new ServiceCollection();
        AddHostServices(services);
        return services;
    }

    private static void AddHostServices(IServiceCollection services)
    {
        services.AddSingleton(TestLogger.CreateNullLogger());
        services.AddSingleton(Mock.Of<IHttpClient>());
        services.AddSingleton(Mock.Of<IArtistService>());
        services.AddSingleton(Mock.Of<IAlbumService>());
        services.AddSingleton(Mock.Of<IMediaFileService>());
        services.AddSingleton(Mock.Of<IAudioTagService>());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Brainarr.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Brainarr.sln not found");
    }

    private static string ExtractBlock(string source, int statementStart)
    {
        var bodyStart = source.IndexOf('{', statementStart);
        bodyStart.Should().BeGreaterThan(statementStart);

        var depth = 0;
        for (var i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(statementStart, i - statementStart + 1);
                }
            }
        }

        throw new InvalidOperationException("Could not extract healer routing block");
    }
}
