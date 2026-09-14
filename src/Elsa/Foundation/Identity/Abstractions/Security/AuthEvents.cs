namespace Elsa.Foundation.Identity.Abstractions.Security;

public interface IAuthEventSink
{
    ValueTask EmitAsync(AuthEvent authEvent, CancellationToken cancellationToken = default);
}

public sealed record AuthEvent(
    string Id,
    DateTimeOffset Timestamp,
    AuthEventCategory Category,
    string Actor,
    string Target,
    AuthEventOutcome Outcome,
    string? TenantId,
    IReadOnlyDictionary<string, string> Detail);

public enum AuthEventCategory
{
    Authentication,
    Authorization,
    IdentityChange,
    CredentialLifecycle
}

public enum AuthEventOutcome
{
    Success,
    Denied,
    Failure
}
