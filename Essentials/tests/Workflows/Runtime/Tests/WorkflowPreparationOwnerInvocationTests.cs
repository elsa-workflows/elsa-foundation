using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowPreparationOwnerInvocationTests
{
    [Fact]
    public async Task A_captured_session_is_invalid_after_the_actual_issuer_callback()
    {
        var lease = new Lease();
        WorkflowPreparationOwnerSession? captured = null;
        await WorkflowPreparationOwnerInvocation.ExecuteAsync(new Issuer(lease), new Participant(), lease.Owner,
            (session, _) => { captured = session; session.EnsureActive(); return ValueTask.CompletedTask; }, default);
        Assert.NotNull(captured);
        Assert.Throws<InvalidOperationException>(() => captured.EnsureActive());
    }

    [Fact]
    public async Task The_participant_can_join_before_current_owner_validation()
    {
        var lease = new Lease();
        await WorkflowPreparationOwnerInvocation.ExecuteAsync(new Issuer(lease), new Participant(), lease.Owner,
            async (session, token) =>
            {
                session.EnsureActive();
                Assert.Throws<InvalidOperationException>(() => session.Lease.EnsureCurrent());
                await session.Lease.JoinAsync(new CommitFrame(session), token);
                session.Lease.EnsureCurrent();
            }, default);
        Assert.True(lease.Joined);
    }

    [Fact]
    public async Task A_repeated_issuer_callback_cannot_create_a_second_session()
    {
        var lease = new Lease();
        var entries = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WorkflowPreparationOwnerInvocation.ExecuteAsync(new Issuer(lease, twice: true), new Participant(), lease.Owner,
                (_, _) => { entries++; return ValueTask.CompletedTask; }, default));
        Assert.Equal(1, entries);
    }

    [Fact]
    public async Task A_different_canonical_owner_is_refused_before_the_operation()
    {
        var lease = new Lease();
        var entered = false;
        await Assert.ThrowsAsync<WorkflowPreparationException>(async () =>
            await WorkflowPreparationOwnerInvocation.ExecuteAsync(new Issuer(lease), new Participant(),
                lease.Owner with { LedgerIncarnation = "recreated" },
                (_, _) => { entered = true; return ValueTask.CompletedTask; }, default));
        Assert.False(entered);
    }

    private sealed class Issuer(Lease lease, bool twice = false) : IWorkflowPreparationOwnerIssuer
    {
        public async ValueTask ExecuteFencedAsync(WorkflowPreparationOwnerStamp expected,
            IWorkflowPreparationCommitParticipant participant,
            Func<IWorkflowPreparationOwnerLease, CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            await write(lease, cancellationToken);
            if (twice) await write(lease, cancellationToken);
        }
    }
    private sealed class Participant : IWorkflowPreparationCommitParticipant
    {
        public ValueTask<WorkflowPreparationWriteResult> ExecuteAsync(WorkflowPreparationOwnerSession owner,
            Func<IWorkflowPreparationCommitContext, CancellationToken, ValueTask<WorkflowPreparationWriteResult>> stage,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class CommitFrame(WorkflowPreparationOwnerSession owner) : IWorkflowPreparationCommitContext
    {
        public WorkflowPreparationOwnerSession Owner => owner;
        public void EnsureActive() => owner.EnsureActive();
    }
    private sealed class Lease : IWorkflowPreparationOwnerLease
    {
        public bool Joined { get; private set; }
        public WorkflowPreparationOwnerStamp Owner { get; } = new(new(null, "owner", "operation"), "ledger", 1, "Prepared",
            "request", "snapshot", "source", "source-profile", "compiler", "prepared", "content");
        public WorkflowPreparationDecision? Decision => throw new NotSupportedException();
        public IReadOnlyList<WorkflowPreparationRoot> SelectedRoots => throw new NotSupportedException();
        public ValueTask JoinAsync(IWorkflowPreparationCommitContext commit, CancellationToken cancellationToken = default)
        { Joined = true; return ValueTask.CompletedTask; }
        public void EnsureCurrent()
        {
            if (!Joined) throw new InvalidOperationException("Native Join has not succeeded.");
        }
    }
}
