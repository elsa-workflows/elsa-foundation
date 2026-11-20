using System.Collections.Concurrent;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests.Recovery;

internal sealed class LeaseTrace
{
    internal Func<ValueTask>? AfterReserved { get; set; }
    internal Func<ValueTask>? AfterDispatched { get; set; }
    internal Func<ValueTask>? AfterClaimed { get; set; }
    internal Func<ValueTask>? AfterAcceptedRead { get; set; }
    internal bool HoldCancellation { get; set; }
    internal bool LoseCancellationResponse { get; init; }
    internal int CancellationAttempts;
    internal int CancellationResponsesLost;
    internal ConcurrentQueue<AgentCancellationReceipt> Cancellations { get; } = new();
    internal bool HoldApplication { get; set; }
    internal bool LoseApplicationResponse { get; init; }
    internal int ApplicationAttempts;
    internal int ApplicationResponsesLost;
    internal bool RejectRenewal { get; init; }
    internal bool LoseAcceptanceResponse { get; init; }
    internal bool FailBeforeAcceptance { get; init; }
    internal Func<ValueTask>? AfterAccepted { get; set; }
    internal Func<AgentInvocationIdentity, ValueTask>? AfterReplayBound { get; set; }
    internal bool ChangeBindingAtChildCompletion { get; init; }
    internal bool AlwaysLoseAcceptedReceiptRead { get; set; }
    internal bool LoseAcceptedReceiptRead { get; init; }
    internal int AcceptedReceiptReadsLost;
    internal int AcceptedResponsesLost;
    internal bool BlockRenewal { get; init; }
    internal TaskCompletionSource RenewalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool LoseRenewalResponse { get; init; }
    internal TaskCompletionSource<AgentTurnDocument?> Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<AgentTurnDocument> ClaimedRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentQueue<(Guid Store, Guid? Context)> Renewals { get; } = new();
    internal ConcurrentQueue<(Guid Store, Guid? Context)> Sends { get; } = new();

    internal void Decorate(IServiceCollection services)
    {
        var registration = services.Single(x => x.ServiceType == typeof(IAgentTurnStore));
        services.Remove(registration);
        services.Add(new ServiceDescriptor(typeof(IAgentTurnStore), provider =>
        {
            var inner = registration.ImplementationInstance ?? registration.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!);
            return new LeaseTraceStore((IAgentTurnStore)inner, this, provider.GetService<AgentDbContext>()?.ContextId.InstanceId);
        }, registration.Lifetime));
    }
}

