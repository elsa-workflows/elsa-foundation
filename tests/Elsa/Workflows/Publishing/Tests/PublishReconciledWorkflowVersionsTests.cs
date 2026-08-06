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
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// Branch coverage (§2.23.2) for the publish-on-reconcile subscriber (spec 147). The handler's
/// contract: latest claim per definition, opted-in sources only, deleted never published, slot
/// pre-check makes restarts idempotent, one failing definition never stops the rest, and no
/// exception ever escapes <c>Handle</c> (Sequential delivery — a throw would fail shell activation).
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
            slots: [new PublicationSlot("slot-1", "wf-a", "default", "pub-1", 1, DateTimeOffset.UtcNow)],
            records: [Record("pub-1", "wf-a", "ver-1", PublicationStatus.Active)]);

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
            slots: [new PublicationSlot("slot-1", "wf-a", "default", "pub-1", 1, DateTimeOffset.UtcNow)],
            records: [Record("pub-1", "wf-a", "ver-1", PublicationStatus.Active)]);

        await handler.Handle(Reconciled(Claim("wf-a", "2.0.0")), CancellationToken.None);

        var request = Assert.Single(sender.Sent.OfType<PublishWorkflow>());
        Assert.Equal("ver-2", request.VersionId);
    }

    [Fact]
    public async Task Publishes_when_the_slots_publication_is_retired_rather_than_active()
    {
        var sender = new SpySender();
        var handler = NewHandler(
            sender,
            definitions: [Definition("wf-a")],
            versions: [Version("wf-a", "1.0.0", "ver-1")],
            slots: [new PublicationSlot("slot-1", "wf-a", "default", "pub-1", 1, DateTimeOffset.UtcNow)],
            records: [Record("pub-1", "wf-a", "ver-1", PublicationStatus.Retired)]);

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
        IReadOnlyList<PublicationSlot>? slots = null,
        IReadOnlyList<PublicationRecord>? records = null,
        CapturingLogger<PublishReconciledWorkflowVersions>? logger = null) =>
        new(
            logger ?? new CapturingLogger<PublishReconciledWorkflowVersions>(),
            new StubDefinitionStore(definitions),
            new StubVersionStore(versions),
            new StubSlotStore(slots ?? []),
            new StubRecordStore(records ?? []),
            sender);

    private static OnWorkflowVersionsReconciled Reconciled(params WorkflowVersionSourceClaim[] claims) => new(claims);

    private static WorkflowVersionSourceClaim Claim(string definitionId, string version, bool publishRequested = true, bool deleted = false) =>
        new(definitionId, version, SemVer.ToSortKey(version), "test-source", "Json", publishRequested, deleted);

    private static WorkflowDefinition Definition(string id) => new() { Id = id, Name = id };

    private static WorkflowDefinitionVersion Version(string definitionId, string version, string versionId) =>
        new(definitionId, version) { Id = versionId };

    private static PublicationRecord Record(string publicationId, string definitionId, string versionId, PublicationStatus status) =>
        new(publicationId, "slot-1", definitionId, versionId, "artifact-x", null, 1, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);

    private sealed class SpySender : IRequestSender
    {
        public List<object> Sent { get; } = [];
        public string? FailFor { get; init; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is PublishWorkflow publish && publish.VersionId == FailFor)
                throw new InvalidOperationException($"Simulated publish failure for '{publish.VersionId}'.");

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

    private sealed class StubSlotStore(IReadOnlyList<PublicationSlot> items) : IPublicationSlotStore
    {
        public ValueTask<IReadOnlyCollection<PublicationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<PublicationSlot>>(items.Where(x => x.WorkflowDefinitionId == workflowDefinitionId).ToList());

        private const string Unused = "Not exercised by publish-on-reconcile tests.";
        public ValueTask<PublicationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<PublicationSlotTransitionResult> TryActivateAsync(string workflowDefinitionId, string slotName, string publicationId, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<PublicationSlotTransitionResult> TryUnpublishAsync(string workflowDefinitionId, string slotName, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class StubRecordStore(IReadOnlyList<PublicationRecord> items) : IPublicationRecordStore
    {
        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(items.FirstOrDefault(x => x.PublicationId == publicationId));

        private const string Unused = "Not exercised by publish-on-reconcile tests.";
        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
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
