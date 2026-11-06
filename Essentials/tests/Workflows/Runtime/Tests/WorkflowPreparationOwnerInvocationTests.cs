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
    public async Task Current_owner_is_checked_again_after_the_operation()
    {
        var lease = new Lease();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WorkflowPreparationOwnerInvocation.ExecuteAsync(new Issuer(lease), new Participant(), lease.Owner,
                (_, _) => { lease.Current = false; return ValueTask.CompletedTask; }, default));
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
    private sealed class Lease : IWorkflowPreparationOwnerLease
    {
        public bool Current { get; set; } = true;
        public WorkflowPreparationOwnerStamp Owner { get; } = new(new(null, "owner", "operation"), "ledger", 1, "Prepared",
            "request", "snapshot", "source", "source-profile", "compiler", "prepared", "content");
        public WorkflowPreparationDecision? Decision => throw new NotSupportedException();
        public IReadOnlyList<WorkflowPreparationRoot> SelectedRoots => throw new NotSupportedException();
        public ValueTask JoinAsync(IWorkflowPreparationCommitContext commit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void EnsureCurrent()
        {
            if (!Current) throw new InvalidOperationException("The canonical owner guard ended.");
        }
    }
}
