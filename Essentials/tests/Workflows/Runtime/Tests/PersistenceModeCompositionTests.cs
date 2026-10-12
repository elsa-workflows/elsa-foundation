using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The replacement contracts the persistence modes touch. They take over the runtime's own placeholder policy and wrap
/// the outbox lookup a host registered, and they refuse, naming the registrations in conflict, what they would otherwise
/// replace silently or lose to: a host's own policy registered before them, and, at shell activation, a policy registered
/// after them or a second resolver.
/// </summary>
public sealed class PersistenceModeCompositionTests
{
    private readonly IServiceCollection _services = new ServiceCollection().AddWorkflowRuntime();

    [Fact]
    public void A_policy_a_host_registered_before_the_persistence_modes_is_refused_naming_both()
    {
        var registeredFirst = new ServiceCollection();
        registeredFirst.AddSingleton<IRuntimeCheckpointPersistencePolicy, HostPolicy>();
        registeredFirst.AddWorkflowRuntime();
        _services.AddSingleton<IRuntimeCheckpointPersistencePolicy, HostPolicy>();

        foreach (var services in new[] { registeredFirst, _services })
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => services.AddPersistenceModes());

            Assert.Contains(typeof(HostPolicy).FullName!, refusal.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(CoalescingRuntimeCheckpointPersistencePolicy).FullName!, refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_runtimes_own_policy_is_the_one_the_persistence_modes_replace()
    {
        _services.AddPersistenceModes();

        var policy = Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointPersistencePolicy));
        Assert.Equal(typeof(CoalescingRuntimeCheckpointPersistencePolicy), policy.ImplementationType);
    }

    [Fact]
    public async Task A_lookup_a_host_registered_keeps_answering_through_the_persistence_modes()
    {
        // A host may register the outbox lookup apart from its outbox store. The persistence modes wrap it like the
        // stores, so an item outside every session is still found where the host keeps it.
        var item = new RuntimePostCommitOutboxItem(
            "outbox-host",
            new RuntimePostCommitIntent("intent-host", "wfexec-host", "host.kind", DateTimeOffset.UnixEpoch, activityExecutionId: null, idempotencyKey: null, payload: null),
            RuntimePostCommitOutboxStatus.Pending,
            DateTimeOffset.UnixEpoch,
            availableAt: DateTimeOffset.UnixEpoch);
        _services.Replace(ServiceDescriptor.Scoped<IPostCommitOutboxLookupStore>(_ => new HostLookup(item)));
        _services.AddPersistenceModes();

        await using var provider = _services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Same(item, await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync("outbox-host"));
    }

    [Fact]
    public async Task A_clean_composition_starts_also_when_the_modes_are_added_twice_and_the_resolver_was_replaced()
    {
        _services.Replace(ServiceDescriptor.Scoped<IRuntimePersistenceModeResolver, HostResolver>());
        _services.AddPersistenceModes();
        _services.AddPersistenceModes(options => options.Mode = PersistenceMode.Durable);

        await InitializeAsync();
    }

    [Fact]
    public async Task A_policy_registered_after_the_persistence_modes_fails_activation_naming_it()
    {
        _services.AddPersistenceModes();
        _services.AddSingleton<IRuntimeCheckpointPersistencePolicy, HostPolicy>();

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(InitializeAsync);

        Assert.Contains(typeof(IRuntimeCheckpointPersistencePolicy).FullName!, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(HostPolicy).FullName!, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_persistence_mode_resolver_fails_activation_naming_both()
    {
        _services.AddPersistenceModes();
        _services.AddScoped<IRuntimePersistenceModeResolver, HostResolver>();

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(InitializeAsync);

        Assert.Contains(typeof(HostResolver).FullName!, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Workflows.Runtime.Services.Checkpoints.RuntimePersistenceModeResolver'", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Runs the shell initializers the composed services register, as shell activation does.</summary>
    private async Task InitializeAsync()
    {
        await using var provider = _services.BuildServiceProvider();
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

    private sealed class HostPolicy : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
    }

    private sealed class HostLookup(RuntimePostCommitOutboxItem item) : IPostCommitOutboxLookupStore
    {
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimePostCommitOutboxItem?>(outboxItemId == item.OutboxItemId ? item : null);
    }

    private sealed class HostResolver : IRuntimePersistenceModeResolver
    {
        public ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResolvedPersistenceMode(PersistenceMode.Durable, RunHasRecord: true));

        public PersistenceMode Resolve(WorkflowExecutable executable) => PersistenceMode.Durable;
    }
}
