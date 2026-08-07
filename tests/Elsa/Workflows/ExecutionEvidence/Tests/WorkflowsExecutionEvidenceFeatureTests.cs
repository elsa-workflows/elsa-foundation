using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.ExecutionEvidence.Tests;

public sealed class WorkflowsExecutionEvidenceFeatureTests : IDisposable
{
    private readonly ServiceProvider _provider = Build(new());

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Feature_registers_the_in_memory_evidence_store_as_a_singleton()
    {
        using var scope = _provider.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IExecutionEvidenceStore>();

        Assert.IsType<InMemoryExecutionEvidenceStore>(store);
        Assert.Same(store, _provider.GetRequiredService<IExecutionEvidenceStore>());
    }

    [Fact]
    public void Feature_contributes_the_checkpoint_enricher_to_the_runtime_enricher_set()
    {
        using var scope = _provider.CreateScope();

        var enrichers = scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>();

        Assert.Single(enrichers, enricher => enricher is ExecutionEvidenceCheckpointEnricher);
    }

    [Fact]
    public void Feature_registers_the_documented_default_buffer_bounds()
    {
        // These are the numbers the feature's own [ManifestSetting] DefaultValue metadata advertises, so an operator
        // reading the manifest and a host resolving the options must agree on them exactly.
        var options = _provider.GetRequiredService<ExecutionEvidenceOptions>();

        Assert.Equal(10_000, options.MaxRecordsPerWorkflow);
        Assert.Equal(1_000, options.MaxWorkflows);
        Assert.Equal(10_000, options.MaxTrackedCheckpointsPerWorkflow);
        Assert.Equal(8_192, options.MaxInlineValueLength);
        Assert.True(options.RedactSensitiveValues);
    }

    [Fact]
    public void Feature_settings_flow_into_the_registered_options()
    {
        using var provider = Build(new()
        {
            MaxRecordsPerWorkflow = 11,
            MaxWorkflows = 22,
            MaxTrackedCheckpointsPerWorkflow = 33,
            MaxInlineValueLength = 44,
            RedactSensitiveValues = false
        });

        var options = provider.GetRequiredService<ExecutionEvidenceOptions>();

        Assert.Equal(11, options.MaxRecordsPerWorkflow);
        Assert.Equal(22, options.MaxWorkflows);
        Assert.Equal(33, options.MaxTrackedCheckpointsPerWorkflow);
        Assert.Equal(44, options.MaxInlineValueLength);
        Assert.False(options.RedactSensitiveValues);
    }

    [Fact]
    public void The_registered_store_honours_the_configured_bounds()
    {
        // Options reaching the container is not the same as the buffer obeying them: the store is constructed with a
        // captured instance, so this pins the whole path from a manifest setting to observable trimming.
        using var provider = Build(new() { MaxRecordsPerWorkflow = 1 });
        var store = provider.GetRequiredService<IExecutionEvidenceStore>();

        store.Append(EvidenceFactory.Batch("cp-1"));
        store.Append(EvidenceFactory.Batch("cp-2"));

        var page = store.List(EvidenceFactory.WorkflowId, 0);

        Assert.Equal(["cp-2"], page.Records.Select(record => record.CheckpointId));
        Assert.Equal(2, page.FirstSequence);
    }

    private static ServiceProvider Build(WorkflowsExecutionEvidenceFeature feature)
    {
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        return services.BuildServiceProvider();
    }
}
