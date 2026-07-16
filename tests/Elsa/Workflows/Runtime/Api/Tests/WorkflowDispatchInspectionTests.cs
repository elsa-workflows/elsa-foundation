using System.Reflection;
using System.Text.Json;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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
        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Capabilities_publish_additive_dispatch_links_without_changing_major_version()
    {
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);

        Assert.Equal("runtime/workflows/dispatches", links["workflow-dispatches"].Href);
        Assert.Equal("runtime/workflows/dispatches/{dispatchId}", links["workflow-dispatch"].Href);
        Assert.True(links["workflow-dispatch"].Templated);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
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

    private static WorkflowDispatchRecord NewRecord(
        string parentExecutionId,
        string activityExecutionId,
        WorkflowDispatchStatus status,
        IReadOnlyDictionary<string, string>? metadata = null)
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
            WorkflowRunKind.PublishedRun,
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
}
