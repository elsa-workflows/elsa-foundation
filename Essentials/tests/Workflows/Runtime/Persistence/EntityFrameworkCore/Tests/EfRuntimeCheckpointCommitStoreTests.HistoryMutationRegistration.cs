using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Public_native_registration_selects_one_actual_mutation_owner_in_either_default_registration_order(bool defaultsFirst)
    {
        var services = CompleteHistoryReaderServices();
        if (defaultsFirst) RuntimeHistoryRegistration.AddServices(services, new() { Enabled = false });
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        if (!defaultsFirst) RuntimeHistoryRegistration.AddServices(services, new() { Enabled = false });
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryObservationWriter)));
        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryRetentionStore)));
        using var provider = services.BuildServiceProvider(); using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var reader = first.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var observation = first.ServiceProvider.GetRequiredService<IRuntimeHistoryObservationWriter>();
        Assert.IsType<EfRuntimeHistoryMutationStore>(observation);
        Assert.Same(observation, first.ServiceProvider.GetRequiredService<IRuntimeHistoryRetentionStore>());
        Assert.Same(observation, first.ServiceProvider.GetRequiredService<IRuntimeHistoryObservationWriter>());
        Assert.NotSame(observation, second.ServiceProvider.GetRequiredService<IRuntimeHistoryObservationWriter>());
        Assert.True(observation.CanRecordHistory(reader));
        Assert.False(observation.CanRecordHistory(second.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>()));
        Assert.False(first.ServiceProvider.GetRequiredService<RuntimeHistoryOptions>().Enabled);
        Assert.False(first.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>().SupportsHistory);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped, false)] [InlineData(ServiceLifetime.Singleton, false)]
    [InlineData(ServiceLifetime.Scoped, true)] [InlineData(ServiceLifetime.Singleton, true)]
    public void Native_registration_preserves_explicit_foreign_independent_ports(ServiceLifetime lifetime, bool retention)
    {
        var services = CompleteHistoryReaderServices(); var contract = retention ? typeof(IRuntimeHistoryRetentionStore) : typeof(IRuntimeHistoryObservationWriter);
        var selected = new ServiceDescriptor(contract, _ => throw new InvalidOperationException("Foreign independent factory must not be invoked during selection."), lifetime);
        ((IServiceCollection)services).Add(selected); services.AddRuntimeCheckpointCommitEntityFrameworkCore(); services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        Assert.Same(selected, Assert.Single(services.Where(descriptor => descriptor.ServiceType == contract)));
    }

    [Fact]
    public void Foreign_selected_reader_does_not_receive_an_unpaired_ef_mutation_owner()
    {
        var services = CompleteHistoryReaderServices();
        services.AddScoped<IRuntimeHistoryStore>(_ => throw new InvalidOperationException("Foreign reader must not be invoked during selection."));
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfRuntimeHistoryMutationStore) ||
            descriptor.ServiceType == typeof(IRuntimeHistoryObservationWriter) || descriptor.ServiceType == typeof(IRuntimeHistoryRetentionStore));
    }

    [Fact]
    public async Task Public_registered_native_reader_observation_and_retention_use_the_same_actual_durable_history()
    {
        var connection = $"Data Source=file:ef-history-mutation-di-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connection); await keeper.OpenAsync();
        var services = CompleteHistoryReaderServices(connection); services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("tenant-a")));
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>(); await context.Database.EnsureCreatedAsync();
        var policy = new RuntimeHistoryCapturePolicy("native-mutation-registration", 1, true, NativeHistoryPolicy.SupportedKinds.Append("DeliveryReceived").ToArray());
        var state = Execution("workflow-a", "tenant-a") with { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
        context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
        await context.SaveChangesAsync();
        var native = NativeHistoryCommit("registered-mutation");
        native = native with { HistoryBatches = [new(policy, native.Checkpoint.CheckpointId, OccurredAt, true, native.HistoryBatches!.Single().Candidates)] };
        await scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(native, Decision());
        var observer = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryObservationWriter>();
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var retention = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryRetentionStore>();
        Assert.True(observer.CanRecordHistory(reader)); Assert.Same(observer, retention);
        var observation = new RuntimeHistoryObservation(policy, new("registered-delivery", "workflow-a", 0,
            RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryReceived", OccurredAt, "activity-a")
            { Authority = RuntimeHistoryAuthority.DeliveryObservation, OccurredAtKnown = false, BookmarkId = "history-bookmark", Causation = new(DeliveryId: "delivery") });
        var recorded = await observer.RecordAsync(observation); Assert.True(recorded.Recorded); Assert.Equal(3, recorded.Event!.Sequence);
        var head = await reader.FindHeadAsync("workflow-a"); Assert.NotNull(head); Assert.Equal(3, head.LastSequence); Assert.Equal("registered-mutation", head.LastPhysicalCommitId);
        Assert.Equal(2, await retention.PurgeThroughAsync("workflow-a", 2)); Assert.Null(await reader.FindEventAsync("workflow-a", "created"));
        Assert.Equal(3, (await reader.FindEventAsync("workflow-a", "registered-delivery"))!.Sequence);
        Assert.Equal(3, await retention.PurgeThroughAsync("workflow-a", long.MaxValue));
        var purgedReplay = await observer.RecordAsync(observation); Assert.True(purgedReplay.Recorded); Assert.Null(purgedReplay.Event);
        Assert.Single(await context.Bookmarks.AsNoTracking().ToArrayAsync()); Assert.Single(await context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
        Assert.False(scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>().SupportsHistory);
        Assert.Null(scope.ServiceProvider.GetService<RuntimeHistoryOptions>());
    }
}
