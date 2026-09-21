using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowTestScopeTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CreatedAt.AddHours(1);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Scope_rejects_blank_identity(string scopeId)
    {
        Assert.Throws<ArgumentException>(() => NewScope(scopeId: scopeId));
    }

    [Fact]
    public void Scope_identity_is_bounded_for_portable_persistence_indexes()
    {
        var tooLong = new string('s', WorkflowTestScope.MaximumScopeIdLength + 1);

        Assert.Throws<ArgumentException>(() => NewScope(scopeId: tooLong));
        Assert.Throws<ArgumentException>(() => new WorkflowTestScopeCloseRequest(
            tooLong,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowTestScopePageQuery(ExpiresAt, 10, ContinuationToken: tooLong).Validate());
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchQuery(testScopeId: tooLong));
    }

    [Fact]
    public void Scope_requires_a_finite_expiry_and_partition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowTestScope(
            "scope-1",
            default,
            "tenant-1",
            new WorkflowExecutionPartition("partition-1")));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewScope(expiresAt: DateTimeOffset.MaxValue));
        Assert.Throws<ArgumentNullException>(() => new WorkflowTestScope("scope-1", ExpiresAt, null, null!));
    }

    [Fact]
    public void Scope_context_is_immutable_and_round_trips()
    {
        var scope = NewScope(tenantId: "tenant-1", partition: "partition-1");

        var restored = JsonSerializer.Deserialize<WorkflowTestScope>(JsonSerializer.Serialize(scope));

        Assert.NotNull(restored);
        Assert.True(WorkflowTestScope.ContextEquals(scope, restored));
        Assert.Equal("tenant-1", restored.TenantId);
        Assert.Equal("partition-1", restored.Partition.Value);
    }

    [Fact]
    public void Equality_at_expiry_is_expired()
    {
        var scope = NewScope();

        Assert.False(scope.IsExpired(ExpiresAt.AddTicks(-1)));
        Assert.True(scope.IsExpired(ExpiresAt));
        Assert.True(scope.IsExpired(ExpiresAt.AddTicks(1)));
    }

    [Fact]
    public void Equivalent_create_is_idempotent_but_conflicting_context_fails_closed()
    {
        var requested = NewScope();
        var created = WorkflowTestScopeTransitions.Create(null, requested, CreatedAt);

        Assert.Same(created, WorkflowTestScopeTransitions.Create(created, NewScope(), CreatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => WorkflowTestScopeTransitions.Create(
            created,
            NewScope(tenantId: "other-tenant"),
            CreatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => WorkflowTestScopeTransitions.Create(
            created,
            NewScope(partition: "other-partition"),
            CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Lifecycle_is_monotonic_and_equivalent_close_is_idempotent()
    {
        var open = WorkflowTestScopeTransitions.Create(null, NewScope(), CreatedAt);
        var request = new WorkflowTestScopeCloseRequest(
            open.Scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            CreatedAt.AddMinutes(10));

        var accepted = WorkflowTestScopeTransitions.Close(open, request);
        var replayed = WorkflowTestScopeTransitions.Close(accepted.Record, request);
        var closed = WorkflowTestScopeTransitions.Complete(accepted.Record!, CreatedAt.AddMinutes(11));
        var closedReplay = WorkflowTestScopeTransitions.Complete(closed, CreatedAt.AddMinutes(12));

        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, accepted.Disposition);
        Assert.Equal(WorkflowTestScopeState.Closing, accepted.Record!.State);
        Assert.Equal(WorkflowTestScopeCloseDisposition.AlreadyClosing, replayed.Disposition);
        Assert.Same(accepted.Record, replayed.Record);
        Assert.Equal(WorkflowTestScopeState.Closed, closed.State);
        Assert.Same(closed, closedReplay);
        Assert.Throws<InvalidOperationException>(() => WorkflowTestScopeTransitions.Complete(open, CreatedAt.AddMinutes(11)));
    }

    [Fact]
    public void Expired_close_requires_expiry_and_records_the_reason_once()
    {
        var open = WorkflowTestScopeTransitions.Create(null, NewScope(), CreatedAt);

        Assert.Throws<InvalidOperationException>(() => WorkflowTestScopeTransitions.Close(
            open,
            new WorkflowTestScopeCloseRequest(open.Scope.ScopeId, WorkflowTestScopeCloseReason.Expired, ExpiresAt.AddTicks(-1))));

        var accepted = WorkflowTestScopeTransitions.Close(
            open,
            new WorkflowTestScopeCloseRequest(open.Scope.ScopeId, WorkflowTestScopeCloseReason.Expired, ExpiresAt));
        var replayedWithDifferentReason = WorkflowTestScopeTransitions.Close(
            accepted.Record,
            new WorkflowTestScopeCloseRequest(open.Scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, ExpiresAt.AddMinutes(1)));

        Assert.Equal(WorkflowTestScopeCloseReason.Expired, accepted.Record!.CloseReason);
        Assert.Equal(WorkflowTestScopeCloseDisposition.AlreadyClosing, replayedWithDifferentReason.Disposition);
        Assert.Equal(WorkflowTestScopeCloseReason.Expired, replayedWithDifferentReason.Record!.CloseReason);
    }

    [Fact]
    public void Close_returns_not_found_without_disclosing_context()
    {
        var result = WorkflowTestScopeTransitions.Close(
            null,
            new WorkflowTestScopeCloseRequest("missing", WorkflowTestScopeCloseReason.ExplicitTeardown, CreatedAt));

        Assert.Equal(WorkflowTestScopeCloseDisposition.NotFound, result.Disposition);
        Assert.Null(result.Record);
    }

    private static WorkflowTestScope NewScope(
        string scopeId = "scope-1",
        DateTimeOffset? expiresAt = null,
        string? tenantId = "tenant-1",
        string partition = "partition-1") =>
        new(scopeId, expiresAt ?? ExpiresAt, tenantId, new WorkflowExecutionPartition(partition));
}
