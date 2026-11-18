using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Instances.VariableLifecycle;

// Spec 1229 (S-6): the variable lifecycle inspection endpoints. All use the existing activity-execution inspection
// permission; values are released only by the payload endpoint, under the payload permission (FR-013).

/// <summary>US5: the instance's variable instances with their current version, last writer and value state.</summary>
[Get("runtime/workflows/instances/{workflowExecutionId}/variables")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("listing instance variables")]
public sealed class ListVariablesEndpoint(IVariableLifecycleInspectionService inspection) : ApiEndpoint<ListInstanceVariables, IReadOnlyList<VariableSummaryView>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListInstanceVariables";

    public override async Task<IReadOnlyList<VariableSummaryView>> HandleAsync(ListInstanceVariables request, CancellationToken cancellationToken) =>
        await inspection.ListVariablesAsync(request.WorkflowExecutionId, cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}

/// <summary>US1, US2: the write history in commit order, filtered, paged by a cursor of commit sequence and position.</summary>
[Get("runtime/workflows/instances/{workflowExecutionId}/variable-writes")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("listing variable writes")]
public sealed class ListVariableWritesEndpoint(IVariableLifecycleInspectionService inspection) : ApiEndpoint<ListVariableWrites, VariableWriteHistoryPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListVariableWrites";

    public override async Task<VariableWriteHistoryPageView> HandleAsync(ListVariableWrites request, CancellationToken cancellationToken) =>
        await inspection.ListWritesAsync(new VariableWriteHistoryQuery(
            request.WorkflowExecutionId,
            request.FrameId,
            request.ScopeId,
            request.VariableKey,
            request.Source,
            request.Outcome,
            request.Overwrites,
            request.Cursor,
            request.Take), cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}

/// <summary>US3: the commit-ordered journal, paged by commit.</summary>
[Get("runtime/workflows/instances/{workflowExecutionId}/journal")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("reading the instance journal")]
public sealed class GetJournalEndpoint(IVariableLifecycleInspectionService inspection) : ApiEndpoint<GetInstanceJournal, JournalPageView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetInstanceJournal";

    public override async Task<JournalPageView> HandleAsync(GetInstanceJournal request, CancellationToken cancellationToken) =>
        await inspection.ListJournalAsync(request.WorkflowExecutionId, request.Cursor, request.Take, cancellationToken) ?? throw new ActivityExecutionMissingSignal();
}

/// <summary>
/// US4: compares two instances of one executable version by run-time path and digest. Any other pair is refused with a
/// 409 problem whose code is <c>differentExecutable</c>; either side missing or hidden is 404.
/// </summary>
[Get("runtime/workflows/instances/{workflowExecutionId}/compare/{otherWorkflowExecutionId}")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("comparing workflow instances")]
public sealed class CompareEndpoint(IVariableLifecycleInspectionService inspection) : ApiEndpoint<CompareWorkflowInstances, InstanceComparisonView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "CompareWorkflowInstances";

    public override async Task<InstanceComparisonView> HandleAsync(CompareWorkflowInstances request, CancellationToken cancellationToken)
    {
        var result = await inspection.CompareAsync(request.WorkflowExecutionId, request.OtherWorkflowExecutionId, cancellationToken);
        return result.Outcome switch
        {
            InstanceComparisonOutcome.Compared => result.View!,
            InstanceComparisonOutcome.DifferentExecutable => throw new InstanceComparisonRefusedSignal(
                result.View?.RefusalReason ?? "differentExecutable",
                "The two instances run different executable versions; only instances of one version can be compared."),
            _ => throw new ActivityExecutionMissingSignal()
        };
    }
}

/// <summary>
/// FR-006: releases one value of a write-history entry (written, overwritten or refused) under the payload permission,
/// audited like the activity value-evidence payload read.
/// </summary>
[Get("runtime/workflows/instances/{workflowExecutionId}/variable-writes/{entryId}/payload")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)]
[ActivityInspectionProblems("resolving a variable write value")]
public sealed class GetVariableWritePayloadEndpoint(IVariableLifecycleInspectionService inspection) : ApiEndpoint<GetVariableWritePayload, VariableWritePayloadView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetVariableWritePayload";

    public override async Task<VariableWritePayloadView> HandleAsync(GetVariableWritePayload request, CancellationToken cancellationToken)
    {
        var result = await inspection.ReadPayloadAsync(request.WorkflowExecutionId, request.EntryId, request.Value, cancellationToken);
        return result.Outcome switch
        {
            VariableWritePayloadOutcome.Resolved => result.Value!,
            VariableWritePayloadOutcome.Denied => throw new ActivityValuePayloadDeniedSignal(),
            VariableWritePayloadOutcome.Unavailable => throw new ActivityValuePayloadUnavailableSignal(),
            _ => throw new ActivityExecutionMissingSignal()
        };
    }
}