internal sealed class LeaseTraceStore(IAgentTurnStore inner, LeaseTrace trace, Guid? contextId) : IAgentTurnStore
{
    private readonly Guid id = Guid.NewGuid();
    public ValueTask<AgentRetentionPage> QueryRetentionAsync(AgentRetentionQuery query, CancellationToken cancellationToken) => inner.QueryRetentionAsync(query, cancellationToken);
    public ValueTask<bool> PurgeContentAsync(AgentRetentionCandidate expected, DateTimeOffset now, CancellationToken cancellationToken) => inner.PurgeContentAsync(expected, now, cancellationToken);
    public ValueTask<AgentJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => inner.GetCapabilitiesAsync(cancellationToken);
    public ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, CancellationToken cancellationToken) => inner.StageAsync(candidate, cancellationToken);
    public ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, bool allowNewOperation, CancellationToken cancellationToken) => inner.StageAsync(candidate, allowNewOperation, cancellationToken);
    public async ValueTask<AgentTurnDocument?> ReadAsync(string handle, AgentInvocationIdentity identity, CancellationToken cancellationToken)
    {
        var document = await inner.ReadAsync(handle, identity, cancellationToken);
        if (document?.Status == AgentTurnStatus.Accepted && (trace.AlwaysLoseAcceptedReceiptRead || trace.LoseAcceptedReceiptRead && Interlocked.Increment(ref trace.AcceptedReceiptReadsLost) == 1))
            throw new IOException("The accepted receipt could not be read by the native facade.");
        if (document?.Status == AgentTurnStatus.Accepted && trace.AfterAcceptedRead is { } afterRead) await afterRead();
        if (document?.Status is AgentTurnStatus.Claimed or AgentTurnStatus.Dispatching) trace.ClaimedRead.TrySetResult(document);
        return document;
    }
    public ValueTask<AgentConversationDocument?> ReadConversationAsync(string conversationId, AgentInvocationIdentity identity, CancellationToken cancellationToken) => inner.ReadConversationAsync(conversationId, identity, cancellationToken);
    public async ValueTask<AgentTurnDocument?> ClaimAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var claimed = await inner.ClaimAsync(handle, identity, childInvocationId, fence, now, cancellationToken);
        if (claimed is not null && trace.AfterClaimed is { } callback) await callback();
        return claimed;
    }
    public async ValueTask<AgentTurnDocument?> ApplyAsync(string handle, AgentTurnApplication application, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref trace.ApplicationAttempts);
        if (trace.HoldApplication) throw new IOException("Settlement delivery failed before its journal write.");
        var result = await inner.ApplyAsync(handle, application, cancellationToken);
        if (result is not null && trace.LoseApplicationResponse && Interlocked.Increment(ref trace.ApplicationResponsesLost) == 1)
            throw new IOException("The committed settlement acknowledgement was lost.");
        return result;
    }
    public async ValueTask<AgentCancellationReceipt> CancelAsync(AgentTurnCancellation cancellation, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref trace.CancellationAttempts);
        if (trace.HoldCancellation) throw new IOException("Cancellation delivery failed before its journal write.");
        var receipt = await inner.CancelAsync(cancellation, cancellationToken);
        trace.Cancellations.Enqueue(receipt);
        if (trace.LoseCancellationResponse && Interlocked.Increment(ref trace.CancellationResponsesLost) == 1)
            throw new IOException("The committed cancellation acknowledgement was lost.");
        return receipt;
    }
    public async ValueTask<AgentTurnDocument?> RenewAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        trace.Renewals.Enqueue((id, contextId));
        trace.RenewalStarted.TrySetResult();
        if (trace.BlockRenewal) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var result = trace.RejectRenewal ? null : await inner.RenewAsync(handle, identity, lease, now, cancellationToken);
        trace.Renewed.TrySetResult(result);
        if (trace.LoseRenewalResponse) throw new IOException("The committed renewal response was lost.");
        return result;
    }
    public async ValueTask<AgentTurnDocument?> ReplayAcceptedAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken)
    {
        var result = await inner.ReplayAcceptedAsync(handle, identity, childInvocationId, cancellationToken);
        if (result is not null && trace.AfterReplayBound is { } callback) await callback(identity);
        return result;
    }
    public async ValueTask<bool> DispatchAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var dispatched = await inner.DispatchAsync(handle, identity, lease, now, cancellationToken);
        if (dispatched && trace.AfterDispatched is { } callback) await callback();
        return dispatched;
    }
    public async ValueTask<AgentModelRequestReservation?> ReserveModelRequestAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, AgentModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        trace.Sends.Enqueue((id, contextId));
        var admitted = await inner.ReserveModelRequestAsync(handle, identity, lease, reservation, now, cancellationToken);
        if (admitted is not null && trace.AfterReserved is { } callback) await callback();
        return admitted;
    }
    public ValueTask<bool> CompleteModelResponseAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, IReadOnlyList<string> requestIds, AgentModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken) => inner.CompleteModelResponseAsync(handle, identity, lease, requestIds, observation, now, cancellationToken);
    public async ValueTask<bool> AcceptAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (trace.FailBeforeAcceptance) throw new IOException("The acceptance request failed before committing.");
        var accepted = await inner.AcceptAsync(handle, identity, lease, protectedResult, now, cancellationToken);
        if (accepted && trace.AfterAccepted is { } callback) await callback();
        if (accepted && trace.LoseAcceptanceResponse && Interlocked.Increment(ref trace.AcceptedResponsesLost) == 1) throw new IOException("The committed acceptance response was lost.");
        return accepted;
    }
    public ValueTask FailAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken) => inner.FailAsync(handle, identity, lease, code, outcomeUnknown, cancellationToken);
}
