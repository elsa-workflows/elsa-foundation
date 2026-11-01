using System.Security.Cryptography;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Specialists.Services;

internal sealed class SpecialistTurnBudgetAllocator(ISpecialistTurnStore store, TimeProvider time)
{
    internal SpecialistTurnBudgetAuthority Create(SpecialistTurnDocument document, SpecialistTurnRequest request)
    {
        if (document.Identity != request.Identity || document.MaximumModelCalls != request.Descriptor.Limits.MaximumModelCalls || document.Fence is null)
            throw SpecialistModelRunner.Failure("specialist.budget.binding", "The request budget does not match the admitted native turn.");
        return new(store, time, document, request.Descriptor);
    }
}

internal sealed class SpecialistTurnBudgetAuthority(ISpecialistTurnStore store, TimeProvider time, SpecialistTurnDocument document,
    SpecialistRuntimeDescriptor descriptor) : ISpecialistModelDispatchAuthority
{
    private int maximumOutputTokens;
    private int reservations;
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly List<SpecialistModelRequestReservation> reserved = [];
    private SpecialistModelResponseObservation? completed;
    internal int Reservations => Volatile.Read(ref reservations);
    internal IReadOnlyList<SpecialistModelRequestReservation> ModelRequests => reserved.ToArray();
    internal SpecialistModelResponseObservation? ResponseObservation => completed;
    // Attribution is qualified only for one physical request. An SDK response spanning more
    // requests cannot release their conservative ceilings based on an unattributed usage value.
    internal bool IsOutputUsageKnown => reserved.Count == 1 && completed?.OutputTokens is not null;
    internal long AccountedOutputTokens => IsOutputUsageKnown ? completed!.OutputTokens!.Value : reserved.Sum(x => (long)x.MaximumOutputTokens);

    internal void BindOutputLimit(int? limit)
    {
        if (limit is not (>= 1 and <= 1_000_000) || descriptor.Limits.MaximumOutputTokens is { } published && limit > published)
            throw SpecialistModelRunner.Failure("specialist.budget.output", "The model has no enforceable finite output limit for this turn.");
        if (Interlocked.CompareExchange(ref maximumOutputTokens, limit.Value, 0) != 0)
            throw SpecialistModelRunner.Failure("specialist.budget.binding", "The admitted output limit is already bound.");
    }

    public async ValueTask BeforeSendAsync(SpecialistModelDispatchObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A scoped store/context is not a concurrent service. Independent hosts still compete
        // through the database CAS; sends within this lease serialize their metadata admission.
        await admission.WaitAsync(cancellationToken);
        try
        {
            await ReserveAsync(observation, cancellationToken);
        }
        finally { admission.Release(); }
    }

    private async ValueTask ReserveAsync(SpecialistModelDispatchObservation observation, CancellationToken cancellationToken)
    {
        var output = Volatile.Read(ref maximumOutputTokens);
        if (output < 1 || observation.ProviderKey != descriptor.Model.ProviderKey || observation.ConfiguredModelId != descriptor.Model.ModelId ||
            observation.CapabilitiesFingerprint != descriptor.CapabilitiesFingerprint)
            throw SpecialistModelRunner.Failure("specialist.budget.binding", "The physical request does not match the admitted model budget.");
        var now = time.GetUtcNow();
        if (now >= document.Deadline)
            throw SpecialistModelRunner.Failure("specialist.turn.deadline", "The specialist deadline expired before its physical request.");
        var reservation = new SpecialistModelRequestReservation(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), output, now, observation);
        if (await store.ReserveModelRequestAsync(document.Handle, document.Identity, document.Fence!, reservation, now, cancellationToken) is null)
        {
            var current = await store.ReadAsync(document.Handle, document.Identity, cancellationToken);
            if (current is null || current.Status != SpecialistTurnStatus.Dispatching || current.Fence != document.Fence)
                throw SpecialistModelRunner.Failure("specialist.turn.fence", "The physical model request has no current journal dispatch fence.");
            throw SpecialistModelRunner.Failure("specialist.budget.calls", "The physical model request has no remaining budget reservation; dispatch was refused.");
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
                throw SpecialistModelRunner.Failure("specialist.budget.protocol", "The response does not have one admitted physical request set.");
            var now = time.GetUtcNow();
            var invalidUsage = usage?.InputTokenCount is < 0 || usage?.OutputTokenCount is < 0 || usage?.TotalTokenCount is < 0;
            var invalidModelId = observedModelId is { } model && (string.IsNullOrWhiteSpace(model) || model.Length > 512 || model.Any(char.IsControl));
            var observation = new SpecialistModelResponseObservation(now, usage is not null,
                invalidUsage ? null : usage?.InputTokenCount, invalidUsage ? null : usage?.OutputTokenCount,
                invalidUsage ? null : usage?.TotalTokenCount, invalidModelId ? null : observedModelId);
            if (!await store.CompleteModelResponseAsync(document.Handle, document.Identity, document.Fence!, reserved.Select(x => x.Id).ToArray(), observation, now, cancellationToken))
                throw SpecialistModelRunner.Failure("specialist.turn.fence", "The response observation cannot commit under this journal fence.");
            completed = observation;
            if (invalidUsage || invalidModelId)
                throw SpecialistModelRunner.Failure("specialist.result.metadata", "The provider returned invalid usage or model metadata; reservations remain conservative.");
            if (observation.OutputTokens is { } output && output > reserved.Sum(x => (long)x.MaximumOutputTokens))
                throw SpecialistModelRunner.Failure("specialist.budget.output-overrun", "The provider reported output usage above the admitted output ceiling.");
        }
        finally { admission.Release(); }
    }
}
