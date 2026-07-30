using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json;
using Elsa.Workflows.Design.Reconciliation.Json.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Json;

/// <summary>
/// The either/or composition rule (a single <c>FilePath</c> XOR an ordered <c>Files</c> list) and the
/// required <c>SourceId</c> are validated at registration by the feature — not deferred to a property
/// getter on the source. These tests pin that gate (mirroring the Activities-side JSON feature).
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
}
