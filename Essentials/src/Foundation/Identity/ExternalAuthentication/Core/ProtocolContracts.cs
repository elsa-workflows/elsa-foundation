using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Core;

public enum ExternalProtocolFailure
{
    None, InvalidConfiguration, SecretUnavailable, RemoteUnavailable, InvalidDiscovery, UntrustedEndpoint,
    UnsupportedClientAuthentication, ProviderChanged, SecretChanged, UpstreamRejected, InvalidToken, InvalidResponse
}

/// <summary>Server-only check. Credential binding is opaque internal evidence and must never enter management projections.</summary>
public sealed record ExternalProtocolCheck(ExternalProtocolFailure Failure = ExternalProtocolFailure.None)
{
    public string? Issuer { get; init; }
    public string? CredentialBinding { get; init; }
}

/// <summary>Server-generated parameters, retained only in protected flow state.</summary>
public sealed record ExternalProtocolChallengeRequest(string RedirectUri, string State, string Nonce, string CodeVerifier);
public sealed record ExternalProtocolChallenge(string? AuthorizationUrl = null, string? Issuer = null, string? CredentialBinding = null,
    ExternalProtocolFailure Failure = ExternalProtocolFailure.None);

/// <summary>Server-only callback context recovered from a consumed, browser-bound flow.</summary>
public sealed record ExternalProtocolCodeRequest(string RedirectUri, string Code, string Nonce, string CodeVerifier,
    string ExpectedIssuer, string CredentialBinding, string? ResponseIssuer = null);

/// <summary>Validated identity evidence only. Upstream credentials are never returned or retained.</summary>
public sealed record ExternalProtocolIdentity(ExternalIdentityKey? Identity, IReadOnlyList<Claim> Claims,
    ExternalProtocolFailure Failure = ExternalProtocolFailure.None);

/// <summary>Named installed protocol contribution. Normal admission and preview use the same validating adapter.</summary>
public interface IExternalAuthenticationProtocol
{
    string Name { get; }
    /// <summary>Local structure and current secret readiness; performs no remote authentication.</summary>
    ValueTask<ExternalProtocolCheck> CheckAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
    /// <summary>Bounded metadata/key test without provisioning or session mutation.</summary>
    ValueTask<ExternalProtocolCheck> TestAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
    ValueTask<ExternalProtocolChallenge> ChallengeAsync(ConnectionRecord connection, ExternalProtocolChallengeRequest request, CancellationToken cancellationToken = default);
    ValueTask<ExternalProtocolIdentity> RedeemAsync(ConnectionRecord connection, ExternalProtocolCodeRequest request, CancellationToken cancellationToken = default);
}
