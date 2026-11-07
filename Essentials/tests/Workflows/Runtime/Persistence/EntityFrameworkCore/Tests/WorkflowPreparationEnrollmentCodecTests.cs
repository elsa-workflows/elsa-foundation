using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.WorkflowPreparationEnrollmentCodec;
using Barrier = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.WorkflowPreparationEnrollmentCodec.Barrier;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Checked metadata controls only; these do not qualify native commit/target/writer exclusion.
public sealed class WorkflowPreparationEnrollmentCodecTests
{
    [Fact]
    public void Abort_round_trips_without_a_header_or_token()
    {
        var barrier = Abort(Binding());
        var restored = Read(Create(barrier), barrier.Binding.Owner.Id);
        Assert.Equal(barrier, restored);
        Assert.Null(restored.HeaderIncarnation);
        Assert.Null(restored.SelectionFingerprint);
    }

    [Fact]
    public void A_new_nonce_or_material_cannot_escape_the_same_preparation_key()
    {
        var original = Binding();
        var nonce = original with { IntentId = "different-intent" };
        var material = original with { Owner = original.Owner with { PreparedContentFingerprint = "other" } };
        Assert.Equal(Create(Abort(original)).Id, Create(Abort(nonce)).Id);
        Assert.Equal(Create(Abort(original)).Id, Create(Abort(material)).Id);
        Assert.False(SameBinding(original, nonce));
        Assert.False(SameBinding(original, material));
    }

    [Fact]
    public void Logical_revision_advances_preserve_identity_but_incarnation_and_target_do_not()
    {
        var original = Binding();
        Assert.True(SameBinding(original, original with
        {
            Owner = original.Owner with { OperationRevision = 12, OperationState = "Rejected" }
        }));
        Assert.False(SameBinding(original, original with { TargetAssociation = "other-native-target" }));
        Assert.False(SameBinding(original, original with { Owner = original.Owner with { LedgerIncarnation = "recreated" } }));
    }

    [Fact]
    public void Global_and_tenant_identity_remain_distinct()
    {
        var original = Binding();
        var tenant = original with { Owner = original.Owner with { Id = original.Owner.Id with { Scope = new("0") } } };
        Assert.NotEqual(Create(Abort(original)).Id, Create(Abort(tenant)).Id);
    }

    [Theory]
    [InlineData("formatVersion")]
    [InlineData("barrier")]
    public void Missing_required_wire_members_are_corrupt(string field)
    {
        var binding = Binding();
        var row = Create(Abort(binding));
        var json = JsonNode.Parse(row.ContentJson)!.AsObject();
        json.Remove(field);
        row.ContentJson = json.ToJsonString();
        Assert.Throws<InvalidDataException>(() => Read(row, binding.Owner.Id));
    }

    [Fact]
    public void Projection_or_partition_tampering_is_not_absence()
    {
        var binding = Binding();
        var row = Create(Abort(binding));
        row.IncarnationId = "replaced";
        Assert.Throws<InvalidDataException>(() => Read(row, binding.Owner.Id));
        row = Create(Abort(binding));
        Assert.Throws<InvalidDataException>(() => Read(row, binding.Owner.Id with { OperationId = "other" }));
    }

    [Fact]
    public void Abort_cannot_carry_selection_or_header_identity()
    {
        Assert.Throws<InvalidDataException>(() => Create(Abort(Binding()) with { HeaderIncarnation = "synthetic" }));
        Assert.Throws<InvalidDataException>(() => Create(Abort(Binding()) with { SelectionFingerprint = "synthetic" }));
    }

    [Fact]
    public void Accepted_requires_actual_header_and_selection_identity()
    {
        var barrier = new Barrier(Binding(), Outcome.Accepted, "barrier", "header", "selection");
        Assert.Equal(barrier, Read(Create(barrier), barrier.Binding.Owner.Id));
        Assert.Throws<InvalidDataException>(() => Create(barrier with { HeaderIncarnation = null }));
        Assert.Throws<InvalidDataException>(() => Create(barrier with { SelectionFingerprint = null }));
    }

    [Fact]
    public void A_terminal_header_keeps_its_original_acceptance_identity()
    {
        var binding = Binding();
        var original = Header(binding.Owner);
        var terminalOwner = binding.Owner with { OperationRevision = 4, OperationState = "Rejected" };
        var terminal = original with
        {
            State = WorkflowPreparationState.RejectedReleased,
            Token = original.Token with { Revision = 2 },
            ClosedFrom = original.Token,
            TerminalDecision = new(WorkflowPreparationDecisionKind.Rejected, terminalOwner,
                "canonical-rejection", "decision-fingerprint", original.Selection.Fingerprint, [])
        };
        var checkedTerminal = WorkflowPreparationRetentionCodec.Read(
            WorkflowPreparationRetentionCodec.Create(terminal, RuntimeArtifactEfModule.PreparationSchemaVersion), original.Id);
        var barrier = new Barrier(binding, Outcome.Accepted, "barrier", original.Token.IncarnationId, original.Selection.Fingerprint);
        RequireAcceptedHeader(barrier, checkedTerminal);
        RequireMatchingHeader(terminalOwner, checkedTerminal);
        Assert.Equal(original.Token, checkedTerminal.ClosedFrom);
        Assert.Equal(WorkflowPreparationState.RejectedReleased, checkedTerminal.State);
    }

