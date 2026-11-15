using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class TokenSubjectTests
{
    [Fact]
    public void Canonical_user_requires_one_consistent_identifier_and_tenant()
    {
        using var services = CreateServices();
        var resolver = services.GetRequiredService<ITokenSubjectResolver>();
        var principal = User();
        Assert.True(resolver.TryResolve(principal, out var subject));
        Assert.Equal(TokenSubject.ForUser("tenant-a", "elsa-user"), subject);

        foreach (var conflicting in new[]
                 {
                     new Claim(IdentityClaimTypes.UserId, "elsa-user"),
                     new Claim(IdentityClaimTypes.TenantId, "tenant-b"),
                     new Claim(ClaimTypes.NameIdentifier, "upstream-subject"),
                     new Claim("SUB", "elsa-user")
                 })
        {
            var invalid = User();
            ((ClaimsIdentity)invalid.Identity!).AddClaim(conflicting);
            Assert.False(resolver.TryResolve(invalid, out _));
        }
        var wrong = User();
        var identity = (ClaimsIdentity)wrong.Identity!;
        identity.RemoveClaim(identity.FindFirst("sub")!);
        identity.AddClaim(new Claim("sub", "upstream-subject"));
        Assert.False(resolver.TryResolve(wrong, out _));
    }

    [Fact]
    public void External_subject_requires_provider_owned_origin_and_never_infers_a_user()
    {
        using var services = CreateServices();
        var resolver = services.GetRequiredService<ITokenSubjectResolver>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("sub", "elsa-user"), new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
         new Claim(IdentityClaimTypes.Provider, "provider-a"), new Claim("iss", "https://untrusted.example/"),
         new Claim(IdentityClaimTypes.Normalized, "v1")], "trusted-adapter"));
        Assert.False(resolver.TryResolve(principal, out _));
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(IdentityClaimTypes.SubjectProvider, "provider-a"));
        identity.AddClaim(new Claim(IdentityClaimTypes.SubjectIssuer, "https://validated.example/"));
        Assert.True(resolver.TryResolve(principal, out var subject));
        Assert.Equal(TokenSubject.ForProviderSubject("tenant-a", "provider-a", "elsa-user", "https://validated.example/"), subject);
        Assert.Null(subject.UserId);
        identity.AddClaim(new Claim(IdentityClaimTypes.SubjectIssuer, "https://other.example/"));
        Assert.False(resolver.TryResolve(principal, out _));
    }

    [Fact]
    public void Issuer_qualified_subject_requires_all_parts()
    {
        Assert.Throws<ArgumentNullException>(() => TokenSubject.ForProviderSubject("tenant", "provider", "sub", null!));
        Assert.Throws<ArgumentException>(() => TokenSubject.ForProviderSubject("tenant", "provider", "sub", " "));
        Assert.Throws<ArgumentException>(() => TokenSubject.ForUser(" ", "sub"));
    }

    [Fact]
    public void Resolver_refuses_untrusted_or_multiple_trusted_identities()
    {
        using var services = CreateServices();
        var resolver = services.GetRequiredService<ITokenSubjectResolver>();
        var untrusted = new ClaimsPrincipal(new ClaimsIdentity(User().Claims, "untrusted-adapter"));
        Assert.False(resolver.TryResolve(untrusted, out _));
        var combined = User();
        combined.AddIdentity((ClaimsIdentity)User().Identity!);
        Assert.False(resolver.TryResolve(combined, out _));
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        services.AddNormalizedAuthenticationType("trusted-adapter");
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal User() => new(new ClaimsIdentity(
    [new Claim("sub", "elsa-user"), new Claim(ClaimTypes.NameIdentifier, "elsa-user"),
     new Claim(IdentityClaimTypes.UserId, "elsa-user"), new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
     new Claim(IdentityClaimTypes.Normalized, "v1")], "trusted-adapter"));
}
