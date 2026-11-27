using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Handlers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// #6070: a reconciled definition without a draft gets one cloned from the newest version the pass reconciled, so
/// Studio can open it; one with a draft keeps it; nodes share the clone key; a failure never escapes the handler.
/// </summary>
public sealed class CloneDraftsOfReconciledWorkflowsTests
{
    [Fact]
    public async Task Clones_a_draft_from_the_newest_reconciled_version_of_a_definition_without_one()
    {
        var clone = new SpyClone();
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-1"), Version("wf-a", "2.0.0", "ver-2")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0"), Claim("wf-a", "2.0.0")), CancellationToken.None);

        var (key, versionId) = Assert.Single(clone.Calls);
        Assert.Equal("ver-2", versionId);
        Assert.Equal(WorkflowReconciliationOperationKeys.DraftClone("wf-a", SemVer.ToSortKey("2.0.0")), key);
    }

    [Fact]
    public async Task Clones_whether_or_not_the_source_asked_for_publication()
    {
        // An import-only source still gives the author something to open and publish.
        var clone = new SpyClone();
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0", publishRequested: false)), CancellationToken.None);

        Assert.Single(clone.Calls);
    }

    [Fact]
    public async Task Keeps_the_draft_a_definition_already_has()
    {
        var clone = new SpyClone();
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-1")], drafts: [Draft("draft-1", "wf-a")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(clone.Calls);
    }

    [Fact]
    public async Task Never_clones_for_a_deleted_definition()
    {
        var clone = new SpyClone();
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-1")]);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0", deleted: true)), CancellationToken.None);

        Assert.Empty(clone.Calls);
    }

    [Fact]
    public async Task Warns_and_skips_when_the_claimed_version_row_is_missing()
    {
        var clone = new SpyClone();
        var logger = new ListLogger();
        var handler = NewHandler(clone, versions: [], logger: logger);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Empty(clone.Calls);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("no matching version row"));
    }

    [Fact]
    public async Task A_failing_clone_is_logged_and_does_not_stop_the_others()
    {
        // Sequential delivery inside the reconcile startup task: an escape would fail shell activation.
        var clone = new SpyClone { FailFor = "ver-a" };
        var logger = new ListLogger();
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-a"), Version("wf-b", "1.0.0", "ver-b")], logger: logger);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0"), Claim("wf-b", "1.0.0")), CancellationToken.None);

        Assert.Contains(clone.Calls, call => call.VersionId == "ver-b");
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("wf-a"));
    }

    [Fact]
    public async Task Cancellation_of_the_provided_token_propagates()
    {
        using var cts = new CancellationTokenSource();
        var clone = new SpyClone { FailFor = "ver-a", FailWith = new OperationCanceledException(cts.Token) };
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-a")]);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), cts.Token));
    }

    [Fact]
    public async Task Reports_a_replayed_clone_of_a_discarded_draft_without_cloning_again()
    {
        // The key replays the earlier clone; its draft is gone, so the definition stays without one until the source
        // brings a newer version.
        var clone = new SpyClone { Returns = "discarded-draft" };
        var logger = new ListLogger();
        var handler = NewHandler(clone, versions: [Version("wf-a", "1.0.0", "ver-1")], logger: logger);

        await handler.Handle(Reconciled(Claim("wf-a", "1.0.0")), CancellationToken.None);

        Assert.Single(clone.Calls);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("was discarded"));
    }

    [Fact]
    public async Task No_claims_means_no_work()
    {
        var clone = new SpyClone();
        var handler = NewHandler(clone, versions: []);

        await handler.Handle(Reconciled(), CancellationToken.None);

        Assert.Empty(clone.Calls);
    }

    private static CloneDraftsOfReconciledWorkflows NewHandler(
        SpyClone clone,
        IReadOnlyList<WorkflowDefinitionVersion> versions,
        IReadOnlyList<WorkflowDefinitionDraft>? drafts = null,
        ListLogger? logger = null)
    {
        var draftStore = new StubDraftStore(drafts ?? []);
        clone.Drafts = draftStore;
        return new(draftStore, new StubVersionStore(versions), clone, logger ?? new ListLogger());
    }

    private static WorkflowVersionsReconciled Reconciled(params WorkflowVersionSourceClaim[] claims) => new(claims);

    private static WorkflowVersionSourceClaim Claim(string definitionId, string version, bool publishRequested = true, bool deleted = false) =>
        new(definitionId, version, SemVer.ToSortKey(version), "test-source", "Json", publishRequested, deleted);

    private static WorkflowDefinitionVersion Version(string definitionId, string version, string versionId) =>
        new(definitionId, version) { Id = versionId };

    private static WorkflowDefinitionDraft Draft(string id, string definitionId) =>
        new() { Id = id, WorkflowDefinitionId = definitionId };

    private sealed class SpyClone : ICloneDraftFromVersionCommand
    {
        public List<(DesignOperationKey Key, string VersionId)> Calls { get; } = [];
        public string? FailFor { get; init; }
        public Exception? FailWith { get; init; }

        /// <summary>The draft id a replay answers with; null clones a new draft into <see cref="Drafts"/>.</summary>
        public string? Returns { get; init; }

        public StubDraftStore Drafts { get; set; } = null!;

        public Task<string> Execute(DesignOperationKey operationKey, string sourceVersionId, CancellationToken cancellationToken = default)
        {
            if (sourceVersionId == FailFor)
                throw FailWith ?? new InvalidOperationException($"Simulated clone failure for '{sourceVersionId}'.");

            Calls.Add((operationKey, sourceVersionId));
            if (Returns is not null)
                return Task.FromResult(Returns);

            var draftId = $"draft-of-{sourceVersionId}";
            Drafts.Items.Add(new WorkflowDefinitionDraft { Id = draftId, WorkflowDefinitionId = "cloned" });
            return Task.FromResult(draftId);
        }
    }

    private sealed class StubDraftStore(IReadOnlyList<WorkflowDefinitionDraft> initial) : IWorkflowDefinitionDraftStore
    {
        public List<WorkflowDefinitionDraft> Items { get; } = [.. initial];

        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == draftId));

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.WorkflowDefinitionId == workflowDefinitionId));

        private const string Unused = "Not exercised by the draft-clone tests.";
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class StubVersionStore(IReadOnlyList<WorkflowDefinitionVersion> items) : IWorkflowDefinitionVersionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(items.Where(x => x.DefinitionId == definitionId).ToList());

        private const string Unused = "Not exercised by the draft-clone tests.";
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    }

    private sealed class ListLogger : ILogger<CloneDraftsOfReconciledWorkflows>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