    [Fact]
    public void A_real_predecessor_header_requires_the_same_immutable_owner_material()
    {
        var owner = Binding().Owner;
        var header = WorkflowPreparationRetentionCodec.Read(
            WorkflowPreparationRetentionCodec.Create(Header(owner), RuntimeArtifactEfModule.PreparationSchemaVersion), owner.Id);
        RequireMatchingHeader(owner with { OperationRevision = 4, OperationState = "Rejected" }, header);
        Assert.Throws<WorkflowPreparationException>(() => RequireMatchingHeader(owner with { LedgerIncarnation = "recreated" }, header));
        Assert.Throws<WorkflowPreparationException>(() => RequireMatchingHeader(owner with { PreparedContentFingerprint = "recompiled" }, header));
    }

    [Fact]
    public void Aborted_or_replaced_barriers_cannot_match_a_real_header()
    {
        var binding = Binding();
        var header = Header(binding.Owner);
        Assert.Throws<WorkflowPreparationException>(() => RequireAcceptedHeader(Abort(binding), header));
        var accepted = new Barrier(binding, Outcome.Accepted, "barrier", "other-header-incarnation", header.Selection.Fingerprint);
        Assert.Throws<WorkflowPreparationException>(() => RequireAcceptedHeader(accepted, header));
    }

    [Fact]
    public async Task Issuer_release_failure_after_a_settled_result_is_unknown()
    {
        var failure = new InvalidOperationException("issuer release failed");
        var issuer = new TestIssuer { ReleaseFailure = failure };
        var participant = new TestParticipant();
        var settlement = new WorkflowPreparationEfEnrollmentSettlementFactory(participant).Bind(issuer);
        var error = await Assert.ThrowsAsync<WorkflowPreparationOutcomeUnknownException>(() =>
            settlement.SettleAsync(issuer.Lease.Owner).AsTask());
        Assert.Same(failure, error.InnerException);
        Assert.True(participant.ReturnedResult);
    }

    [Fact]
    public async Task Current_owner_failure_after_a_settled_result_is_unknown()
    {
        var issuer = new TestIssuer();
        var participant = new TestParticipant { LoseOwnerAfterResult = true };
        var settlement = new WorkflowPreparationEfEnrollmentSettlementFactory(participant).Bind(issuer);
        var error = await Assert.ThrowsAsync<WorkflowPreparationOutcomeUnknownException>(() =>
            settlement.SettleAsync(issuer.Lease.Owner).AsTask());
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.True(participant.ReturnedResult);
    }

