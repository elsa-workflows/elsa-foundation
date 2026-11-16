using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.Core.Authentication;

/// <summary>Resolves an unambiguous token identity from one provider-owned normalized principal.</summary>
[ReplacementContract]
public interface ITokenSubjectResolver
{
    bool TryResolve(ClaimsPrincipal principal, [NotNullWhen(true)] out TokenSubject? subject);
}
