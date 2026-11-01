namespace Elsa.Agent.Specialists.Core.Models;

// Shared by the actual in-memory and relational adapters; no provider type crosses this boundary.
internal static class SpecialistTurnDocumentRules
{
    internal static bool IsToken(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static void Prepared(SpecialistTurnDocument document)
    {
        Validate(document);
        if (document.Status != SpecialistTurnStatus.Prepared || document.ModelRequests is { Count: > 0 } || document.ResponseObservation is not null)
            throw new InvalidDataException("Only a prepared specialist operation can be staged.");
    }

    internal static void Validate(SpecialistTurnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Identity);
        Token(document.Handle); Token(document.Key); Token(document.RequestHash);
        if (document.Key != SpecialistTurnDocument.CalculateOperationKey(document.Identity))
            throw new InvalidDataException("The specialist operation key does not match its native scope and operation.");
        if (!Enum.IsDefined(document.Status) || string.IsNullOrWhiteSpace(document.ProtectedRequest) ||
            document.Status == SpecialistTurnStatus.Accepted && string.IsNullOrWhiteSpace(document.ProtectedResult) ||
            document.Status != SpecialistTurnStatus.Accepted && document.ProtectedResult is not null ||
            document.Status == SpecialistTurnStatus.Prepared && (document.ChildInvocationId is not null || document.Fence is not null) ||
            document.Status != SpecialistTurnStatus.Prepared && (string.IsNullOrWhiteSpace(document.ChildInvocationId) || string.IsNullOrWhiteSpace(document.Fence)) ||
            document.Status is SpecialistTurnStatus.Failed or SpecialistTurnStatus.OutcomeUnknown && string.IsNullOrWhiteSpace(document.FailureCode) ||
            document.Status is not (SpecialistTurnStatus.Failed or SpecialistTurnStatus.OutcomeUnknown) && document.FailureCode is not null)
            throw new InvalidDataException("The specialist journal lifecycle record is invalid.");
        if (document.Fence is { } fence) Fence(fence);
        if (document.MaximumModelCalls is < 1 or > 32 || document.ModelRequests is { } requests &&
            (requests.Count > document.MaximumModelCalls || requests.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != requests.Count))
            throw new InvalidDataException("The specialist journal request budget is invalid.");
        foreach (var reservation in document.ModelRequests ?? []) Reservation(reservation, document.Deadline);
        if (document.ResponseObservation is { } response)
        {
            if (document.Status is SpecialistTurnStatus.Prepared or SpecialistTurnStatus.Claimed || document.ModelRequests is not { Count: > 0 } ||
                response.ObservedAt >= document.Deadline || response.InputTokens is < 0 || response.OutputTokens is < 0 || response.TotalTokens is < 0 ||
                !response.UsageAvailable && (response.InputTokens is not null || response.OutputTokens is not null || response.TotalTokens is not null) ||
                response.ObservedModelId is { } model && (string.IsNullOrWhiteSpace(model) || model.Length > 512 || model.Any(char.IsControl)))
                throw new InvalidDataException("The specialist model response observation is invalid.");
        }
    }

    internal static SpecialistTurnDocument Copy(SpecialistTurnDocument document) => document with { ModelRequests = document.ModelRequests?.ToArray() };

    internal static void Reservation(SpecialistModelRequestReservation reservation, DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        Token(reservation.Id);
        if (reservation.MaximumOutputTokens is < 1 or > 1_000_000 || reservation.ReservedAt >= deadline ||
            reservation.Observation is not { ConnectionVersion: > 0 } observation ||
            string.IsNullOrWhiteSpace(observation.ProviderKey) || string.IsNullOrWhiteSpace(observation.ConfiguredModelId) ||
            string.IsNullOrWhiteSpace(observation.ConnectionIncarnation) || string.IsNullOrWhiteSpace(observation.CapabilitiesFingerprint) ||
            string.IsNullOrWhiteSpace(observation.ProtocolProfile))
            throw new InvalidDataException("The specialist physical request reservation is invalid.");
    }

    internal static void Token(string value)
    {
        if (!IsToken(value))
            throw new ArgumentException("The specialist journal identity is not a canonical opaque token.");
    }
    internal static void Fence(string value)
    {
        if (value is null || value.Length is < 1 or > 64 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("The specialist journal fence is invalid.");
    }
}