    [Fact]
    public async Task A_failure_before_any_settled_result_preserves_its_original_cause()
    {
        var failure = new InvalidOperationException("native operation failed before result");
        var issuer = new TestIssuer();
        var participant = new TestParticipant { Failure = failure };
        var settlement = new WorkflowPreparationEfEnrollmentSettlementFactory(participant).Bind(issuer);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            settlement.SettleAsync(issuer.Lease.Owner).AsTask());
        Assert.Same(failure, error);
        Assert.False(participant.ReturnedResult);
    }

    [Fact]
    public async Task An_existing_unknown_result_is_not_replaced_or_reclassified()
    {
        var issuer = new TestIssuer();
        var failure = new WorkflowPreparationOutcomeUnknownException(issuer.Lease.Owner.Id, new IOException("commit reply lost"));
        var participant = new TestParticipant { Failure = failure };
        var settlement = new WorkflowPreparationEfEnrollmentSettlementFactory(participant).Bind(issuer);
        var error = await Assert.ThrowsAsync<WorkflowPreparationOutcomeUnknownException>(() =>
            settlement.SettleAsync(issuer.Lease.Owner).AsTask());
        Assert.Same(failure, error);
    }

    [Fact]
    public async Task Unsupported_capability_does_not_enter_an_owner_or_report_absence()
    {
        var issuer = new TestIssuer();
        var participant = new TestParticipant { Supported = false };
        var settlement = new WorkflowPreparationEfEnrollmentSettlementFactory(participant).Bind(issuer);
        var result = await settlement.SettleAsync(issuer.Lease.Owner);
        Assert.Equal(WorkflowPreparationEfEnrollmentStatus.Unsupported, result.Status);
        Assert.Equal(0, issuer.Calls);
        Assert.Null(result.Binding);
        Assert.Null(result.Header);
        Assert.Null(result.BarrierIncarnation);
    }

    // These doubles exercise response/owner-lifetime classification only. They perform NO native
    // commit and supply no provider, target, barrier serialization or recovery acceptance evidence.
    private sealed class TestIssuer : IWorkflowPreparationOwnerIssuer
    {
        public TestLease Lease { get; } = new();
        public Exception? ReleaseFailure { get; init; }
        public int Calls { get; private set; }
        public async ValueTask ExecuteFencedAsync(WorkflowPreparationOwnerStamp expected,
            IWorkflowPreparationCommitParticipant participant,
            Func<IWorkflowPreparationOwnerLease, CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            await write(Lease, cancellationToken);
            if (ReleaseFailure is { } failure) throw failure;
        }
    }

    private sealed class TestLease : IWorkflowPreparationOwnerLease
    {
        private bool joined;
        public bool Current { get; set; } = true;
        public WorkflowPreparationOwnerStamp Owner { get; } = Binding().Owner with { OperationState = "Rejected" };
        public WorkflowPreparationDecision? Decision => throw new NotSupportedException();
        public IReadOnlyList<WorkflowPreparationRoot> SelectedRoots => throw new NotSupportedException();
        public ValueTask JoinAsync(IWorkflowPreparationCommitContext commit, CancellationToken cancellationToken = default)
        { joined = true; return ValueTask.CompletedTask; }
        public void EnsureCurrent()
        {
            if (!joined || !Current) throw new InvalidOperationException("Joined current owner required.");
        }
    }

    private sealed class TestFrame(WorkflowPreparationOwnerSession owner) : IWorkflowPreparationCommitContext
    {
        public WorkflowPreparationOwnerSession Owner => owner;
        public void EnsureActive() => owner.EnsureActive();
    }

    private sealed class TestParticipant : IWorkflowExecutableSourceReferenceWriter, IWorkflowPreparationEfEnrollmentParticipant
    {
        public bool Supported { get; init; } = true;
        public bool LoseOwnerAfterResult { get; init; }
        public Exception? Failure { get; init; }
        public bool ReturnedResult { get; private set; }
        public bool CanSettleEnrollment(IWorkflowPreparationOwnerIssuer issuer) => Supported;
        public async ValueTask<WorkflowPreparationEfEnrollmentResult> SettleEnrollmentAsync(
            WorkflowPreparationOwnerSession owner, CancellationToken cancellationToken = default)
        {
            if (Failure is { } failure) throw failure;
            await owner.Lease.JoinAsync(new TestFrame(owner), cancellationToken);
            owner.Lease.EnsureCurrent();
            ReturnedResult = true;
            if (LoseOwnerAfterResult) ((TestLease)owner.Lease).Current = false;
            return new(WorkflowPreparationEfEnrollmentStatus.Aborted,
                new(owner.Owner, "original-intent", "target-association"), "barrier");
        }
        public DbContext RuntimeContext => throw new NotSupportedException();
        public string PhysicalStoreIdentity => throw new NotSupportedException();
        public IWorkflowPreparationEfCommitContext RequireActiveContext(IWorkflowPreparationCommitContext context) => throw new NotSupportedException();
        public ValueTask<WorkflowPreparationWriteResult> ExecuteAsync(WorkflowPreparationOwnerSession owner,
            Func<IWorkflowPreparationCommitContext, CancellationToken, ValueTask<WorkflowPreparationWriteResult>> stage,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RetireAsync(string id, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference reference, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch,
            DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static WorkflowPreparationSnapshot Header(WorkflowPreparationOwnerStamp owner)
    {
        var identity = new ExecutableActivityTemplateIdentity("template", "hash");
        var selection = new WorkflowPreparationSelection(owner.SnapshotId, owner.SourceFingerprint, owner.SourceProfile,
            owner.CompilerProfile, owner.PreparedProfile, owner.PreparedContentFingerprint,
            [new WorkflowPreparationTemplateRoot(identity)],
            [new WorkflowPreparationTemplatePin(identity, "material-incarnation", "hash-incarnation", 1)], "selection");
        return new(owner.Id, selection, owner, WorkflowPreparationState.Retained,
            new(owner.Id, "real-header-incarnation", 1, selection.Fingerprint), null, null);
    }

    private static Barrier Abort(WorkflowPreparationEfEnrollmentBinding binding) =>
        new(binding, Outcome.Aborted, "permanent-barrier", null, null);

    private static WorkflowPreparationEfEnrollmentBinding Binding() => new(
        new(new(null, "source-owner", "existing-operation"), "ledger-incarnation", 3, "Prepared", "request",
            "snapshot", "source-fingerprint", "source-profile", "compiler-profile", "prepared-profile", "content-fingerprint"),
        "committed-intent", "native-association");
}
