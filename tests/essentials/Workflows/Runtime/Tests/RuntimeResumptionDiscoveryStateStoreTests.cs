using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Recovery;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeResumptionDiscoveryStateStoreTests
{
    private readonly RuntimeResumptionDiscoveryStateStore _store = new();

    /// <summary>
    /// #2188 review: eviction took the oldest insert, so with more scopes than entries a swept scope lost its walk on
    /// every sweep. It now takes the scope used least recently, and both reading and writing count as use.
    /// </summary>
    [Fact]
    public void Eviction_takes_the_scope_used_least_recently()
    {
        for (var index = 0; index < RuntimeResumptionDiscoveryStateStore.MaximumEntries; index++)
            _store.Set(Scope(index), Walked(index));

        _ = _store.Get(Scope(0));
        _store.Set(Scope(1), Walked(1));
        _store.Set("scope-new", Walked(-1));

        Assert.Equal(Position(0), _store.Get(Scope(0)).BacklogAfterWorkflowExecutionId);
        Assert.Equal(Position(1), _store.Get(Scope(1)).BacklogAfterWorkflowExecutionId);
        Assert.Null(_store.Get(Scope(2)).BacklogAfterWorkflowExecutionId);
        Assert.Equal(Position(3), _store.Get(Scope(3)).BacklogAfterWorkflowExecutionId);
        Assert.Equal(Position(-1), _store.Get("scope-new").BacklogAfterWorkflowExecutionId);
    }

    [Fact]
    public void An_unknown_scope_starts_the_walk_with_the_scanner_on_turn()
    {
        Assert.Equal(new RuntimeResumptionDiscoveryState(BacklogAfterWorkflowExecutionId: null, RecoveryHasSingleSlotTurn: true), _store.Get("scope-unknown"));
    }

    [Fact]
    public void Warns_once_per_queue_type()
    {
        Assert.True(_store.FirstWarningFor(typeof(string)));
        Assert.False(_store.FirstWarningFor(typeof(string)));
        Assert.True(_store.FirstWarningFor(typeof(int)));
        Assert.True(new RuntimeResumptionDiscoveryStateStore().FirstWarningFor(typeof(string)));
    }

    private static string Scope(int index) => $"scope-{index}";

    private static string Position(int index) => $"wfexec-{index}";

    private static RuntimeResumptionDiscoveryState Walked(int index) => new(Position(index));
}
