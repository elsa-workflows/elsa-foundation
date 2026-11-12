using System.Text.Json;
using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Routes the stored preparation to the same installed module, with current server authorization.</summary>
internal sealed class DefaultWeaverActionProposalExecutor(WeaverPreparedInvocationResolver resolver) : IWeaverActionProposalExecutor
{
    // Proof may outlive a request with its retained result. Its opaque token must not retain this scoped executor.
    private readonly DispatchOrigin _dispatchOrigin = new();
    internal sealed class DispatchOrigin;
    /// <summary>A known zero-effect cancellation observed before the owning module or client was dispatched.</summary>
    internal const string CancelledBeforeDispatchCode = "weaver.proposal.cancelled";

    /// <summary>
    /// The engine-owned signal for <see cref="CancelledBeforeDispatchCode"/>. Classification compares this exact
    /// instance, never the public code: a module can return any failure code after dispatch, but it cannot
    /// produce this private instance, so a module-authored "cancelled" failure stays an ordinary refusal.
    /// </summary>
    private static readonly WeaverError CancelledBeforeDispatch = new(CancelledBeforeDispatchCode,
        "Execution was cancelled before dispatch to the owning module. No effect occurred.", 409);

    private static readonly ConditionalWeakTable<WeaverResult<WeaverProposalExecutionResult>, PreDispatchProof> Undispatched = new();
    private sealed record PreDispatchProof(string ProposalId, string? InvocationId, DispatchOrigin Origin);

    /// <summary>Private live proof tied to this execution producer and exact invocation, never to a public code/flag.</summary>
    internal static WeaverResult<WeaverProposalExecutionResult> RefusedBeforeDispatch(WeaverActionProposal proposal, WeaverError error, DispatchOrigin origin)
    {
        WeaverResult<WeaverProposalExecutionResult> result = new(null, error);
        Undispatched.Add(result, new(proposal.Id, proposal.PreparedInvocation?.InvocationId, origin));
        return result;
    }

    internal static WeaverResult<WeaverProposalExecutionResult> CancelledBeforeDispatchResult(WeaverActionProposal proposal, DispatchOrigin origin)
        => RefusedBeforeDispatch(proposal, CancelledBeforeDispatch, origin);

    internal static bool IsBeforeDispatch(WeaverActionProposal proposal, WeaverResult<WeaverProposalExecutionResult> result, DispatchOrigin origin)
        => Undispatched.TryGetValue(result, out var proof) && ReferenceEquals(proof.Origin, origin)
            && proof.ProposalId == proposal.Id && proof.InvocationId == proposal.PreparedInvocation?.InvocationId;

    internal bool IsOwnBeforeDispatch(WeaverActionProposal proposal, WeaverResult<WeaverProposalExecutionResult> result)
        => IsBeforeDispatch(proposal, result, _dispatchOrigin);

    internal static bool HasBeforeDispatchProof(WeaverResult<WeaverProposalExecutionResult> result)
        => Undispatched.TryGetValue(result, out _);

    /// <summary>True only for this executor's own pre-dispatch cancellation result.</summary>
    internal static bool IsCancelledBeforeDispatch<T>(WeaverResult<T> result) => ReferenceEquals(result.Error, CancelledBeforeDispatch);

