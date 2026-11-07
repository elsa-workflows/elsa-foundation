using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Feature", "RunHistory")]
    public async Task History_composes_before_or_after_EF_with_one_actual_reader_observation_and_retention_boundary(bool historyFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccessor("tenant-a"));
        services.AddWorkflowRuntime();
        if (historyFirst) services.AddRuntimeHistory();
        services.AddRuntimeEntityFrameworkCore(HistoryEfOptions());
        if (!historyFirst) services.AddRuntimeHistory();
        var snapshot = services.ToArray();
        services.AddRuntimeEntityFrameworkCore(HistoryEfOptions());
        Assert.Equal(snapshot, services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync();
        await SeedHistoryRunAsync(context);
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var observations = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryObservationWriter>();
        var retention = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryRetentionStore>();
        var checkpoints = scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        Assert.IsType<EfRuntimeHistoryStore>(reader); Assert.IsType<EfRuntimeHistoryStore>(observations); Assert.IsType<EfRuntimeHistoryStore>(retention);
        Assert.True(observations.CanRecordHistory(reader)); Assert.True(checkpoints.CanCommitHistory(reader));
        Assert.True(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().IsSupported);
        await checkpoints.CommitAsync(HistoryCommit("registration", true, [HistoryCandidate("checkpoint-registration", 0, "WorkflowStarted")]), Decision());
        var e = new RuntimeHistoryEvent("registration-delivery", "workflow-a", 0, RuntimeHistoryEventCategory.OperationalLifecycle, "StimulusReceived", OccurredAt)
        { Authority = RuntimeHistoryAuthority.DeliveryObservation };
        Assert.True((await observations.RecordAsync(new(HistoryPolicy, e))).Recorded);
        Assert.Equal(2, (await reader.FindHeadAsync("workflow-a"))!.LastSequence);
        Assert.Equal(2, await retention.PurgeThroughAsync("workflow-a", 2));
        Assert.Empty((await reader.ReadPageAsync(new("workflow-a"))).Items);
    }

    [Theory]
    [InlineData(typeof(IRuntimeHistoryStore))]
    [InlineData(typeof(IRuntimeHistoryObservationWriter))]
    [InlineData(typeof(IRuntimeHistoryRetentionStore))]
    [Trait("Feature", "RunHistory")]
    public void EF_history_refuses_unowned_history_factories_without_mutating_service_registrations(Type contract)
    {
        var services = new ServiceCollection(); services.AddWorkflowRuntime(); services.AddRuntimeHistory();
        foreach (var descriptor in services.Where(x => x.ServiceType == contract).ToArray()) services.Remove(descriptor);
        ((IServiceCollection)services).Add(ServiceDescriptor.Scoped(contract, _ => throw new InvalidOperationException("An unowned factory must never be invoked during composition.")));
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeEntityFrameworkCore(HistoryEfOptions()));
        Assert.Equal(before, services);
    }
    private static RuntimeEntityFrameworkCoreOptions HistoryEfOptions() => new()
    {
        Provider = "Sqlite", ConnectionString = "Data Source=:memory:",
        RecoveryContinuationSigningKey = "run-history-registration-signing-key-32",
        HierarchyCursorSigningKey = "run-history-registration-signing-key-32"
    };
}
