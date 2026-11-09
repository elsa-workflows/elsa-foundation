using System.Text.Json;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Test-control replacement seam for inspecting and deliberately driving active waits.</summary>
public interface ITestRunWaitOperations
{
    Task<TestRunWaitsView> GetAsync(string testRunId, CancellationToken cancellationToken);
    Task<TestRunEventView> DeliverAsync(TestRunEventRequest request, CancellationToken cancellationToken);
}

internal sealed class TestRunWaitOperations(
    IWorkflowTestRunStarter testRuns,
    IWorkflowExecutionStateStore executions,
    IWorkflowTestScopeStore scopes,
    IWorkflowExecutableStore executables,
    IBookmarkStateStore bookmarks,
    IActivityExecutionStateStore activities,
    IBookmarkResumeDispatcher resume,
    IEnumerable<ITestRunWaitDriver> drivers,
    TimeProvider time) : ITestRunWaitOperations
{
    public async Task<TestRunWaitsView> GetAsync(string testRunId, CancellationToken cancellationToken)
    {
        var execution = await ResolveExecutionAsync(testRunId, cancellationToken);
        if (execution is null || !await IsOpenAsync(execution, cancellationToken))
            return new([]);
        var executable = await executables.FindAsync(execution.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            return new([]);
        var waits = await bookmarks.ListAllBookmarkStatesAsync(execution.WorkflowExecutionId, cancellationToken);
        var result = new List<TestRunWaitView>();
        foreach (var wait in waits.Where(wait => wait.ExpiresAt is null || wait.ExpiresAt > time.GetUtcNow()))
        {
            if (!executable.NodesById.TryGetValue(wait.ExecutableNodeId, out var node))
                continue;
            var context = new TestRunWaitContext(executable, node, wait);
            var matches = drivers.Where(driver => driver.Supports(context)).ToArray();
            var label = node.Metadata.GetValueOrDefault("displayName") ?? node.AuthoredActivityId;
            result.Add(matches.Length == 1
                ? new(wait.BookmarkId, node.AuthoredActivityId, label, matches[0].DriverId, true, null, matches[0].Describe(context))
                : new(wait.BookmarkId, node.AuthoredActivityId, label, null, false,
                    matches.Length == 0 ? "No installed module supports test delivery to this wait." : "Multiple module drivers claim this wait.", []));
        }
        return new(result);
    }

    public async Task<TestRunEventView> DeliverAsync(TestRunEventRequest request, CancellationToken cancellationToken)
    {
        var execution = await ResolveExecutionAsync(request.TestRunId, cancellationToken);
        if (execution is null || !await IsOpenAsync(execution, cancellationToken))
            return new("Rejected", "This test run is missing, stopped or expired.");
        if (string.IsNullOrWhiteSpace(request.WaitId) || string.IsNullOrWhiteSpace(request.ModeId))
            return new("Rejected", "Select an active wait and a delivery mode.");
        if (!Guid.TryParseExact(request.DeliveryId, "D", out _))
            return new("Rejected", "The event delivery ID must be a UUID.");

        // The canonical payload is module-owned. A prior receipt is checked against the submitted configuration
        // digest before bookmark lookup, so retries remain safe after the wait has been consumed.
        var configurationDigest = TestRunDeliveryReceipt.Digest(JsonSerializer.SerializeToElement(new { modeId = request.ModeId, data = request.Data }));
        var receipt = new TestRunDeliveryReceipt(request.TestRunId, request.DeliveryId, request.WaitId, configurationDigest);
        if (receipt.MatchesExisting(execution) is { } matches)
            return matches ? new("AlreadyDelivered") : new("Rejected", "This delivery ID already names different data or another wait.");
        if (execution.SystemMetadata.Keys.Count(key => key.StartsWith(TestRunDeliveryReceipt.ReceiptPrefix, StringComparison.Ordinal)) >= TestRunDeliveryReceipt.MaximumReceipts)
            return new("Rejected", "This test run reached its manual event delivery limit. Start a new test run.");

        var wait = await bookmarks.FindAsync(execution.WorkflowExecutionId, request.WaitId, cancellationToken);
        if (wait is null || wait.ExpiresAt <= time.GetUtcNow())
            return new("NoLongerWaiting", "The selected wait is no longer active.");
        var executable = await executables.FindAsync(execution.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !executable.NodesById.TryGetValue(wait.ExecutableNodeId, out var node))
            return new("Rejected", "The pinned test activity is unavailable.");
        var context = new TestRunWaitContext(executable, node, wait);
        var matchesDrivers = drivers.Where(driver => driver.Supports(context)).ToArray();
        if (matchesDrivers.Length != 1)
            return new("Rejected", "No single installed module can deliver to this wait.");
        var driver = matchesDrivers[0];
        var validation = driver.Validate(context, request.ModeId, request.Data);
        if (!validation.IsValid)
            return new("Rejected", string.Join(" ", validation.Findings.Select(finding => finding.Message)));
        var stimulus = await driver.PrepareAsync(new(context, request.ModeId, request.Data), cancellationToken);
        receipt = new(request.TestRunId, request.DeliveryId, request.WaitId, configurationDigest, TestRunDeliveryReceipt.Digest(stimulus));
        var activity = await activities.FindAsync(execution.WorkflowExecutionId, wait.ActivityExecutionId, cancellationToken);
        var registration = activity?.TriggerRegistrations?.SingleOrDefault(registration => registration.RegistrationId == wait.BookmarkId);
        if (registration is null)
            return new("NoLongerWaiting", "The selected trigger registration is no longer active.");

        var dispatched = await resume.DispatchAsync(new BookmarkResumeDispatchRequest(
            execution.WorkflowExecutionId, wait.StimulusType, wait.StimulusHash, stimulus,
            idempotencyKey: $"test-drive:{receipt.DeliveryId}",
            requestedBy: StartWorkflowTestRunRequestHandler.RequestedBy,
            metadata: null,
            payloadType: registration.PayloadType, providerId: driver.DriverId,
            bookmarkId: wait.BookmarkId, testRunDelivery: receipt), cancellationToken: cancellationToken);
        return dispatched.Status switch
        {
            BookmarkResumeDispatchStatus.Dispatched => new("Delivered"),
            BookmarkResumeDispatchStatus.Duplicate or BookmarkResumeDispatchStatus.Deferred => new("Pending", "This delivery is queued. Refresh its result before retrying."),
            BookmarkResumeDispatchStatus.NotFound => new("NoLongerWaiting"),
            _ => new("Rejected", "The runtime could not accept this test event delivery.")
        };
    }

    private async Task<WorkflowExecutionState?> ResolveExecutionAsync(string testRunId, CancellationToken cancellationToken)
    {
        var view = await testRuns.GetAsync(testRunId, cancellationToken);
        if (view?.WorkflowExecutionId is not { } executionId)
            return null;
        var execution = await executions.FindAsync(executionId, cancellationToken);
        return execution is { RunKind: WorkflowRunKind.TestRun, TestScope: { } scope } && scope.ScopeId == testRunId ? execution : null;
    }

    private async ValueTask<bool> IsOpenAsync(WorkflowExecutionState execution, CancellationToken cancellationToken)
    {
        var expected = execution.TestScope!;
        var record = await scopes.FindAsync(expected.ScopeId, cancellationToken);
        return record is { State: WorkflowTestScopeState.Open } && WorkflowTestScope.ContextEquals(record.Scope, expected) && !expected.IsExpired(time.GetUtcNow());
    }
}
