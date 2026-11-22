using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Configuration;
using Elsa.Workflows.Design.Reconciliation.Json;
using Elsa.Workflows.Design.Reconciliation.Json.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Json;

/// <summary>
/// The exactly-one composition rule (a single <c>FilePath</c>, an ordered <c>Files</c> list, or a
/// scanned <c>FolderPath</c>) and the required <c>SourceId</c> are validated at registration by the
/// feature — not deferred to a property getter on the source. These tests pin that gate (mirroring
/// the Activities-side JSON feature).
/// </summary>
public sealed class JsonWorkflowReconciliationFeatureTests
{
    [Fact]
    public async Task Configured_json_sources_keep_their_own_identity_and_options()
    {
        var feature = BindAdditionalSource("secondary", "secondary.json", null, publishOnReconcile: true);
        var services = new ServiceCollection();
        services.AddLogging();
        feature.ConfigureServices(services);
        services.AddSingleton<IJsonWorkflowCatalogReader>(new CatalogReader());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IWorkflowReconciliationSource>().ToArray();

        Assert.Equal(new[] { "primary", "secondary" }, sources.Select(x => x.SourceId));
        Assert.False(sources[0].RequestsPublication);
        Assert.True(sources[1].RequestsPublication);
        var models = new List<WorkflowVersionReconciliationModel>();
        foreach (var source in sources)
            models.AddRange(await source.Read(CancellationToken.None));
        Assert.Equal(new[] { "primary.json", "secondary.json" }, models.Select(x => x.DefinitionId));
    }

    [Theory]
    [InlineData("", "secondary.json", null, "non-empty SourceId")]
    [InlineData("primary", "secondary.json", null, "distinct source identities")]
    [InlineData("secondary", null, null, "exactly one of FilePath, Files, or FolderPath")]
    [InlineData("secondary", "secondary.json", "/defs", "exactly one of FilePath, Files, or FolderPath")]
    public void Invalid_additional_source_configuration_fails_registration(string sourceId, string? filePath, string? folderPath, string expectedRule)
    {
        var feature = BindAdditionalSource(sourceId, filePath, folderPath);

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
        Assert.Contains("AdditionalSources[0]", exception.Message);
        Assert.Contains(expectedRule, exception.Message);
    }

    [Fact]
    public void Invalid_later_source_names_its_index_and_identity()
    {
        var feature = BindAdditionalSource("secondary", "secondary.json", null);
        feature.AdditionalSources = [feature.AdditionalSources.Single(), new() { SourceId = "third" }];

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
        Assert.Contains("AdditionalSources[1]", exception.Message);
        Assert.Contains("third", exception.Message);
        Assert.Contains("exactly one of FilePath, Files, or FolderPath", exception.Message);
    }

    private static JsonWorkflowReconciliationFeature BindAdditionalSource(
        string sourceId, string? filePath, string? folderPath, bool publishOnReconcile = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Options:SourceId"] = "primary",
            ["Options:FilePath"] = "primary.json",
            ["AdditionalSources:0:SourceId"] = sourceId,
            ["AdditionalSources:0:FilePath"] = filePath,
            ["AdditionalSources:0:FolderPath"] = folderPath,
            ["AdditionalSources:0:PublishOnReconcile"] = publishOnReconcile.ToString()
        }).Build();
        return configuration.Get<JsonWorkflowReconciliationFeature>()!;
    }

    private sealed class CatalogReader : IJsonWorkflowCatalogReader
    {
        public IReadOnlyList<WorkflowVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken) =>
            [new(filePath, filePath, null, "1.0.0", WorkflowDefinitionState.Empty)];
    }

    [Fact]
    public void ConfigureServices_WithoutSourceId_Throws()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options = { FilePath = "catalog.json" },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
        Assert.Contains("Options", exception.Message);
        Assert.Contains("non-empty SourceId", exception.Message);
    }

    [Fact]
    public void ConfigureServices_WithBothFilePathAndFiles_Throws()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options =
            {
                SourceId = "catalog",
                FilePath = "catalog.json",
                Files = [new JsonWorkflowReconciliationFileOption(1, "more.json")],
            },
        };

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }

    [Fact]
    public void ConfigureServices_WithNeitherFilePathNorFiles_Throws()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options = { SourceId = "catalog" },
        };

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }

    // Kept beside the composition test: conditional registration, the FilePath shape, which the composition test's FolderPath configuration does not take.
    [Fact]
    public void ConfigureServices_WithSingleFilePath_RegistersSource()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options = { SourceId = "catalog", FilePath = "catalog.json" },
        };

        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowReconciliationSource));
    }

    // Kept beside the composition test: conditional registration, the Files-list shape, which the composition test's FolderPath configuration does not take.
    [Fact]
    public void ConfigureServices_WithFilesList_RegistersSource()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options =
            {
                SourceId = "catalog",
                Files = [new JsonWorkflowReconciliationFileOption(1, "catalog.json")],
            },
        };

        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowReconciliationSource));
    }

    // Kept beside the composition test: conditional registration, the publish flag leaves the source registration unchanged.
    [Fact]
    public void ConfigureServices_WithPublishOnReconcile_StillRegistersSource()
    {
        // The flag is orthogonal to the path options; the publish step itself lives on the Publishing
        // side and only activates when a publishing feature subscribing to the reconciled event exists.
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options = { SourceId = "catalog", FolderPath = "/defs", PublishOnReconcile = true },
        };

        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowReconciliationSource));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ConfigureServices_WithFolderPathAndAnotherPathOption_Throws(bool filePath, bool files, bool folderPath)
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options =
            {
                SourceId = "catalog",
                FilePath = filePath ? "catalog.json" : null,
                Files = files ? [new JsonWorkflowReconciliationFileOption(1, "more.json")] : [],
                FolderPath = folderPath ? "/defs" : null,
            },
        };

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }
}
