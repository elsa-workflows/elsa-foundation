using System.Net.Http.Json;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class RuntimeMinimalApiBehaviorTests
{
    [Theory]
    [InlineData("pending")]
    [InlineData("delivered")]
    [InlineData("missing")]
    [InlineData("unavailable")]
    [InlineData("corrupt")]
    [InlineData("bounded")]
    [InlineData("advanced")]
    [InlineData("child")]
    [InlineData("child-corrupt")]
    [InlineData("wrong-kind")]
    public async Task Owner_source_coverage_uses_bounded_kind_pages_and_exact_physical_sources(string scenario)
    {
        var child = scenario.StartsWith("child", StringComparison.Ordinal);
        var corrupt = scenario is "corrupt" or "child-corrupt" or "wrong-kind";
        var nativeKind = child ? "ChildWorkflowSettled" : "StimulusPublicationCommitted";
        var source = new HistorySource
        {
            CapturePolicy = new("runtime-history-v3", 3, true,
                child ? ["ChildWorkflowSettled", "DeliveryReceived", "DeliveryDispatched"] :
                    ["StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved"]),
            DeliverySource = new RuntimeHistoryEvent("source-event", "wf-1", 1, RuntimeHistoryEventCategory.DeliveryObservation,
                nativeKind, DateTimeOffset.UnixEpoch,
                metadata: child ? new Dictionary<string, string> { ["history.relatedWorkflowExecutionId"] = "parent-run" } : null)
                { Authority = RuntimeHistoryAuthority.CommittedTransition, Causation = new(WorkflowExecutionId: "wf-1", DeliveryId: "source") },
            MoreSourceHistory = scenario == "bounded",
            AdvanceAfterSourceInspection = scenario == "advanced"
        };
        var lookup = new HistorySourceLookup(scenario);
        await using var host = await HistoryHostAsync(source, nativeSources: lookup);

        var page = await host.Client.GetFromJsonAsync<RuntimeHistoryPageView>(HistoryPath);

        Assert.NotNull(page);
        Assert.Equal(corrupt ? 0 : 1, page.UpperSequence);
        var query = Assert.Single(source.SourceQueries);
        Assert.Equal(("wf-1", nativeKind, 100), (query.WorkflowExecutionId, query.Name, query.Limit));
        Assert.False(string.IsNullOrWhiteSpace(query.AccessBinding));
        Assert.Equal(scenario == "bounded" ? 0 : 1, lookup.ExactReads);
        if (corrupt)
        {
            Assert.Equal("invalid", page.Coverage.State);
            Assert.Empty(page.Items);
        }
        else if (scenario == "pending")
        {
            Assert.Equal("pending", page.Coverage.State);
            Assert.True(page.Coverage.SourceHistoryPending);
            Assert.NotEmpty(page.Items);
        }
        else if (scenario is "delivered" or "child")
        {
            Assert.Equal("captured", page.Coverage.State);
            Assert.False(page.Coverage.SourceHistoryPending);
            Assert.Null(page.Coverage.SourceHistoryLimitation);
        }
        else
        {
            Assert.Equal("partial", page.Coverage.State);
            Assert.Null(page.Coverage.SourceHistoryPending);
            Assert.NotNull(page.Coverage.SourceHistoryLimitation);
            Assert.NotEmpty(page.Items);
            if (scenario == "advanced") Assert.Equal(2, page.Coverage.LastSequence);
        }
    }

    private sealed class HistorySourceLookup(string scenario) : IPostCommitOutboxLookupStore
    {
        internal int ExactReads;
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Ordinary or overlay source lookup cannot prove delivery history responsibility.");
        public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default)
        {
            Assert.Equal("source", outboxItemId); ExactReads++;
            if (scenario == "unavailable") throw new IOException("private-source-sentinel");
            if (scenario == "missing") return ValueTask.FromResult<RuntimePostCommitOutboxItem?>(null);
            var targetRun = scenario == "child" ? "parent-run" : scenario == "child-corrupt" ? "foreign-parent" : "wf-1";
            var kind = scenario.StartsWith("child", StringComparison.Ordinal) ? WorkflowDispatchLifecycle.ResumeParentIntentKind
                : scenario == "wrong-kind" ? "test.unrelated" : "Elsa.Activities.Primitives.PublishStimulus";
            var intent = new RuntimePostCommitIntent("publish", targetRun, kind, DateTimeOffset.UnixEpoch, null, null, null)
                { HistoryCausation = new(scenario == "corrupt" ? "wrong-event" : "source-event", "wf-1", "source") };
            var status = scenario == "pending" ? RuntimePostCommitOutboxStatus.Pending : RuntimePostCommitOutboxStatus.Delivered;
            return ValueTask.FromResult<RuntimePostCommitOutboxItem?>(new("source", intent, status,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new(2, TimeSpan.FromSeconds(1)), 0,
                null, null, status == RuntimePostCommitOutboxStatus.Delivered ? DateTimeOffset.UnixEpoch : null, null, null));
        }
    }
}
