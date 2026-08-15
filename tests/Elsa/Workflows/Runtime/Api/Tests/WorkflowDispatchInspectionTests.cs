using System.Reflection;
using System.Text.Json;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowDispatchInspectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("runtime/workflows/dispatches", typeof(ListWorkflowDispatches), typeof(IReadOnlyCollection<WorkflowDispatchView>))]
    [InlineData("runtime/workflows/dispatches/{dispatchId}", typeof(GetWorkflowDispatch), typeof(WorkflowDispatchView))]
    public void Endpoints_expose_authenticated_runtime_read_contracts(string route, Type request, Type response)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);

        Assert.Equal((request, response), RuntimeApiEndpointTestFactory.Contract(endpoint));
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, PermissionNames.WorkflowRuntimeRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Redrive_endpoint_requires_manage_permission_without_weakening_read_routes()
    {
        var redrive = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/dispatches/{dispatchId}/redrive");
        var read = RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/dispatches/{dispatchId}");

        Assert.Equal(
            (typeof(RedriveWorkflowDispatch), typeof(WorkflowDispatchRedriveView)),
            RuntimeApiEndpointTestFactory.Contract(redrive));
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(redrive, PermissionNames.WorkflowRuntimeManage);
        Assert.Null(redrive.Definition.AnonymousVerbs);
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(read, PermissionNames.WorkflowRuntimeRead);
    }

    [Fact]
    public void Capabilities_publish_additive_dispatch_links_without_changing_major_version()
    {
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);

        Assert.Equal("runtime/workflows/dispatches", links["workflow-dispatches"].Href);
        Assert.Equal("runtime/workflows/dispatches/{dispatchId}", links["workflow-dispatch"].Href);
        Assert.True(links["workflow-dispatch"].Templated);
        Assert.Equal("runtime/workflows/dispatches/{dispatchId}/redrive", links["workflow-dispatch-redrive"].Href);
        Assert.True(links["workflow-dispatch-redrive"].Templated);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public async Task List_handler_enforces_the_bounded_maximum_and_forwards_continuation()
    {
        var store = new CapturingQueryStore();
        var handler = new ListWorkflowDispatchesRequestHandler(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.Handle(new ListWorkflowDispatches("parent-1", null, null, WorkflowDispatchQuery.MaximumTake + 1), CancellationToken.None));

        await handler.Handle(
            new ListWorkflowDispatches("parent-1", null, "dispatchfailed", 5)
            {
                AfterCreatedAt = Now,
                AfterDispatchId = "dispatch-after"
            },
            CancellationToken.None);

        Assert.NotNull(store.Query);
        Assert.Equal(5, store.Query.Take);
        Assert.Equal(Now, store.Query.AfterCreatedAt);
        Assert.Equal("dispatch-after", store.Query.AfterDispatchId);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, store.Query.Status);
    }

    [Fact]
    public async Task List_handler_filters_by_parent_child_and_status_before_take()
    {
        var store = new InMemoryWorkflowDispatchStore();
        await SaveLifecycleAsync(store, NewRecord("parent-1", "activity-1", WorkflowDispatchStatus.Completed));
        await SaveLifecycleAsync(store, NewRecord("parent-1", "activity-2", WorkflowDispatchStatus.Started));
        await SaveLifecycleAsync(store, NewRecord("parent-2", "activity-3", WorkflowDispatchStatus.Completed));
        var childId = new WorkflowDispatchIdentity("parent-1", "activity-1").ChildWorkflowExecutionId;
        var handler = new ListWorkflowDispatchesRequestHandler(store);

        var result = await handler.Handle(
            new ListWorkflowDispatches("parent-1", childId, "completed", Take: 1),
            CancellationToken.None);

        var dispatch = Assert.Single(result);
        Assert.Equal("parent-1", dispatch.ParentWorkflowExecutionId);
        Assert.Equal(childId, dispatch.ChildWorkflowExecutionId);
        Assert.Equal(WorkflowDispatchStatus.Completed, dispatch.Status);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, null, "not-a-status")]
    public async Task List_handler_rejects_missing_or_invalid_filters(string? parent, string? child, string? status)
    {
        var handler = new ListWorkflowDispatchesRequestHandler(new InMemoryWorkflowDispatchStore());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ListWorkflowDispatches(parent, child, status), CancellationToken.None));
    }

    [Fact]
    public async Task Get_handler_returns_safe_view_or_not_found_shape()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var record = NewRecord("parent-1", "activity-1", WorkflowDispatchStatus.Pending);
        await store.SaveAsync(record);
        var handler = new GetWorkflowDispatchRequestHandler(store);

        var found = await handler.Handle(new GetWorkflowDispatch(record.DispatchId), CancellationToken.None);
        var missing = await handler.Handle(new GetWorkflowDispatch("missing"), CancellationToken.None);

        Assert.NotNull(found.Dispatch);
        Assert.Null(missing.Dispatch);
    }

    [Fact]
    public async Task Failed_dispatch_list_and_detail_return_identical_safe_failure_evidence()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var pending = NewRecord("parent-failed-parity", "activity-failed-parity", WorkflowDispatchStatus.Pending);
        var identity = new WorkflowDispatchIdentity(
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId);
        var deadLetterId = RuntimePostCommitOutboxItems.OutboxItemId(
            "commit-failed-parity",
            identity.StartIntentId);
        var failedAt = Now.AddMinutes(4);
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
            pending,
            deadLetterId,
            generation: 2,
            attemptCount: 4,
            firstAttemptAt: Now,
            failedAt);
        await store.SaveAsync(pending);
        await store.SaveAsync(failed);
        var listHandler = new ListWorkflowDispatchesRequestHandler(store);
        var detailHandler = new GetWorkflowDispatchRequestHandler(store);

        var listed = Assert.Single(await listHandler.Handle(
            new ListWorkflowDispatches(failed.ParentWorkflowExecutionId, null, "dispatchfailed"),
            CancellationToken.None));
        var detailed = Assert.IsType<WorkflowDispatchView>(
            (await detailHandler.Handle(new GetWorkflowDispatch(failed.DispatchId), CancellationToken.None)).Dispatch);

        Assert.Equal(JsonSerializer.Serialize(detailed), JsonSerializer.Serialize(listed));
        Assert.Equal(identity.DeliveryIncidentId(2), listed.DeliveryIncidentId);
        Assert.Equal(deadLetterId, listed.DeliveryDeadLetterId);
        Assert.Equal(4, listed.DeliveryAttemptCount);
        Assert.Equal(failedAt, listed.DeliveryFailedAt);
        Assert.True(listed.RedriveEligible);
    }

    [Fact]
    public void Safe_view_never_serializes_values_authority_context_or_arbitrary_diagnostics()
    {
        var record = NewRecord(
            "parent-secret",
            "activity-1",
            WorkflowDispatchStatus.DispatchFailed,
            metadata: new Dictionary<string, string>
            {
                ["runtime.diagnostic.code"] = WorkflowDispatchLifecycle.ChildStartDeliveryFailedCode,
                ["runtime.diagnostic.category"] = WorkflowDispatchLifecycle.DeliveryCategory,
                ["rawInput"] = "input-secret",
                ["exception"] = "exception-secret",
                ["stackTrace"] = "stack-secret",
                ["output"] = "output-secret",
                ["redactedOutput"] = "redacted-secret"
            });

        var view = WorkflowDispatchView.From(record);
        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(WorkflowDispatchLifecycle.ChildStartDeliveryFailedCode, view.DiagnosticCode);
        Assert.Equal(WorkflowDispatchLifecycle.DeliveryCategory, view.DiagnosticCategory);
        Assert.All(view.InputCaptures, capture =>
        {
            Assert.Equal("metadataOnly", capture.CaptureMode);
            Assert.False(capture.ValueCaptured);
        });
        Assert.DoesNotContain("input-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("authority-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stack-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("output-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("redacted-secret", json, StringComparison.Ordinal);
        Assert.Null(typeof(WorkflowDispatchView).GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(WorkflowDispatchView).GetProperty("Authority", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(WorkflowDispatchView).GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(WorkflowDispatchView).GetProperty("RequestId", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(WorkflowDispatchView).GetProperty("TestScope", BindingFlags.Public | BindingFlags.Instance));

        var untrustedDiagnostic = WorkflowDispatchView.From(NewRecord(
            "parent-secret-2",
            "activity-2",
            WorkflowDispatchStatus.DispatchFailed,
            new Dictionary<string, string>
            {
                ["runtime.diagnostic.code"] = "exception-secret",
                ["runtime.diagnostic.category"] = "stack-secret"
            }));
        Assert.Null(untrustedDiagnostic.DiagnosticCode);
        Assert.Null(untrustedDiagnostic.DiagnosticCategory);
    }

    [Theory]
    [InlineData(WorkflowRunKind.Unknown)]
    [InlineData(WorkflowRunKind.PublishedRun)]
    [InlineData(WorkflowRunKind.TestRun)]
    [InlineData(WorkflowRunKind.BackgroundWeaverRun)]
    public void Safe_view_exposes_the_exact_inherited_run_kind(WorkflowRunKind runKind)
    {
        var record = NewRecord(
            "parent-run-kind",
            "activity-run-kind",
            WorkflowDispatchStatus.Pending,
            runKind: runKind);

        var view = WorkflowDispatchView.From(record);

        Assert.Equal(runKind, view.RunKind);
    }

    [Fact]
    public void Safe_view_projects_only_allowlisted_delivery_failure_evidence()
    {
        var pending = NewRecord("parent-1", "activity-1", WorkflowDispatchStatus.Pending);
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var deadLetterId = RuntimePostCommitOutboxItems.OutboxItemId("commit-1", identity.StartIntentId);
        var failedAt = Now.AddMinutes(4);
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
            pending,
            deadLetterId,
            generation: 2,
            attemptCount: 4,
            firstAttemptAt: Now,
            failedAt);

        var view = WorkflowDispatchView.From(failed);

        Assert.Equal(identity.DeliveryIncidentId(2), view.DeliveryIncidentId);
        Assert.Equal(deadLetterId, view.DeliveryDeadLetterId);
        Assert.Equal(2, view.DeliveryGeneration);
        Assert.Equal(4, view.DeliveryAttemptCount);
        Assert.Equal(Now, view.DeliveryFirstAttemptAt);
        Assert.Equal(failedAt, view.DeliveryFailedAt);
        Assert.True(view.RedriveEligible);
    }

    [Fact]
    public void Safe_view_suppresses_failure_identifiers_when_either_value_conflicts_with_dispatch_identity()
    {
        var pending = NewRecord("parent-safe-identifiers", "activity-safe-identifiers", WorkflowDispatchStatus.Pending);
        var identity = new WorkflowDispatchIdentity(
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId);
        var otherIdentity = new WorkflowDispatchIdentity("other-parent", "other-activity");
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
            pending,
            RuntimePostCommitOutboxItems.OutboxItemId(
                "commit-safe-identifiers",
                identity.StartIntentId),
            generation: 2,
            attemptCount: 4,
            firstAttemptAt: Now,
            failedAt: Now.AddMinutes(4));
        var forgedValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkflowDispatchLifecycle.DeliveryIncidentIdMetadataKey] =
                otherIdentity.DeliveryIncidentId(2),
            [WorkflowDispatchLifecycle.DeliveryDeadLetterIdMetadataKey] =
                RuntimePostCommitOutboxItems.OutboxItemId(
                    "commit-forged",
                    otherIdentity.StartIntentId),
            [WorkflowDispatchLifecycle.DeliveryGenerationMetadataKey] = "generation-secret-forged"
        };

        foreach (var forged in forgedValues)
        {
            var metadata = failed.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            metadata[forged.Key] = forged.Value;
            var corrupt = NewRecord(
                failed.ParentWorkflowExecutionId,
                failed.ParentActivityExecutionId,
                WorkflowDispatchStatus.DispatchFailed,
                metadata);

            var view = WorkflowDispatchView.From(corrupt);
            var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Null(view.DeliveryIncidentId);
            Assert.Null(view.DeliveryDeadLetterId);
            Assert.False(view.RedriveEligible);
            Assert.DoesNotContain(forged.Value, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Redrive_handler_forwards_only_server_time_and_operator_request_identity()
    {
        var store = new CapturingRedriveStore(new WorkflowDispatchRedriveResult(
            "dispatch-1",
            WorkflowDispatchRedriveDisposition.Accepted,
            WorkflowDispatchStatus.Pending,
            generation: 4,
            incidentId: "incident-1",
            deadLetterId: "outbox-1"));
        var handler = new RedriveWorkflowDispatchRequestHandler(store, new FixedTimeProvider(Now));

        var view = await handler.Handle(new RedriveWorkflowDispatch("dispatch-1", "request-1"), CancellationToken.None);

        Assert.Equal(new WorkflowDispatchRedriveRequest("dispatch-1", "request-1", Now), store.Request);
        Assert.Equal("dispatch-1", view.DispatchId);
        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, view.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Pending, view.Status);
        Assert.Equal(4, view.DeliveryGeneration);
        Assert.Equal("incident-1", view.DeliveryIncidentId);
        Assert.Equal("outbox-1", view.DeliveryDeadLetterId);
        Assert.DoesNotContain("request-1", JsonSerializer.Serialize(view), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(WorkflowDispatchRedriveRequest.RequestedAt), JsonSerializer.Serialize(view), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkflowDispatchRedriveDisposition.Accepted)]
    [InlineData(WorkflowDispatchRedriveDisposition.AlreadyApplied)]
    [InlineData(WorkflowDispatchRedriveDisposition.ActiveConflict)]
    [InlineData(WorkflowDispatchRedriveDisposition.NotEligible)]
    public async Task Redrive_handler_logs_safe_structured_disposition_only(
        WorkflowDispatchRedriveDisposition disposition)
    {
        var logger = new RecordingLogger<RedriveWorkflowDispatchRequestHandler>();
        var store = new CapturingRedriveStore(new WorkflowDispatchRedriveResult(
            "dispatch-safe",
            disposition,
            WorkflowDispatchStatus.DispatchFailed,
            generation: 3,
            incidentId: "incident-safe",
            deadLetterId: "outbox-safe"));
        var handler = new RedriveWorkflowDispatchRequestHandler(store, new FixedTimeProvider(Now), logger);

        await handler.Handle(new RedriveWorkflowDispatch("dispatch-safe", "request-secret"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(68109, entry.EventId.Id);
        Assert.Equal("WorkflowDispatchRedriveEvaluated", entry.EventId.Name);
        Assert.Null(entry.Exception);
        var serialized = string.Join(" ", entry.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.Contains(disposition.ToString(), serialized, StringComparison.Ordinal);
        Assert.Contains("dispatch-safe", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("request-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redrive_http_request_has_no_executable_tenant_context_or_delivery_payload_inputs()
    {
        var properties = typeof(RedriveWorkflowDispatch)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { nameof(RedriveWorkflowDispatch.DispatchId), nameof(RedriveWorkflowDispatch.RequestId) },
            properties);
    }

    private static WorkflowDispatchRecord NewRecord(
        string parentExecutionId,
        string activityExecutionId,
        WorkflowDispatchStatus status,
        IReadOnlyDictionary<string, string>? metadata = null,
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun)
    {
        var identity = new WorkflowDispatchIdentity(parentExecutionId, activityExecutionId);
        return new(
            identity.DispatchId,
            parentExecutionId,
            activityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new WorkflowExecutableSourceProvenance(
                "source-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
                "definition-1", "version-1", "1.0.0", "publication-1", "slot-1"),
            WorkflowDispatchMode.FireAndForget,
            status,
            "correlation-secret",
            "tenant-secret",
            new WorkflowExecutionPartition("partition-secret"),
            runKind,
            new WorkflowExecutionAuthoritySnapshot("authority-secret", "initiator-secret"),
            [new WorkflowDispatchInputDescriptor("message", "System.String")],
            Now,
            Now,
            metadata);
    }

    private static async ValueTask SaveLifecycleAsync(
        InMemoryWorkflowDispatchStore store,
        WorkflowDispatchRecord record)
    {
        if (record.Status == WorkflowDispatchStatus.Pending)
        {
            await store.SaveAsync(record);
            return;
        }

        var pending = NewRecord(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId, WorkflowDispatchStatus.Pending);
        await store.SaveAsync(pending);
        await store.SaveAsync(pending.TransitionTo(record.Status, record.UpdatedAt));
    }

    private sealed class CapturingQueryStore : IWorkflowDispatchQueryStore
    {
        public WorkflowDispatchQuery? Query { get; private set; }

        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
            WorkflowDispatchQuery query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>([]);
        }
    }

    private sealed class CapturingRedriveStore(WorkflowDispatchRedriveResult result) : IWorkflowDispatchRedriveStore
    {
        public WorkflowDispatchRedriveRequest? Request { get; private set; }

        public ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
            WorkflowDispatchRedriveRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?> { ["Message"] = formatter(state, exception) };
            Entries.Add(new LogEntry(eventId, fields, exception));
        }
    }

    private sealed record LogEntry(
        EventId EventId,
        IReadOnlyDictionary<string, object?> Fields,
        Exception? Exception);
}
