using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Test ports isolate actual owner inputs. They never create history batches or claim provider durability.</summary>
internal static class HistoryOwnerInputTestComposition
{
    internal static RuntimeHistoryCapturePolicy Policy { get; } = new("runtime-history-v3", 3, false,
        Enum.GetValues<RuntimeHistorySourceFactKind>().Select(RuntimeHistorySourceFacts.EventName).ToArray());

    internal static void Configure(IServiceCollection services, IWorkflowExecutionStateStore executions, IRuntimeCheckpointCommitStore checkpoints)
    {
        services.AddSingleton(executions);
        services.RemoveAll<RuntimeCheckpointCommitter>();
        services.AddWorkflowRuntime(); // Use the real lazy source handoff factory, preserving the harness's existing native stores.
        services.RemoveAll<IPersistenceAccessContextAccessor>(); // Preserve this component harness's existing unscoped native boundary.
        services.RemoveAll<IRuntimeCheckpointCommitEnricher>();
        services.AddSingleton<IRuntimeCheckpointCommitEnricher, OwnerInputCapture>();
        var reader = new UnusedReader();
        services.AddSingleton<IRuntimeHistoryStore>(reader);
        services.AddSingleton<IRuntimeCheckpointCommitStore>(new OwnerInputWriter(checkpoints, reader));
        services.AddSingleton<IRuntimeHistorySourceExtension, OwnerInputSource>();
    }

    private sealed class OwnerInputCapture : IRuntimeCheckpointCommitEnricher, IRuntimeHistoryCaptureSupport
    {
        public int Order => 2000;
        public bool CanCapture(RuntimeHistoryCapturePolicy policy) => RuntimeHistorySourceFacts.IsFinalProfile(policy);
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) => ValueTask.FromResult(commit);
    }

    private sealed class OwnerInputSource(IWorkflowExecutionStateStore executions) : IRuntimeHistorySourceExtension
    {
        public async ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
            Func<RuntimeHistoryCapturePolicy, IEnumerable<RuntimeHistorySourceFact>> facts, CancellationToken cancellationToken = default)
        {
            var policy = (await executions.FindAsync(commit.WorkflowExecutionId, cancellationToken))?.HistoryCapturePolicy;
            return RuntimeHistorySourceFacts.IsFinalProfile(policy)
                ? commit with { HistorySourceFacts = (commit.HistorySourceFacts ?? []).Concat(facts(policy!)).ToArray() }
                : commit;
        }
    }

    private sealed class OwnerInputWriter(IRuntimeCheckpointCommitStore inner, IRuntimeHistoryStore reader) : IRuntimeCheckpointCommitStore
    {
        public bool SupportsHistory => true;
        public bool CanCommitHistory(IRuntimeHistoryStore selectedStore) => ReferenceEquals(reader, selectedStore);
        public bool SupportsExactBookmarkResume => inner.SupportsExactBookmarkResume;
        public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore executions, IActivityExecutionStateStore activities) =>
            inner.CanCommitExactBookmarkResume(bookmarks, executions, activities);
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            Assert.Empty(commit.HistoryBatches ?? []); // This test adapter records native source inputs, never physical history.
            return inner.CommitAsync(commit, decision, cancellationToken);
        }
    }

    private sealed class UnusedReader : IRuntimeHistoryStore
    {
        public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryPage> ReadPageAsync(RuntimeHistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryBookmarkPage> ListBookmarksAsync(RuntimeHistoryBookmarkQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
