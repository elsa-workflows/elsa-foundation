using Elsa.Workflows.Design.Reconciliation.Contracts;
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
    public void ConfigureServices_WithoutSourceId_Throws()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options = { FilePath = "catalog.json" },
        };

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
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

    [Fact]
    public void ConfigureServices_WithFolderPath_RegistersSource()
    {
        var feature = new JsonWorkflowReconciliationFeature
        {
            Options = { SourceId = "catalog", FolderPath = "/defs" },
        };

        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowReconciliationSource));
    }

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
