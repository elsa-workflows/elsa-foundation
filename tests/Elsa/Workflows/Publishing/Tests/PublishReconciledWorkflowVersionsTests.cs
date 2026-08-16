using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// Branch coverage (§2.23.2) for the publish-on-reconcile subscriber (spec 147). The handler's
/// contract: latest claim per definition, opted-in sources only, deleted never published, a
/// pre-check on the policy-resolved target slot (and only that slot) makes restarts idempotent,
/// one failing definition never stops the rest, and no exception ever escapes <c>Handle</c>
/// (Sequential delivery — a throw would fail shell activation).
/// </summary>
public sealed class PublishReconciledWorkflowVersionsTests
{
    [Fact]
    public async Task Publishes_the_latest_claimed_version_of_an_opted_in_definition()
    {
        var (v1, v2) = (Version("wf-a", "1.0.0", "ver-1"), Version("wf-a", "2.0.0", "ver-2"));
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [Definition("wf-a")], versions: [v1, v2]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0"), Claim("wf-a", "2.0.0")), CancellationToken.None);

        var request = Assert.Single(sender.Sent.OfType<PublishWorkflow>());
        Assert.Equal("ver-2", request.VersionId); // latest by SemVer sort key, one publish per definition
    }

    [Fact]
    public async Task Ignores_claims_whose_source_did_not_request_publication()
    {
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [Definition("wf-a")], versions: [Version("wf-a", "1.0.0", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0", publishRequested: false)), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Never_publishes_a_deleted_definition()
    {
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [Definition("wf-a")], versions: [Version("wf-a", "1.0.0", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0", deleted: true)), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Never_publishes_a_definition_soft_deleted_in_the_store()
    {
        // The claim says live, but the store's latest-wins soft-delete already marked it deleted.
        var deleted = Definition("wf-a");
        deleted.DeletedAt = DateTimeOffset.UtcNow;
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [deleted], versions: [Version("wf-a", "1.0.0", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Warns_and_skips_when_the_claimed_version_row_is_missing()
    {
        var logger = new CapturingLogger<PublishReconciledWorkflowVersions>();
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [Definition("wf-a")], versions: [], logger: logger);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("no matching version row"));
    }

    [Fact]
    public async Task Warns_and_skips_when_the_definition_is_missing_from_the_store()
    {
        var logger = new CapturingLogger<PublishReconciledWorkflowVersions>();
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [], versions: [], logger: logger);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Skips_when_the_slot_already_holds_an_active_publication_of_the_target_version()
    {
        // Restart idempotency (FR-007): the pre-check avoids even compiling when nothing changed.
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1")],
            slots: [new WorkflowActivationSlot("slot-1", "wf-a", "default", "pub-1", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)],
            references: [Reference("pub-1", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Publishes_when_the_active_publication_is_for_an_older_version()
    {
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1"), Version("wf-a", "2.0.0", "ver-2")],
            slots: [new WorkflowActivationSlot("slot-1", "wf-a", "default", "pub-1", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)],
            references: [Reference("pub-1", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "2.0.0")), CancellationToken.None);

        var request = Assert.Single(sender.Sent.OfType<PublishWorkflow>());
        Assert.Equal("ver-2", request.VersionId);
    }

    [Fact]
    public async Task Publishes_when_only_a_non_target_slot_holds_the_target_version()
    {
        // A side-by-side 'canary' publication of the same version says nothing about the slot the
        // slot-less PublishWorkflow request updates — the default slot here has no publication at all,
        // so skipping on the canary would leave the deployment unpublished (reviewed on #1161).
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1")],
            slots: [new WorkflowActivationSlot("slot-canary", "wf-a", "canary", "pub-canary", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)],
            references: [Reference("pub-canary", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        var request = Assert.Single(sender.Sent.OfType<PublishWorkflow>());
        Assert.Equal("ver-1", request.VersionId);
    }

    [Fact]
    public async Task Publishes_when_the_target_slot_still_holds_an_older_version_than_a_non_target_slot()
    {
        // The default slot is stale at v1 while 'canary' already runs v2: the canary must not mask the
        // default slot's staleness.
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1"), Version("wf-a", "2.0.0", "ver-2")],
            slots:
            [
                new WorkflowActivationSlot("slot-1", "wf-a", "default", "pub-1", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow),
                new WorkflowActivationSlot("slot-canary", "wf-a", "canary", "pub-canary", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)
            ],
            references:
            [
                Reference("pub-1", "ver-1"),
                Reference("pub-canary", "ver-2")
            ]);

        await handler.Handle(Reconciled(Claim("wf-a", "2.0.0")), CancellationToken.None);

        var request = Assert.Single(sender.Sent.OfType<PublishWorkflow>());
        Assert.Equal("ver-2", request.VersionId);
    }

    [Fact]
    public async Task Skips_when_the_policy_resolved_slot_is_a_non_default_one_holding_the_target_version()
    {
        // A workflow policy whose default slot is 'canary' moves the target: the pre-check follows the
        // policy the publish request will be resolved against, not the literal 'default' name.
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1")],
            slots:
            [
                new WorkflowActivationSlot("slot-1", "wf-a", "default", null, null, 1, DateTimeOffset.UtcNow),
                new WorkflowActivationSlot("slot-canary", "wf-a", "canary", "pub-canary", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)
            ],
            references: [Reference("pub-canary", "ver-1")],
            policies: [new PublicationPolicy("wf-a", PublicationPolicyDefaultAction.ReplaceDefaultSlot, "canary", 1, DateTimeOffset.UtcNow)]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Sends_the_publish_request_when_the_policy_cannot_resolve_a_slot()
    {
        // An explicit-slot policy makes the pre-check unresolvable. It is an optimization, not a gate:
        // the request is still sent so PublishWorkflow raises the authoritative 'explicit_slot_required'
        // instead of the handler silently skipping (or throwing) on a policy it does not own.
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1")],
            slots: [new WorkflowActivationSlot("slot-1", "wf-a", "default", "pub-1", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)],
            references: [Reference("pub-1", "ver-1")],
            policies: [new PublicationPolicy("wf-a", PublicationPolicyDefaultAction.RequireExplicitSlot, "default", 1, DateTimeOffset.UtcNow)]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Single(sender.Sent.OfType<PublishWorkflow>());
    }

    [Fact]
    public async Task Publishes_when_the_slots_publication_is_retired_rather_than_active()
    {
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1")],
            slots: [new WorkflowActivationSlot("slot-1", "wf-a", "default", "pub-1", WorkflowActivationSource.Publishing, 1, DateTimeOffset.UtcNow)],
            references: [Reference("pub-1", "ver-1", retired: true)]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Single(sender.Sent.OfType<PublishWorkflow>());
    }

    [Fact]
    public async Task A_failing_definition_is_logged_and_does_not_stop_the_others()
    {
        var logger = new CapturingLogger<PublishReconciledWorkflowVersions>();
        var sender = new SpySender { FailFor = "ver-a" };
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a"), Definition("wf-b")],
            versions: [Version("wf-a", "1.0.0", "ver-a"), Version("wf-b", "1.0.0", "ver-b")],
            logger: logger);

        // Must not throw (Sequential delivery: an escape would fail the reconcile pass).
        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0"), Claim("wf-b", "1.0.0")), CancellationToken.None);

        Assert.Contains(sender.Sent.OfType<PublishWorkflow>(), r => r.VersionId == "ver-b");
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("wf-a"));
    }

    [Fact]
    public async Task Cancellation_of_the_provided_token_propagates()
    {
        // Host shutdown is not a per-definition failure: the pass must observe cancellation instead
        // of the handler swallowing it and continuing to publish during teardown.
        using var cts = new CancellationTokenSource();
        var sender = new SpySender { FailFor = "ver-a", FailWith = new OperationCanceledException(cts.Token) };
        var handler = NewHandler(sender, definitions: [Definition("wf-a")], versions: [Version("wf-a", "1.0.0", "ver-a")]);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), cts.Token));
    }

    [Fact]
    public async Task An_OperationCanceledException_without_a_cancelled_token_is_isolated_like_any_failure()
    {
        // A dependency throwing OCE on its own (token not cancelled) is an operational failure, not a
        // shutdown signal — the catch-all fallback keeps shell activation alive (reviewed on #1161).
        var logger = new CapturingLogger<PublishReconciledWorkflowVersions>();
        var sender = new SpySender { FailFor = "ver-a", FailWith = new OperationCanceledException() };
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a"), Definition("wf-b")],
            versions: [Version("wf-a", "1.0.0", "ver-a"), Version("wf-b", "1.0.0", "ver-b")],
            logger: logger);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0"), Claim("wf-b", "1.0.0")), CancellationToken.None);

        Assert.Contains(sender.Sent.OfType<PublishWorkflow>(), r => r.VersionId == "ver-b");
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("wf-a"));
    }

    [Fact]
    public async Task No_claims_means_no_work()
    {
        var sender = new SpySender();
        var handler = NewHandler(sender, definitions: [], versions: []);

        await handler.Handle(Reconciled(), CancellationToken.None);

        Assert.Empty(sender.Sent);
    }

    private static PublishReconciledWorkflowVersions NewHandler(
        SpySender sender,
        IReadOnlyList<WorkflowDefinition> definitions,
        IReadOnlyList<WorkflowDefinitionVersion> versions,
        IReadOnlyList<WorkflowActivationSlot>? slots = null,
        IReadOnlyList<WorkflowExecutableSourceReference>? references = null,
        IReadOnlyList<PublicationPolicy>? policies = null,
        CapturingLogger<PublishReconciledWorkflowVersions>? logger = null) =>
        new(
            logger ?? new CapturingLogger<PublishReconciledWorkflowVersions>(),
            new StubDefinitionStore(definitions),
            new StubVersionStore(versions),
            new StubPolicyStore(policies ?? []),
            new PublicationPolicyResolver(),
            new StubActivationAuthority(slots ?? []),
            new StubSourceReferenceStore(references ?? []),
            sender,
            TimeProvider.System);

    private static WorkflowVersionsReconciled Reconciled(params WorkflowVersionSourceClaim[] claims) => new(claims);

    private static WorkflowVersionSourceClaim Claim(string definitionId, string version, bool publishRequested = true, bool deleted = false) =>
        new(definitionId, version, SemVer.ToSortKey(version), "test-source", "Json", publishRequested, deleted);

    private static WorkflowDefinition Definition(string id) => new() { Id = id, Name = id };

    private static WorkflowDefinitionVersion Version(string definitionId, string version, string versionId) =>
        new(definitionId, version) { Id = versionId };

    /// <summary>
    /// The live source reference the coordinator mints for <paramref name="activationId"/>. FR-B-006 makes the
    /// slot plus this reference — not the publication journal — the answer to "what is this slot serving?", so
    /// that is what the pre-check is seeded with.
    /// </summary>
    private static WorkflowExecutableSourceReference Reference(string activationId, string versionId, bool retired = false) =>
        new(
            SourceReferenceId: WorkflowActivationReferenceIdentity.Create(activationId),
            ArtifactId: "artifact-x",
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: versionId,
            SourceVersion: null,
            DefinitionId: "wf-a",
            DefinitionVersionId: versionId,
            ArtifactVersion: "1.0.0",
            CreatedAt: DateTimeOffset.UtcNow,
            PublishedAt: DateTimeOffset.UtcNow,
            Scope: WorkflowExecutableReferenceScope.Published,
            DeletedAt: retired ? DateTimeOffset.UtcNow : null,
            DeletedReason: retired ? "activation-replaced" : null,
            ActivationId: activationId,
            SlotId: "slot-1");

    private sealed class SpySender : IRequestSender
    {
        public List<object> Sent { get; } = [];
        public string? FailFor { get; init; }
        public Exception? FailWith { get; init; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is PublishWorkflow publish && publish.VersionId == FailFor)
                throw FailWith ?? new InvalidOperationException($"Simulated publish failure for '{publish.VersionId}'.");

            Sent.Add(request);
            if (request is PublishWorkflow p)
                return Task.FromResult((T)(object)View(p.VersionId));
            throw new NotSupportedException("Only PublishWorkflow is exercised by these tests.");
        }

        private static PublishedWorkflowView View(string versionId) => new(
            PublicationId: "pub-new",
            DefinitionId: "wf",
            VersionId: versionId,
            DefinitionVersionId: versionId,
            ArtifactId: "artifact-new",
            SlotName: "default",
            Status: PublicationStatusView.Active,
            SourceReferenceId: "ref-1",
            CreatedAt: DateTimeOffset.UtcNow,
            ActivatedAt: DateTimeOffset.UtcNow,
            RetiredAt: null,
            ArtifactVersion: "1",
            ArtifactHash: "hash",
            RootActivityId: "root",
            NodeCount: 1,
            WasCreated: true);
    }

    private sealed class StubDefinitionStore(IReadOnlyList<WorkflowDefinition> items) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(items.FirstOrDefault(x => x.Id == id));

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(items.First(x => x.Id == id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(items);
    }

    private sealed class StubVersionStore(IReadOnlyList<WorkflowDefinitionVersion> items) : IWorkflowDefinitionVersionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(items.Where(x => x.DefinitionId == definitionId).ToList());

        private const string Unused = "Not exercised by publish-on-reconcile tests.";
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class StubPolicyStore(IReadOnlyList<PublicationPolicy> items) : IPublicationPolicyStore
    {
        public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(items.FirstOrDefault(x => x.WorkflowDefinitionId == workflowDefinitionId));

        public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(PublicationPolicy policy, long expectedRevision, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Not exercised by publish-on-reconcile tests.");
    }

    private sealed class StubActivationAuthority(IReadOnlyList<WorkflowActivationSlot> items) : IWorkflowActivationAuthority
    {
        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(items.FirstOrDefault(x => x.WorkflowDefinitionId == workflowDefinitionId && x.SlotName == slotName));

        // The pre-check is slot-targeted: enumerating every slot of the definition would be the
        // slot-agnostic behaviour this handler must not have.
        private const string Unused = "Not exercised by publish-on-reconcile tests.";
        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class StubSourceReferenceStore(IReadOnlyList<WorkflowExecutableSourceReference> items) : IWorkflowExecutableSourceReferenceStore
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(items.FirstOrDefault(x => x.SourceReferenceId == sourceReferenceId));

        // The pre-check resolves the slot's reference by its deterministic id and reads nothing else. Every other
        // member throwing is the assertion that it stays that way.
        private const string Unused = "Not exercised by publish-on-reconcile tests.";
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
