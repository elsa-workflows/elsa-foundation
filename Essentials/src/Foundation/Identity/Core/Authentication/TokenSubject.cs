using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.Core.Authentication;

/// <summary>
/// Explicit identity scope shared by token issuance and revocation. An Elsa user is scoped by tenant;
/// a non-user subject additionally requires its provider and validated issuer. Values are opaque.
/// </summary>
public sealed record TokenSubject
{
    public TokenSubject(string tenantId, string subjectId, string? provider = null, string? issuer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        if (provider is not null || issuer is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        }

        TenantId = tenantId;
        SubjectId = subjectId;
        Provider = provider;
        Issuer = issuer;
    }

    public string TenantId { get; }
    public string SubjectId { get; }
    public string? Provider { get; }
    public string? Issuer { get; }
    public string? CanonicalUserId => Provider is null ? SubjectId : null;
}

/// <summary>Resolves an unambiguous token identity from a provider-owned normalized principal.</summary>
[ReplacementContract]
public interface ITokenSubjectResolver
{
    bool TryResolve(ClaimsPrincipal principal, [NotNullWhen(true)] out TokenSubject? subject);
}
