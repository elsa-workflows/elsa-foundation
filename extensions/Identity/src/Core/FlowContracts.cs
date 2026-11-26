using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Core;

public enum AuthenticationArtifactPurpose { SignIn = 1, Preview = 2, PreviewResult = 3 }

/// <summary>Immutable, bounded storage envelope. IDs and browser/admin bindings are hashes; payload is protected.</summary>
public sealed record AuthenticationArtifact(string Id, string TenantId, AuthenticationArtifactPurpose Purpose,
    string BrowserBinding, string? AdministratorBinding, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string ProtectedPayload);

[ReplacementContract]
public interface IAuthenticationArtifactStore
{
    bool IsDurable { get; }
    /// <exception cref="AuthenticationStoreException">Persistence failed; a mutation may have committed. Cancellation is propagated unchanged.</exception>
    ValueTask<bool> TryCreateAsync(AuthenticationArtifact artifact, CancellationToken cancellationToken = default);
    /// <summary>Returns a matching unexpired artifact to exactly one caller; wrong bindings do not consume it.</summary>
    /// <exception cref="AuthenticationStoreException">Persistence failed; do not issue authentication or replay a consume acknowledgement.</exception>
    ValueTask<AuthenticationArtifact?> TryConsumeAsync(string tenantId, string id, AuthenticationArtifactPurpose purpose,
        string browserBinding, string? administratorBinding, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public enum ExternalFlowFailure
{
    None, Unavailable, Denied, ConnectionUnavailable, StaleConnection, InvalidReturnUrl, InvalidCallback,
    InvalidState, ExpiredOrUsed, ProviderFailed, AdmissionRejected, Conflict, InvalidResult
}

/// <summary>API supplies key/return URL; purpose and browser binding come from the trusted route/cookie.</summary>
public sealed record ExternalFlowStart(string ConnectionKey, AuthenticationArtifactPurpose Purpose, string BrowserBinding,
    string ReturnUrl = "/", string? ConnectionRecordId = null);
public sealed record ExternalFlowStarted(string? AuthorizationUrl, ExternalFlowFailure Failure = ExternalFlowFailure.None,
    ExternalProtocolFailure ProtocolFailure = ExternalProtocolFailure.None)
{
    /// <summary>Server-only correlation handle, also carried by the authorization URL.</summary>
    public string? State { get; init; }
}

public sealed record ExternalFlowCallback(string ConnectionKey, AuthenticationArtifactPurpose Purpose, string State,
    string BrowserBinding, string? Code, string? ResponseIssuer = null, bool ProviderError = false);
/// <summary>Verified server-only origin. Internal credential binding is never a public session or administration field.</summary>
public sealed record ExternalAuthenticationProvenance(string ConnectionKey, string ConnectionRecordId, string Issuer, string Subject)
{
    /// <summary>Issued durable Elsa session, supplied only by canonical sign-in after protocol completion.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AuthenticationSessionId { get; init; }
    public string? MaterialRevision { get; init; }
    public string? CredentialBinding { get; init; }
    public string? RedirectUri { get; init; }
}
/// <summary>A failed preview can carry its protected, currently allowed return URL after state and actor validation.
/// Untrusted/unbound state carries no return URL; normal sign-in failures never redirect.</summary>
public sealed record ExternalFlowCompleted(string? UserId = null, string? ReturnUrl = null, string? PreviewResultHandle = null,
    ExternalAuthenticationProvenance? Provenance = null, ExternalFlowFailure Failure = ExternalFlowFailure.None,
    ExternalProtocolFailure ProtocolFailure = ExternalProtocolFailure.None, ExternalAdmissionFailure AdmissionFailure = ExternalAdmissionFailure.None);

public sealed record ExternalAuthenticationPreview(string ConnectionKey, string ConnectionRecordId, string MaterialRevision,
    string Issuer, string Subject, ExternalAdmissionKind Admission, string? ExistingUserId, string? UserName, string? DisplayName,
    string? Email, IReadOnlyList<string> RoleIds, IReadOnlyList<string> Permissions, ExternalAdmissionFailure AdmissionFailure,
    DateTimeOffset ObservedAt, bool Stale = false);
public sealed record ExternalPreviewResult(ExternalAuthenticationPreview? Preview, ExternalFlowFailure Failure = ExternalFlowFailure.None);

[ReplacementContract]
public interface IExternalAuthenticationFlowService
{
    string? GetCallbackUri(string connectionKey, AuthenticationArtifactPurpose purpose);
    ValueTask<ExternalFlowStarted> BeginAsync(ExternalFlowStart request, ClaimsPrincipal? administrator = null, CancellationToken cancellationToken = default);
    ValueTask<ExternalFlowCompleted> CompleteAsync(ExternalFlowCallback request, ClaimsPrincipal? administrator = null, CancellationToken cancellationToken = default);
    ValueTask<ExternalPreviewResult> ReadPreviewAsync(string connectionKey, string handle, string browserBinding,
        ClaimsPrincipal administrator, CancellationToken cancellationToken = default);
}
