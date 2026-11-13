using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class InMemoryIngressReservationReader(InMemoryRuntimeCheckpointStoreState state,
    IPersistenceAccessContextAccessor access) : IRuntimeIngressReservationReader
{
    public bool SupportsReservations => true;
    public ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        Require(scope.Persistence, cancellationToken);
        lock (state.SyncRoot)
        {
            var found = state.IngressReservations.GetValueOrDefault(RuntimeIngressReservation.CreateIdentity(scope.Persistence, stimulusType, stimulusHash));
            return ValueTask.FromResult(found?.Scope == scope ? found : null);
        }
    }
    public async ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
    {
        var page = await ListInScopeAsync(scope.Persistence, query, cancellationToken);
        return new(page.Items.Where(item => item.Scope == scope).ToArray(), page.Next);
    }
    public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken); RuntimeStorePageRequest.ValidateLimit(query.Limit, nameof(query));
        lock (state.SyncRoot)
        {
            var page = state.IngressReservations.Values.Where(item => item.Scope.Persistence == scope && item.StimulusType == query.StimulusType &&
                    (query.StimulusHash is null || item.StimulusHash == query.StimulusHash) &&
                    (query.After is null || StringComparer.Ordinal.Compare(item.ReservationId, query.After) > 0))
                .OrderBy(item => item.ReservationId, StringComparer.Ordinal).Take(query.Limit + 1).ToArray();
            return ValueTask.FromResult(new RuntimeIngressReservationPage(page.Take(query.Limit).ToArray(), page.Length > query.Limit ? page[query.Limit - 1].ReservationId : null));
        }
    }
    private void Require(PersistenceScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (access.Current.RequireScope() != scope) throw new InvalidOperationException("Reservation reads require their actual persistence scope.");
    }
}

internal sealed partial class InMemoryRuntimeIngressDeliveryStore
{
    partial void CheckReservationAdmission(RuntimeIngressAdmissionRequest request, ref bool checkedReservation)
    {
        var expected = request.Reservation ?? throw new InvalidDataException("A routed source has no reservation.");
        RuntimeIngressReservationRules.CheckAdmission(request, State.IngressReservations.GetValueOrDefault(expected.ReservationId));
        checkedReservation = true;
    }
}