    public async Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAsync(WeaverActionProposal proposal, CancellationToken cancellationToken = default)
    {
        (WeaverToolAdmission? Admission, WeaverError? Error) resolved;
        // The current-authority recheck precedes any module or client dispatch, so cancellation here
        // is a known zero-effect outcome rather than an uncertain one.
        try { resolved = await resolver.ResolveAsync(proposal, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CancelledBeforeDispatchResult(proposal, _dispatchOrigin);
        }
        catch (Exception)
        {
            return RefusedBeforeDispatch(proposal, new("weaver.proposal.admission_unavailable",
                "Current execution admission could not be established before dispatch.", 503), _dispatchOrigin);
        }
        if (resolved.Error is { } error)
            return RefusedBeforeDispatch(proposal, error, _dispatchOrigin);
        try { return await ExecuteAdmittedAsync(proposal, resolved.Admission!, cancellationToken); }
        catch (Exception)
        {
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "Execution was interrupted after dispatch. Its effect is uncertain and will not be replayed."));
        }
    }

    internal async Task<WeaverResult<WeaverProposalExecutionResult>> ExecuteAdmittedAsync(WeaverActionProposal proposal,
        WeaverToolAdmission admission, CancellationToken cancellationToken)
    {
        var prepared = proposal.PreparedInvocation!;
        if (cancellationToken.IsCancellationRequested)
            return CancelledBeforeDispatchResult(proposal, _dispatchOrigin);
        if (prepared.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState)
        {
            if (!proposal.RequiresApproval || proposal.ApprovedAt is null || prepared.Client is null || admission.Tool is not IWeaverClientTool)
                return RefusedBeforeDispatch(proposal, new("weaver.proposal.client_unavailable", "No reviewed originating client execution contract is available.", 409), _dispatchOrigin);
            return WeaverResult<WeaverProposalExecutionResult>.Success(Result(proposal, false,
                "Waiting for the originating view to confirm the change.", "awaiting-client") with { ClientInvocation = prepared });
        }

        var execution = await admission.Tool.ExecuteAsync(prepared, cancellationToken);
        if (execution.Error is { } ownerError)
            execution = execution with { Error = WeaverModuleCodes.Error(ownerError, admission.Descriptor.ModuleCodes, ownerOrigin: true) };
        if (execution.Succeeded && execution.Value is null || !execution.Succeeded
            && (execution.Value?.Succeeded == true || execution.Value?.Receipt is not null))
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The module returned a contradictory or empty success after dispatch. Its effect is uncertain and will not be replayed."));
        if (!execution.Succeeded)
            return execution.Error is { } error ? new(null, error)
                : WeaverResult<WeaverProposalExecutionResult>.Failure("weaver.proposal.refused", "The owning module refused application.", 409);
        var normalized = WeaverModuleCodes.Result(execution.Value!, admission.Descriptor.ModuleCodes).Value;
        var status = WeaverInvocationIntegrity.SuccessStatus(prepared, normalized, admission.Descriptor);
        if (status is null)
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The module returned without a valid owning-resource effect receipt. Its outcome is uncertain; the operation will not be replayed."));
        var result = FreezeResult(normalized);
        return WeaverResult<WeaverProposalExecutionResult>.Success(Result(proposal, true, result.Content, status) with { ToolResult = result });
    }

    public async Task<WeaverResult<WeaverProposalExecutionResult>> CompleteClientAsync(WeaverActionProposal proposal,
        WeaverClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        if (proposal.PreparedInvocation is not { ExecutionLocation: WeaverExecutionLocation.ClientWorkingState, Client: not null } prepared)
            return WeaverResult<WeaverProposalExecutionResult>.Failure("weaver.proposal.client_unavailable", "This proposal has no bound client completion contract.", 409);
        (WeaverToolAdmission? Admission, WeaverError? Error) resolved;
        try { resolved = await resolver.ResolveAsync(proposal, cancellationToken); }
        catch (Exception)
        {
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "Client completion admission is unavailable. Its reported effect remains uncertain and will not be replayed."));
        }
        if (resolved.Error is not null)
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "A client result was reported after its execution contract, authority or lifetime changed. The effect is unverified and will not be replayed."));
        try { return await CompleteAdmittedAsync(proposal, acknowledgement, resolved.Admission!, cancellationToken); }
        catch (Exception)
        {
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "Client completion validation was interrupted. Its reported effect remains uncertain and will not be replayed."));
        }
    }

    internal async Task<WeaverResult<WeaverProposalExecutionResult>> CompleteAdmittedAsync(WeaverActionProposal proposal,
        WeaverClientAcknowledgement acknowledgement, WeaverToolAdmission admission, CancellationToken cancellationToken)
    {
        var prepared = proposal.PreparedInvocation!;
        if (admission.Tool is not IWeaverClientTool clientTool)
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
        if (completed.Error is { } ownerError)
            completed = completed with { Error = WeaverModuleCodes.Error(ownerError, admission.Descriptor.ModuleCodes, ownerOrigin: true) };
        if (!completed.Succeeded || completed.Value is null)
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                    "Client completion validation is unavailable. Its reported effect remains uncertain and will not be replayed."));
        var normalized = WeaverModuleCodes.Result(completed.Value, admission.Descriptor.ModuleCodes).Value;
        if (WeaverInvocationIntegrity.SuccessStatus(prepared, normalized, admission.Descriptor) != "executed")
            return WeaverResult<WeaverProposalExecutionResult>.Success(Uncertain(proposal,
                "The reported client effect did not validate against the exact prepared operation. Its outcome is uncertain and will not be replayed."));
        var result = FreezeResult(normalized);
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

    internal static WeaverToolResult FreezeResult(WeaverToolResult value) => value with
    {
        Payload = value.Payload is null ? null : WeaverInvocationIntegrity.Freeze(value.Payload),
        Receipt = value.Receipt is null ? null : value.Receipt with { Evidence = WeaverInvocationIntegrity.Freeze(value.Receipt.Evidence) }
    };
}
