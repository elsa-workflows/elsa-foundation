using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class RuntimeIngressReservationRules
{
    internal static IReadOnlyList<RuntimeIngressReservation> Prepare(TriggerScope scope, string ownerKind, string ownerId,
        long revision, long configurationRevision, IReadOnlyList<RuntimeIngressReservationDescriptor> claims)
    {
        if (ownerKind is not (RuntimeIngressReservation.StandaloneTriggerOwner or RuntimeIngressReservation.PublicationSlotOwner))
            throw new ArgumentException("The reservation owner kind is unsupported.", nameof(ownerKind));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configurationRevision);
        var result = new List<RuntimeIngressReservation>();
        foreach (var claim in claims)
        {
            ArgumentNullException.ThrowIfNull(claim);
            ArgumentException.ThrowIfNullOrWhiteSpace(claim.StimulusType); ArgumentException.ThrowIfNullOrWhiteSpace(claim.StimulusHash);
            if (claim.StimulusType.Length > 256 || claim.StimulusHash.Length > 450 || claim.Metadata.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
                throw new ArgumentException("A reservation identity or metadata is invalid.", nameof(claims));
            result.Add(new(RuntimeIngressReservation.CreateIdentity(scope.Persistence, claim.StimulusType, claim.StimulusHash),
                scope, claim.StimulusType, claim.StimulusHash, ownerKind, ownerId, revision, configurationRevision,
                new ReadOnlyDictionary<string, string>(claim.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))));
        }
        if (result.Select(item => item.ReservationId).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new ArgumentException("The source plan contains duplicate exclusive reservations.", nameof(claims));
        return result;
    }

    internal static void CheckOwner(RuntimeIngressReservation existing, RuntimeIngressReservation candidate)
    {
        if (existing.ReservationId != candidate.ReservationId || existing.Scope.Persistence != candidate.Scope.Persistence ||
            existing.StimulusType != candidate.StimulusType || existing.StimulusHash != candidate.StimulusHash)
            throw new InvalidDataException("A reservation hash does not match its exact ingress namespace.");
        // Publication slots retain physical ownership while a replacement changes captured delivery tenancy.
        // Resource owners remain tenant-scoped; a different slot or owner kind never inherits the route.
        if (existing.OwnerKind != candidate.OwnerKind || existing.OwnerId != candidate.OwnerId ||
            existing.Scope.TenantId != candidate.Scope.TenantId && existing.OwnerKind != RuntimeIngressReservation.PublicationSlotOwner)
            throw new TriggerOperationException("route-conflict", "Another source owns this endpoint reservation.");
    }

    internal static void CheckAdmission(RuntimeIngressAdmissionRequest request, RuntimeIngressReservation? actual)
    {
        if (request.Reservation is not { } expected || actual is null || !Same(actual, expected) ||
            expected.Scope != request.Scope || expected.OwnerKind != RuntimeIngressReservation.StandaloneTriggerOwner ||
            expected.OwnerId != request.Candidate.StandaloneIngress!.TriggerId || expected.OwnerRevision != request.ExpectedTriggerRevision)
            throw new TriggerOperationException("conflict", "The matched source reservation changed before admission.");
    }

    internal static bool Same<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
}
