using System.Security.Cryptography;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;

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
    internal int Reservations => Volatile.Read(ref reservations);

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
    }
}
