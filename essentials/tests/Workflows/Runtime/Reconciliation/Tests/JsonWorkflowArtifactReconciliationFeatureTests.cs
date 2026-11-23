using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

public sealed class JsonWorkflowArtifactReconciliationFeatureTests
{
    [Fact]
    public void Multiple_json_features_keep_their_own_source_options()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IWorkflowArtifactClosureReader, UnusedClosureReader>();

        ConfigureSource(services, "catalog-a", "/mounts/a/artifacts.json");
        ConfigureSource(services, "catalog-b", "/mounts/b/artifacts.json");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IWorkflowArtifactReconciliationSource>().ToArray();

        Assert.Equal(["catalog-a", "catalog-b"], sources.Select(source => source.SourceId));
    }

    private static void ConfigureSource(IServiceCollection services, string sourceId, string filePath) =>
        new JsonWorkflowArtifactReconciliationFeature
        {
            Options =
            {
                SourceId = sourceId,
                FilePath = filePath,
            },
        }.ConfigureServices(services);

    private sealed class UnusedClosureReader : IWorkflowArtifactClosureReader
    {
        public WorkflowArtifactClosure Read(string filePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This registration test does not read closure files.");
    }
}
