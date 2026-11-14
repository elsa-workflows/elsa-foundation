using System.Reflection;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.OpenIddict;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

public sealed class TokenSubjectKeyCompatibilityTests
{
    [Fact]
    public void Issuer_qualified_keys_do_not_collapse_opaque_UTF16_values()
    {
        // Inspect the wire-key boundary directly: JWT serializers may reject malformed Unicode,
        // but distinct admitted .NET subjects must never hash to the same revocation scope.
        var type = typeof(OpenIddictTokenService).Assembly.GetType(
            "Elsa.Foundation.Identity.OpenIddict.TokenSubjectKey", throwOnError: true)!;
        var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Func<TokenSubject, string>>();
        var values = new[] { "\ud800", "\udfff", "\ufffd", "\u00e9", "e\u0301", "a\0b", "a\0B" };
        var subjects = values.Select(value => TokenSubject.ForProviderSubject("tenant", "idp", value, "https://issuer/"));
        var issuers = values.Select(value => TokenSubject.ForProviderSubject("tenant", "idp", "subject", value));
        Assert.Equal(values.Length, subjects.Select(create).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(values.Length, issuers.Select(create).Distinct(StringComparer.Ordinal).Count());
    }
}
