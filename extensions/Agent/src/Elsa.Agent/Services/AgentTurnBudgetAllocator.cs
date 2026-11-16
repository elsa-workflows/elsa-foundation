using System.Security.Cryptography;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Services;

internal sealed class AgentTurnBudgetAllocator(IAgentTurnStore store, TimeProvider time)
{
    internal AgentTurnBudgetAuthority Create(AgentTurnDocument document, AgentTurnRequest request, Func<CancellationToken, ValueTask>? verifyNative = null)
    {
        var limits = AgentEffectiveLimits.For(request);
        if (!AgentTurnDeliveryRules.OwnsClaim(document, request.Identity) || document.MaximumModelCalls != limits.MaximumModelCalls || document.Fence is null || document.AdmissionGeneration < 1 || document.LeaseExpiresAt is null)
            throw AgentModelRunner.Failure("agent.budget.binding", "The request budget does not match the admitted native turn.");
        return new(store, time, document, request.Descriptor, limits, verifyNative);
    }
}

internal sealed class AgentTurnBudgetAuthority(IAgentTurnStore store, TimeProvider time, AgentTurnDocument document,
    AgentRuntimeDescriptor descriptor, AgentTurnLimits? effectiveLimits = null, Func<CancellationToken, ValueTask>? verifyNative = null) : IAgentModelDispatchAuthority
{
    private int maximumOutputTokens;
    private int reservations;
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly List<AgentModelRequestReservation> reserved = [];
    private AgentModelResponseObservation? completed;
    internal int Reservations => Volatile.Read(ref reservations);
    internal IReadOnlyList<AgentModelRequestReservation> ModelRequests => reserved.ToArray();
    internal AgentModelResponseObservation? ResponseObservation => completed;
    // Attribution is qualified only for one physical request. An SDK response spanning more
    // requests cannot release their conservative ceilings based on an unattributed usage value.
    internal bool IsOutputUsageKnown => reserved.Count == 1 && completed?.OutputTokens is not null;
    internal long AccountedOutputTokens => IsOutputUsageKnown ? completed!.OutputTokens!.Value : reserved.Sum(x => (long)x.MaximumOutputTokens);

    internal void BindOutputLimit(int? limit)
    {
        if (limit is not (>= 1 and <= 1_000_000) || descriptor.Limits.MaximumOutputTokens is { } published && limit > published ||
            effectiveLimits?.MaximumOutputTokens is { } requested && limit > requested)
            throw AgentModelRunner.Failure("agent.budget.output", "The model has no enforceable finite output limit for this turn.");
        if (Interlocked.CompareExchange(ref maximumOutputTokens, limit.Value, 0) != 0)
            throw AgentModelRunner.Failure("agent.budget.binding", "The admitted output limit is already bound.");
    }

    public async ValueTask BeforeSendAsync(AgentModelDispatchObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A scoped store/context is not a concurrent service. Independent hosts still compete
        // through the database CAS; sends within this lease serialize their metadata admission.
        await admission.WaitAsync(cancellationToken);
        try
        {
            if (verifyNative is not null) await verifyNative(cancellationToken);
            await ReserveAsync(observation, cancellationToken);
            // A native cancellation can commit while reservation I/O is in progress. Recheck
            // immediately before giving the provider permission to send; retain any reservation.
            if (verifyNative is not null) await verifyNative(cancellationToken);
        }
        finally { admission.Release(); }
    }

    private async ValueTask ReserveAsync(AgentModelDispatchObservation observation, CancellationToken cancellationToken)
    {
        var output = Volatile.Read(ref maximumOutputTokens);
        if (output < 1 || observation.ProviderKey != descriptor.Model.ProviderKey || observation.ConfiguredModelId != descriptor.Model.ModelId ||
            observation.CapabilitiesFingerprint != descriptor.CapabilitiesFingerprint)
            throw AgentModelRunner.Failure("agent.budget.binding", "The physical request does not match the admitted model budget.");
        var now = time.GetUtcNow();
        if (now >= document.Deadline)
            throw AgentModelRunner.Failure("agent.turn.deadline", "The agent deadline expired before its physical request.");
        var reservation = new AgentModelRequestReservation(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), output, now, observation);
        if (await store.ReserveModelRequestAsync(document.Handle, document.Identity, AgentTurnLeaseRules.Token(document), reservation, now, cancellationToken) is null)
        {
            var current = await store.ReadAsync(document.Handle, document.Identity, cancellationToken);
            if (current is null || current.Status != AgentTurnStatus.Dispatching || !AgentTurnLeaseRules.Live(current, AgentTurnLeaseRules.Token(document), now))
                throw AgentModelRunner.Failure("agent.turn.fence", "The physical model request has no current journal dispatch fence.");
            throw AgentModelRunner.Failure("agent.budget.calls", "The physical model request has no remaining budget reservation; dispatch was refused.");
        }
        Interlocked.Increment(ref reservations);
        reserved.Add(reservation);
    }

    internal async ValueTask CompleteResponseAsync(UsageDetails? usage, string? observedModelId, CancellationToken cancellationToken)
    {
        await admission.WaitAsync(cancellationToken);
        try
        {
            if (reserved.Count == 0 || completed is not null)
                throw AgentModelRunner.Failure("agent.budget.protocol", "The response does not have one admitted physical request set.");
            var now = time.GetUtcNow();
            var invalidUsage = usage?.InputTokenCount is < 0 || usage?.OutputTokenCount is < 0 || usage?.TotalTokenCount is < 0;
            var invalidModelId = observedModelId is { } model && (string.IsNullOrWhiteSpace(model) || model.Length > 512 || model.Any(char.IsControl));
            var observation = new AgentModelResponseObservation(now, usage is not null,
                invalidUsage ? null : usage?.InputTokenCount, invalidUsage ? null : usage?.OutputTokenCount,
                invalidUsage ? null : usage?.TotalTokenCount, invalidModelId ? null : observedModelId);
            if (!await store.CompleteModelResponseAsync(document.Handle, document.Identity, AgentTurnLeaseRules.Token(document), reserved.Select(x => x.Id).ToArray(), observation, now, cancellationToken))
                throw AgentModelRunner.Failure("agent.turn.fence", "The response observation cannot commit under this journal fence.");
            completed = observation;
            if (invalidUsage || invalidModelId)
                throw AgentModelRunner.Failure("agent.result.metadata", "The provider returned invalid usage or model metadata; reservations remain conservative.");
            if (observation.OutputTokens is { } output && output > reserved.Sum(x => (long)x.MaximumOutputTokens))
                throw AgentModelRunner.Failure("agent.budget.output-overrun", "The provider reported output usage above the admitted output ceiling.");
        }
        finally { admission.Release(); }
    }
}
