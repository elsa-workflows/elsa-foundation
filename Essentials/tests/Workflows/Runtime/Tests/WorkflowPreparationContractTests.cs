using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowPreparationContractTests
{
    private static readonly WorkflowPreparationId Id = new(new PersistenceScope("scope-a"), "owner-address", "operation-a");
    private static readonly WorkflowExecutableIdentity Executable = new("artifact", "definition", "version", "1", "sha256:executable");
    private static readonly ExecutableActivityTemplateIdentity Template = new("template", "sha256:template");
    private static readonly WorkflowPreparationOwnerStamp Owner = new(
        Id, "ledger-incarnation", 7, "Prepared", "request-fingerprint", "snapshot", "source-fingerprint",
        "source-profile", "compiler-profile", "prepared-profile", "prepared-content-fingerprint");

    [Fact]
    public async Task Existing_reader_surface_refuses_preparation_reads_instead_of_reporting_absence()
    {
        var legacy = new LegacyReferences();
        IWorkflowExecutableSourceReferenceReader reader = legacy;
        var query = new WorkflowPreparationSelectionQuery(Id, Owner.SnapshotId, Owner.SourceFingerprint,
            Owner.SourceProfile, Owner.CompilerProfile, Owner.PreparedProfile, Owner.PreparedContentFingerprint,
            [new WorkflowPreparationTemplateRoot(Template)]);

        Assert.Null(await reader.FindAsync("ordinary-reference"));
        Assert.Equal(1, legacy.Calls);
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.InspectPreparationAsync(query).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.FindPreparationAsync(Id).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.ListPreparationPageAsync(new()).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.HasPreparationMaterialAsync(query.Roots[0]).AsTask());
        Assert.Equal(1, legacy.Calls);
    }

    [Fact]
    public async Task Existing_writer_surface_has_no_preparation_capability_or_fallback_writes()
    {
        var legacy = new LegacyReferences();
        IWorkflowExecutableSourceReferenceWriter writer = legacy;
        var issuer = new UnusedIssuer();
        var commit = new UnusedCommitContext();
        var selection = Selection();
        var retain = new WorkflowPreparationRetain(Id, selection, Owner);
        var complete = new WorkflowPreparationComplete(Id, new(Id, "retention-incarnation", 1, selection.Fingerprint), Owner);

        Assert.False(writer.CanRetainPreparations(new InMemoryWorkflowExecutableStore(),
            new InMemoryExecutableActivityTemplateStore(), issuer));
        await Assert.ThrowsAsync<NotSupportedException>(() => writer.StageRetainPreparationAsync(retain, [], commit).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => writer.StageCompletePreparationAsync(complete, [], commit).AsTask());
        Assert.Equal(0, legacy.Calls);
        Assert.Equal(0, issuer.Calls);
        Assert.Equal(0, commit.Calls);
    }

    [Fact]
    public void Typed_selection_preserves_native_model_and_template_material_and_physical_pins()
    {
        var selection = Selection();
        var native = Assert.IsType<WorkflowPreparationExecutableRoot>(selection.Roots[0]);
        var model = Assert.IsType<WorkflowPreparationExecutableRoot>(selection.Roots[1]);
        var template = Assert.IsType<WorkflowPreparationTemplateRoot>(selection.Roots[2]);
        Assert.Equal(WorkflowPreparationExecutableKind.Native, native.Kind);
        Assert.Equal(WorkflowPreparationExecutableKind.Model, model.Kind);
        Assert.Same(Executable, native.Identity);
        Assert.Same(Executable, model.Identity);
        Assert.NotEqual(native, model);
        Assert.Same(Template, template.Identity);

        var nativePin = Assert.IsType<WorkflowPreparationExecutablePin>(selection.Closure[0]);
        var modelPin = Assert.IsType<WorkflowPreparationExecutablePin>(selection.Closure[1]);
        var templatePin = Assert.IsType<WorkflowPreparationTemplatePin>(selection.Closure[2]);
        Assert.Equal(WorkflowPreparationExecutableKind.Native, nativePin.Kind);
        Assert.Equal(WorkflowPreparationExecutableKind.Model, modelPin.Kind);
        Assert.Equal("native-incarnation", nativePin.IncarnationId);
        Assert.Equal("model-incarnation", modelPin.IncarnationId);
        Assert.Same(Template, templatePin.Identity);
        Assert.Equal("template-incarnation", templatePin.IncarnationId);
        Assert.Equal("hash-claim-incarnation", templatePin.HashClaimIncarnationId);
        Assert.Equal(12, templatePin.Revision);
        Assert.Equal(Owner.PreparedContentFingerprint, selection.PreparedContentFingerprint);
        Assert.NotEqual(selection.PreparedContentFingerprint, selection.Fingerprint);
    }

    [Fact]
    public void Confirmation_and_terminal_snapshot_preserve_the_exact_tokens_and_canonical_decision()
    {
        var selection = Selection();
        var accepted = new WorkflowPreparationToken(Id, "retention-incarnation", 1, selection.Fingerprint);
        var terminal = accepted with { Revision = 2 };
        var decision = new WorkflowPreparationDecision(WorkflowPreparationDecisionKind.Published,
            Owner with { OperationRevision = 8, OperationState = "Published" }, "decision-id", "decision-fingerprint",
            selection.Fingerprint, ["published-reference"]);
        var snapshot = new WorkflowPreparationSnapshot(Id, selection, Owner, WorkflowPreparationState.Transferred,
            terminal, accepted, decision);
        var result = new WorkflowPreparationWriteResult(WorkflowPreparationWriteStatus.AlreadyReleased, terminal);
        var command = new WorkflowPreparationComplete(Id, accepted, Owner);

        Assert.Same(accepted, command.Expected);
        Assert.Same(accepted, snapshot.ClosedFrom);
        Assert.Same(terminal, snapshot.Token);
        Assert.Same(terminal, result.Confirmation);
        Assert.Same(Owner, snapshot.AcceptedOwner);
        Assert.Same(decision, snapshot.TerminalDecision);
        Assert.Equal(["published-reference"], snapshot.TerminalDecision.PublishedReferenceIds);
        Assert.Null(result.Code);
    }

    [Fact]
    public void Preparation_page_defaults_and_opaque_continuations_use_existing_runtime_limits()
    {
        var defaults = new WorkflowPreparationPageQuery();
        const string opaque = " provider-owned:\u0000/+=? \ud83d\udcc4 ";
        RuntimeStorePageRequest query = new WorkflowPreparationPageQuery(2, opaque);
        var page = new RuntimeStorePage<WorkflowPreparationSnapshot>(query, [Snapshot()], "another opaque token");

        Assert.Equal(RuntimeStorePageRequest.DefaultLimit, defaults.Limit);
        Assert.Null(defaults.ContinuationToken);
        Assert.Equal(2, query.Limit);
        Assert.Equal(opaque, query.ContinuationToken);
        Assert.Equal("another opaque token", page.NextContinuationToken);
        Assert.Single(page.Items);
        Assert.Equal(new WorkflowPreparationPageQuery(2, opaque), query);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(RuntimeStorePageRequest.MaximumLimit + 1)]
    public void Preparation_page_rejects_invalid_limits(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowPreparationPageQuery(limit));

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Preparation_page_rejects_blank_continuations(string continuation) =>
        Assert.Throws<ArgumentException>(() => new WorkflowPreparationPageQuery(1, continuation));

    [Fact]
    public void Preparation_pages_reject_oversized_stalled_empty_and_overfull_results()
    {
        var tooLong = new string('x', RuntimeStorePageRequest.MaximumContinuationTokenLength + 1);
        Assert.Throws<ArgumentException>(() => new WorkflowPreparationPageQuery(1, tooLong));
        var query = new WorkflowPreparationPageQuery(1, "current");
        var snapshot = Snapshot();
        Assert.Throws<ArgumentException>(() => new RuntimeStorePage<WorkflowPreparationSnapshot>(query, [snapshot], "current"));
        Assert.Throws<ArgumentException>(() => new RuntimeStorePage<WorkflowPreparationSnapshot>(query, [], "next"));
        Assert.Throws<ArgumentException>(() => new RuntimeStorePage<WorkflowPreparationSnapshot>(query, [snapshot, snapshot]));
        Assert.Throws<ArgumentException>(() => new RuntimeStorePage<WorkflowPreparationSnapshot>(query, [snapshot], tooLong));
    }

    [Fact]
    public void Failures_preserve_identity_and_original_cause_without_expanding_the_outer_message()
    {
        var cause = new IOException("provider-private-details");
        var failure = new WorkflowPreparationException("Unavailable", Id, cause);
        var unknown = new WorkflowPreparationOutcomeUnknownException(Id, cause);

        Assert.Equal("Unavailable", failure.Code);
        Assert.Same(Id, failure.Id);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal("Preparation retention failed (Unavailable).", failure.Message);
        Assert.Same(Id, unknown.Id);
        Assert.Same(cause, unknown.InnerException);
        Assert.Equal("Preparation retention commit outcome is unknown.", unknown.Message);
        Assert.Null(new WorkflowPreparationException("Unsupported", Id).InnerException);
    }

    [Fact]
    public void Owner_session_has_no_public_construction_and_only_the_runtime_has_friend_access()
    {
        var type = typeof(WorkflowPreparationOwnerSession);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        var constructor = Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsAssembly);
        Assert.Equal([typeof(IWorkflowPreparationOwnerLease), typeof(Action)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        var friends = type.Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName).ToArray();
        Assert.Contains("Elsa.Workflows.Runtime", friends);
        Assert.DoesNotContain(friends, name => name.Contains("Publishing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owner_session_preserves_join_gated_reads_and_refuses_every_access_after_callback_exit()
    {
        var lease = new TestOwnerLease();
        var active = true;
        var expired = new InvalidOperationException("Callback has ended.");
        // Reflection exercises the opaque boundary without granting the test assembly construction access.
        var constructor = Assert.Single(typeof(WorkflowPreparationOwnerSession)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        var session = (WorkflowPreparationOwnerSession)constructor.Invoke([lease, (Action)(() =>
        {
            if (!active) throw expired;
        })]);

        session.EnsureActive();
        Assert.Same(Owner, session.Owner);
        Assert.Same(lease, session.Lease);
        Assert.Throws<InvalidOperationException>(() => session.Decision);
        Assert.Throws<InvalidOperationException>(() => session.SelectedRoots);
        await lease.JoinAsync(new UnusedCommitContext());
        Assert.Null(session.Decision);
        Assert.Same(lease.SelectedRoots, session.SelectedRoots);

        active = false;
        var reads = lease.Reads;
        Assert.Same(expired, Assert.Throws<InvalidOperationException>(session.EnsureActive));
        Assert.Same(expired, Assert.Throws<InvalidOperationException>(() => session.Owner));
        Assert.Same(expired, Assert.Throws<InvalidOperationException>(() => session.Decision));
        Assert.Same(expired, Assert.Throws<InvalidOperationException>(() => session.SelectedRoots));
        Assert.Same(expired, Assert.Throws<InvalidOperationException>(() => session.Lease));
        Assert.Equal(reads, lease.Reads);
    }

    private static WorkflowPreparationSelection Selection() => new(
        Owner.SnapshotId, Owner.SourceFingerprint, Owner.SourceProfile, Owner.CompilerProfile,
        Owner.PreparedProfile, Owner.PreparedContentFingerprint,
        [new WorkflowPreparationExecutableRoot(WorkflowPreparationExecutableKind.Native, Executable),
            new WorkflowPreparationExecutableRoot(WorkflowPreparationExecutableKind.Model, Executable),
            new WorkflowPreparationTemplateRoot(Template)],
        [new WorkflowPreparationExecutablePin(WorkflowPreparationExecutableKind.Native, Executable, "native-incarnation"),
            new WorkflowPreparationExecutablePin(WorkflowPreparationExecutableKind.Model, Executable, "model-incarnation"),
            new WorkflowPreparationTemplatePin(Template, "template-incarnation", "hash-claim-incarnation", 12)],
        "physical-selection-fingerprint");

    private static WorkflowPreparationSnapshot Snapshot() => new(Id, Selection(), Owner,
        WorkflowPreparationState.Retained, new(Id, "retention-incarnation", 1, "physical-selection-fingerprint"), null, null);

    // Implements only the pre-preparation store surface: inherited default members must keep it source compatible.
    private sealed class LegacyReferences : IWorkflowExecutableSourceReferenceStore
    {
        public int Calls { get; private set; }
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string id, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<WorkflowExecutableSourceReference?>(null); }
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
            WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(new RuntimeStorePage<WorkflowExecutableSourceReference>(query, [])); }
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
            WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(new RuntimeStorePage<WorkflowExecutableSourceReference>(query, [])); }
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
            WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<IReadOnlyCollection<string>>([]); }
        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.CompletedTask; }
        public ValueTask<bool> RetireAsync(string id, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(false); }
        public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference reference,
            DateTimeOffset now, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(false); }
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(
            WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<IReadOnlyCollection<string>>([]); }
    }

    private sealed class UnusedIssuer : IWorkflowPreparationOwnerIssuer
    {
        public int Calls { get; private set; }
        public ValueTask ExecuteFencedAsync(WorkflowPreparationOwnerStamp expected,
            IWorkflowPreparationCommitParticipant participant,
            Func<IWorkflowPreparationOwnerLease, CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Legacy provider must not invoke the issuer."); }
    }

    private sealed class UnusedCommitContext : IWorkflowPreparationCommitContext
    {
        public int Calls { get; private set; }
        public WorkflowPreparationOwnerSession Owner
        { get { Calls++; throw new InvalidOperationException("Legacy provider must not inspect the commit frame."); } }
        public void EnsureActive()
        { Calls++; throw new InvalidOperationException("Legacy provider must not enter a commit frame."); }
    }

    private sealed class TestOwnerLease : IWorkflowPreparationOwnerLease
    {
        private readonly IReadOnlyList<WorkflowPreparationRoot> roots = [new WorkflowPreparationTemplateRoot(Template)];
        private bool joined;
        public int Reads { get; private set; }
        public WorkflowPreparationOwnerStamp Owner
        { get { Reads++; return WorkflowPreparationContractTests.Owner; } }
        public WorkflowPreparationDecision? Decision
        { get { Reads++; RequireJoined(); return null; } }
        public IReadOnlyList<WorkflowPreparationRoot> SelectedRoots
        { get { Reads++; RequireJoined(); return roots; } }
        public ValueTask JoinAsync(IWorkflowPreparationCommitContext commit, CancellationToken cancellationToken = default)
        { joined = true; return ValueTask.CompletedTask; }
        public void EnsureCurrent() => RequireJoined();
        private void RequireJoined()
        {
            if (!joined) throw new InvalidOperationException("Native Join has not succeeded.");
        }
    }
}
