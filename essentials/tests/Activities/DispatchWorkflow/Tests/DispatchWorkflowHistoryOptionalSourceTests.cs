using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowHistoryOptionalSourceTests
{
    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task The_native_source_owner_requires_optional_access_only_for_a_retained_v5_decision(int version)
    {
        var registrations = new ServiceCollection();
        registrations.AddWorkflowRuntimeHistory(new() { Enabled = false });
        var featureRegistrations = new ServiceCollection(); new DispatchWorkflowRuntimeFeature().ConfigureServices(featureRegistrations);
        var executions = new InMemoryWorkflowExecutionStateStore();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IWorkflowExecutionStateStore>(executions);
        // Resolve the actual feature's owner and the actual default source extension, without unrelated
        // publication/partition services that independently require access. No synthetic source factory.
        services.Add(registrations.Single(item => item.ServiceType == typeof(IRuntimeHistorySourceExtension)));
        services.Add(featureRegistrations.Single(item => item.ServiceType == typeof(IRuntimeCheckpointCommitEnricher) &&
            item.ImplementationType?.Name == "WorkflowDispatchHistoryEnricher"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var at = DateTimeOffset.UnixEpoch;
        var pin = new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash");
        await executions.SaveAsync(new("parent", pin, WorkflowExecutionStatus.Running, null, at, at, at, null, null, null, null,
            new Dictionary<string, string>()) { HistoryCapturePolicy = version == 0 ? null : new("runtime-history-v" + version,
                version, true, ["ChildWorkflowDispatchRequested", "ChildStartAttemptObserved", "ChildStartDispositionObserved"]) });
        var identity = new WorkflowDispatchIdentity("parent", "activity");
        var edge = new WorkflowDispatchRecord(identity.DispatchId, "parent", "activity", identity.ChildWorkflowExecutionId, pin,
            new("reference", "WorkflowDefinitionVersion", "version", "1", "definition", "version", "1", "publication", "slot"),
            WorkflowDispatchMode.FireAndForget, WorkflowDispatchStatus.Pending, null, null, new(PersistenceScope.DefaultValue),
            WorkflowRunKind.PublishedRun, new("caller", "initiator"), [], at, at);
        var commit = new RuntimeCheckpointCommit("commit", new("checkpoint", "ActivityCompleted", "parent", at, ["activity"],
            new Dictionary<string, string>()), new(null, null, [], [], [], [], [], workflowDispatches:
            [new(edge.DispatchId, RuntimeStateChangeOperation.Upsert, edge, new Dictionary<string, string>())]),
            [new(identity.StartIntentId, "parent", WorkflowDispatchLifecycle.StartChildIntentKind, at, "activity", null, null)], new Dictionary<string, string>());
        var owner = scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitEnricher>();
        Assert.Null(scope.ServiceProvider.GetService<IPersistenceAccessContextAccessor>());
        if (version == 5) await Assert.ThrowsAsync<NotSupportedException>(() => owner.EnrichAsync(commit).AsTask());
        else
        {
            var result = await owner.EnrichAsync(commit);
            Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(commit), RuntimeCheckpointCommitFingerprint.Compute(result)); Assert.Null(result.HistorySourceFacts); Assert.Null(result.PostCommitIntents.Single().HistoryCausation);
        }
    }
}
