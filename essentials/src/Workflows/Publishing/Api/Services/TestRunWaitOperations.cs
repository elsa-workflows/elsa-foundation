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
    TimeProvider time,
    IWorkflowTestRunWaitControl? kernel = null) : ITestRunWaitOperations
{
    public async Task<TestRunWaitsView> GetAsync(string testRunId, CancellationToken cancellationToken)
    {
        var run = await testRuns.GetAsync(testRunId, cancellationToken);
        if (IsKernelRun(run))
        {
            if (await GetKernelRunAsync(run!, cancellationToken) is not { } exact) return new([]);
            return new((await ReadContextsAsync(exact, cancellationToken)).Select(DescribeWait).ToArray());
        }
        var execution = await ResolveExecutionAsync(run, cancellationToken);
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
            result.Add(DescribeWait(new(executable, node, wait)));
        }
        return new(result);
    }

    public async Task<TestRunEventView> DeliverAsync(TestRunEventRequest request, CancellationToken cancellationToken)
    {
        var run = await testRuns.GetAsync(request.TestRunId, cancellationToken);
        if (IsKernelRun(run))
        {
            var exact = await GetKernelRunAsync(run!, cancellationToken);
            return exact is null ? Missing() : await DeliverKernelAsync(exact, request, cancellationToken);
        }
        var execution = await ResolveExecutionAsync(run, cancellationToken);
        if (execution is null || !await IsOpenAsync(execution, cancellationToken))
            return Missing();
        if (ValidateRequest(request) is { } invalid) return invalid;

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

    private async Task<WorkflowExecutionState?> ResolveExecutionAsync(WorkflowTestRunView? view, CancellationToken cancellationToken)
    {
        if (view?.WorkflowExecutionId is not { } executionId)
            return null;
        var execution = await executions.FindAsync(executionId, cancellationToken);
        return execution is { RunKind: WorkflowRunKind.TestRun, TestScope: { } scope } && scope.ScopeId == view.TestRunId ? execution : null;
    }

    private async ValueTask<bool> IsOpenAsync(WorkflowExecutionState execution, CancellationToken cancellationToken)
    {
        var expected = execution.TestScope!;
        var record = await scopes.FindAsync(expected.ScopeId, cancellationToken);
        return record is { State: WorkflowTestScopeState.Open } && WorkflowTestScope.ContextEquals(record.Scope, expected) && !expected.IsExpired(time.GetUtcNow());
    }

    private sealed record KernelRun(string Execution, WorkflowExecutable Executable, WorkflowTestScopeRecord Scope);

    private static bool IsKernelRun(WorkflowTestRunView? run) =>
        run is not null && run.Metadata.GetValueOrDefault("runtime.sourceReferenceId") == run.TestRunId;

    private async ValueTask<KernelRun?> GetKernelRunAsync(WorkflowTestRunView run, CancellationToken token)
    {
        if (kernel is null || run is not { WorkflowExecutionId: { } execution, ArtifactId: { } artifact } ||
            await scopes.FindAsync(run.TestRunId, token) is not { State: WorkflowTestScopeState.Open } scope ||
            scope.IsolatedActor?.ActorReference != execution || scope.Scope.IsExpired(time.GetUtcNow()) ||
            await executables.FindAsync(artifact, token) is not { } executable)
            return null;
        return new(execution, executable, scope);
    }

    private async ValueTask<IReadOnlyList<TestRunWaitContext>> ReadContextsAsync(KernelRun run, CancellationToken token)
    {
        var waits = await kernel!.ReadAsync(run.Scope.Scope, run.Execution, token);
        var result = new List<TestRunWaitContext>();
        foreach (var wait in waits)
        {
            var target = WorkflowExecutableResumeTarget.ComposeScopedId(wait.NodeId, "resume-target:event");
            if (!run.Executable.NodesById.TryGetValue(wait.NodeId, out var node) ||
                !run.Executable.ResumeTargets.ContainsKey(target)) continue;
            var bookmark = new BookmarkState(wait.WaitId, run.Execution, wait.FrameId, wait.NodeId, target, "Event",
                wait.Event, null, new Dictionary<string, string>(), run.Scope.CreatedAt, run.Scope.Scope.ExpiresAt);
            result.Add(new(run.Executable, node, bookmark));
        }
        return result;
    }

    private TestRunWaitView DescribeWait(TestRunWaitContext context)
    {
        var matches = drivers.Where(driver => driver.Supports(context)).ToArray();
        var label = context.Node.Metadata.GetValueOrDefault("displayName") ?? context.Node.AuthoredActivityId;
        return matches.Length == 1
            ? new(context.Wait.BookmarkId, context.Node.AuthoredActivityId, label, matches[0].DriverId, true, null, matches[0].Describe(context))
            : new(context.Wait.BookmarkId, context.Node.AuthoredActivityId, label, null, false,
                matches.Length == 0 ? "No installed module supports test delivery to this wait." : "Multiple module drivers claim this wait.", []);
    }

    private async Task<TestRunEventView> DeliverKernelAsync(KernelRun run, TestRunEventRequest request, CancellationToken token)
    {
        if (ValidateRequest(request) is { } invalid) return invalid;
        var configuration = TestRunDeliveryReceipt.Digest(JsonSerializer.SerializeToElement(new { modeId = request.ModeId, data = request.Data }));
        var receipt = new TestRunDeliveryReceipt(request.TestRunId, request.DeliveryId, request.WaitId, configuration);
        var prior = await kernel!.DeliverAsync(run.Scope.Scope, run.Execution, receipt, null, token);
        if (prior != "Unseen") return DeliveryResult(prior);
        var context = (await ReadContextsAsync(run, token)).SingleOrDefault(wait => wait.Wait.BookmarkId == request.WaitId);
        if (context is null) return DeliveryResult("NoLongerWaiting");
        var matches = drivers.Where(driver => driver.Supports(context)).ToArray();
        if (matches is not [var driver]) return new("Rejected", "No single installed module can deliver to this wait.");
        var validation = driver.Validate(context, request.ModeId, request.Data);
        if (!validation.IsValid)
            return new("Rejected", string.Join(" ", validation.Findings.Select(finding => finding.Message)));
        var stimulus = await driver.PrepareAsync(new(context, request.ModeId, request.Data), token);
        receipt = new(request.TestRunId, request.DeliveryId, request.WaitId, configuration, TestRunDeliveryReceipt.Digest(stimulus));
        return DeliveryResult(await kernel.DeliverAsync(run.Scope.Scope, run.Execution, receipt, stimulus, token));
    }

    private static TestRunEventView? ValidateRequest(TestRunEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WaitId) || string.IsNullOrWhiteSpace(request.ModeId))
            return new("Rejected", "Select an active wait and a delivery mode.");
        return Guid.TryParseExact(request.DeliveryId, "D", out _) ? null : new("Rejected", "The event delivery ID must be a UUID.");
    }

    private static TestRunEventView Missing() => new("Rejected", "This test run is missing, stopped or expired.");

    private static TestRunEventView DeliveryResult(string status) => status switch
    {
        "Delivered" or "AlreadyDelivered" => new(status),
        "Conflict" => new("Rejected", "This delivery ID already names different data or another wait."),
        "Limit" => new("Rejected", "This test run reached its manual event delivery limit. Start a new test run."),
        "Pending" => new("Pending", "This delivery is queued. Refresh its result before retrying."),
        "NoLongerWaiting" => new("NoLongerWaiting", "The selected wait is no longer active."),
        _ => Missing()
    };
}
