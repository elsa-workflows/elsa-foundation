using System.Collections.Concurrent;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Specialists.Tests.Recovery;

internal sealed class LeaseTrace
{
    internal bool RejectRenewal { get; init; }
    internal bool LoseRenewalResponse { get; init; }
    internal TaskCompletionSource<SpecialistTurnDocument?> Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<SpecialistTurnDocument> ClaimedRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentQueue<(Guid Store, Guid? Context)> Renewals { get; } = new();
    internal ConcurrentQueue<(Guid Store, Guid? Context)> Sends { get; } = new();

    internal void Decorate(IServiceCollection services)
    {
        var registration = services.Single(x => x.ServiceType == typeof(ISpecialistTurnStore));
        services.Remove(registration);
        services.Add(new ServiceDescriptor(typeof(ISpecialistTurnStore), provider =>
        {
            var inner = registration.ImplementationInstance ?? registration.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!);
            return new LeaseTraceStore((ISpecialistTurnStore)inner, this, provider.GetService<SpecialistsDbContext>()?.ContextId.InstanceId);
        }, registration.Lifetime));
    }
}

internal sealed class LeaseTraceStore(ISpecialistTurnStore inner, LeaseTrace trace, Guid? contextId) : ISpecialistTurnStore
{
    private readonly Guid id = Guid.NewGuid();
    public ValueTask<SpecialistJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => inner.GetCapabilitiesAsync(cancellationToken);
    public ValueTask<SpecialistTurnDocument> StageAsync(SpecialistTurnDocument candidate, CancellationToken cancellationToken) => inner.StageAsync(candidate, cancellationToken);
    public async ValueTask<SpecialistTurnDocument?> ReadAsync(string handle, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        var document = await inner.ReadAsync(handle, identity, cancellationToken);
        if (document?.Status is SpecialistTurnStatus.Claimed or SpecialistTurnStatus.Dispatching) trace.ClaimedRead.TrySetResult(document);
        return document;
    }
    public ValueTask<SpecialistConversationDocument?> ReadConversationAsync(string conversationId, SpecialistInvocationIdentity identity, CancellationToken cancellationToken) => inner.ReadConversationAsync(conversationId, identity, cancellationToken);
    public ValueTask<SpecialistTurnDocument?> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken) => inner.ClaimAsync(handle, identity, childInvocationId, fence, now, cancellationToken);
    public async ValueTask<SpecialistTurnDocument?> RenewAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        trace.Renewals.Enqueue((id, contextId));
        var result = trace.RejectRenewal ? null : await inner.RenewAsync(handle, identity, lease, now, cancellationToken);
        trace.Renewed.TrySetResult(result);
        if (trace.LoseRenewalResponse) throw new IOException("The committed renewal response was lost.");
        return result;
    }
    public ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken) => inner.DispatchAsync(handle, identity, lease, now, cancellationToken);
    public ValueTask<SpecialistModelRequestReservation?> ReserveModelRequestAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, SpecialistModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        trace.Sends.Enqueue((id, contextId));
        return inner.ReserveModelRequestAsync(handle, identity, lease, reservation, now, cancellationToken);
    }
    public ValueTask<bool> CompleteModelResponseAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, IReadOnlyList<string> requestIds, SpecialistModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken) => inner.CompleteModelResponseAsync(handle, identity, lease, requestIds, observation, now, cancellationToken);
    public ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken) => inner.AcceptAsync(handle, identity, lease, protectedResult, now, cancellationToken);
    public ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, SpecialistTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken) => inner.FailAsync(handle, identity, lease, code, outcomeUnknown, cancellationToken);
}
