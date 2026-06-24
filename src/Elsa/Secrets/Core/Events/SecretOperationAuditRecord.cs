namespace Elsa.Secrets.Core.Events;

public sealed record SecretOperationAuditRecord(
    string Operation,
    string SecretName,
    string Outcome,
    DateTimeOffset Timestamp,
    string? ActorId = null,
    string? Reason = null);
