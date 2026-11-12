using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Routes the stored preparation to the same installed module, with current server authorization.</summary>
internal sealed class DefaultWeaverActionProposalExecutor(WeaverPreparedInvocationResolver resolver) : IWeaverActionProposalExecutor
{
    /// <summary>A known zero-effect cancellation observed before the owning module or client was dispatched.</summary>
    internal const string CancelledBeforeDispatchCode = "weaver.proposal.cancelled";

    /// <summary>
    /// The engine-owned signal for <see cref="CancelledBeforeDispatchCode"/>. Classification compares this exact
    /// instance, never the public code: a module can return any failure code after dispatch, but it cannot
    /// produce this private instance, so a module-authored "cancelled" failure stays an ordinary refusal.
    /// </summary>
    private static readonly WeaverError CancelledBeforeDispatch = new(CancelledBeforeDispatchCode,
        "Execution was cancelled before dispatch to the owning module. No effect occurred.", 409);

    /// <summary>True only for this executor's own pre-dispatch cancellation result.</summary>
    internal static bool IsCancelledBeforeDispatch<T>(WeaverResult<T> result) => ReferenceEquals(result.Error, CancelledBeforeDispatch);

    public async Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default)
    {
        (IWeaverTool? Tool, WeaverError? Error) resolved;
        // The current-authority recheck precedes any module or client dispatch, so cancellation here
        // is a known zero-effect outcome rather than an uncertain one.
        try { resolved = await resolver.ResolveAsync(proposal, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(null, CancelledBeforeDispatch);
        }
        if (resolved.Error is { } error)
            return WeaverResult<WeaverProposalExecutionResult>.Failure(error.Code, error.Message, error.StatusCode);
        var prepared = proposal.PreparedInvocation!;
        if (prepared.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState)
        {
            if (!proposal.RequiresApproval || proposal.ApprovedAt is null || prepared.Client is null || resolved.Tool is not IWeaverClientTool)
                return WeaverResult<WeaverProposalExecutionResult>.Failure("weaver.proposal.client_unavailable", "No reviewed originating client execution contract is available.", 409);
            return WeaverResult<WeaverProposalExecutionResult>.Success(Result(proposal, false,
                "Waiting for the originating view to confirm the change.", "awaiting-client") with { ClientInvocation = prepared });
        }

        var execution = await resolved.Tool!.ExecuteAsync(prepared, cancellationToken);
        if (!execution.Succeeded || execution.Value is null)
            return WeaverResult<WeaverProposalExecutionResult>.Failure(execution.Error?.Code ?? "weaver.proposal.refused",
                execution.Error?.Message ?? "The owning module refused application.", execution.Error?.StatusCode ?? 409);
        if (!WeaverInvocationIntegrity.ValidReceipt(prepared, execution.Value, WeaverEffectScope.ServerPersisted))
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The module returned without a valid owning-resource effect receipt. Its outcome is uncertain; the operation will not be replayed."));
        var result = FreezeResult(execution.Value);
        return WeaverResult<WeaverProposalExecutionResult>.Success(Result(proposal, true, result.Content, "executed") with { ToolResult = result });
    }

    public async Task<WeaverResult<WeaverProposalExecutionResult>> CompleteClientAsync(WeaverActionProposal proposal,
        WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        if (proposal.PreparedInvocation is not { ExecutionLocation: WeaverExecutionLocation.ClientWorkingState, Client: not null } prepared)
            return WeaverResult<WeaverProposalExecutionResult>.Failure("weaver.proposal.client_unavailable", "This proposal has no bound client completion contract.", 409);
        var resolved = await resolver.ResolveAsync(proposal, cancellationToken);
        if (resolved.Error is not null)
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "A client result was reported after its execution contract, authority or lifetime changed. The effect is unverified and will not be replayed."));
        if (resolved.Tool is not IWeaverClientTool clientTool)
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The installed tool no longer supports validation of the dispatched client operation. Its reported effect remains uncertain and will not be replayed."));
        if (acknowledgement.Outcome == WeaverClientOutcome.Uncertain)
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The originating client could not establish whether the operation completed. It will not be replayed."));
        if (acknowledgement.Outcome == WeaverClientOutcome.Refused)
        {
            var refused = LifecycleResult(prepared, "refused", "The originating client refused the operation before application.");
            return WeaverResult<WeaverProposalExecutionResult>.Success(Result(proposal, false, refused.Content, "refused") with { ToolResult = refused });
        }
        var completed = await clientTool.CompleteAsync(prepared, acknowledgement, cancellationToken);
        if (!completed.Succeeded || completed.Value is null || !WeaverInvocationIntegrity.ValidReceipt(prepared, completed.Value, WeaverEffectScope.ClientWorkingState))
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The reported client effect did not validate against the exact prepared operation. Its outcome is uncertain and will not be replayed."));
        var result = FreezeResult(completed.Value);
        return WeaverResult<WeaverProposalExecutionResult>.Success(Result(proposal, true, result.Content, "executed") with { ToolResult = result });
    }

    internal static WeaverProposalExecutionResult Result(WeaverActionProposal proposal, bool executed, string summary, string status) => new(
        proposal.Id, executed, proposal.PreparedInvocation?.Target?.ResourceType ?? proposal.ResourceType ?? "unavailable",
        proposal.PreparedInvocation?.Target?.ResourceId ?? proposal.ResourceId ?? string.Empty, summary) { Status = status };

    internal static WeaverProposalExecutionResult Uncertain(WeaverActionProposal proposal, string summary)
        => Result(proposal, false, summary, "uncertain") with
        {
            ToolResult = proposal.PreparedInvocation is { } prepared ? LifecycleResult(prepared, "uncertain", summary) : null
        };

    internal const string LifecycleOutcomeType = "weaver.operation-outcome";
    internal const string LifecycleOutcomeSchemaVersion = "1";

    internal static WeaverToolResult LifecycleResult(WeaverPreparedToolInvocation prepared, string status, string summary)
        => new(prepared.ToolCallId, prepared.ToolName, false, summary,
            new(LifecycleOutcomeType, LifecycleOutcomeSchemaVersion, JsonSerializer.SerializeToElement(new { status, prepared.InvocationId })));

    /// <summary>
    /// Recognizes the engine's own receipt-less lifecycle envelope (<see cref="LifecycleResult"/>) for the stamped
    /// original invocation of a mutating tool. A module never authors such a result: proposal execution replaces a
    /// module refusal or receipt-less return with this envelope, and the invoker refuses failed mutation dispatch.
    /// A read tool's own result, any receipt or success, and any other status, schema or identity are not lifecycle outcomes.
    /// </summary>
    internal static bool IsLifecycleResult(WeaverToolResult result, WeaverToolDescriptor tool)
    {
        if (result.Succeeded || result.Receipt is not null || tool.Mutability != WeaverToolMutability.Mutating || result.ToolName != tool.Name
            || string.IsNullOrEmpty(result.ToolCallId) || result.Access is not { InvocationId.Length: > 0 } access
            || result.Payload is not { TypeId: LifecycleOutcomeType, SchemaVersion: LifecycleOutcomeSchemaVersion, Data: var data }
            || data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() != 2)
            return false;
        return data.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            && status.GetString() is "denied" or "refused" or "uncertain"
            && data.TryGetProperty(nameof(WeaverPreparedToolInvocation.InvocationId), out var identity)
            && identity.ValueKind == JsonValueKind.String && identity.GetString() == access.InvocationId;
    }

    private static WeaverToolResult FreezeResult(WeaverToolResult value) => value with
    {
        Payload = value.Payload is null ? null : WeaverInvocationIntegrity.Freeze(value.Payload),
        Receipt = value.Receipt is null ? null : value.Receipt with { Evidence = WeaverInvocationIntegrity.Freeze(value.Receipt.Evidence) }
    };
}
