using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core.Exceptions;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferenceScopeResolverTests
{
    [Fact]
    public async Task UsesAuthenticatedSessionAsAuthoritativeScope()
    {
        var resolver = new StudioPreferenceScopeResolver(new SessionService(new(
            "authenticated", "user-7", "Ada", "tenant-3", new HashSet<string>(), new HashSet<string>(), "fresh", "test")));

        var key = await resolver.ResolveAsync(new ClaimsPrincipal(), "studio-primary", "dashboard");

        Assert.Equal("user-7", key.SubjectId);
        Assert.Equal("tenant-3", key.TenantId);
        Assert.Equal("studio-primary", key.StudioHostId);
    }

    [Theory]
    [InlineData("anonymous", null, null)]
    [InlineData("authenticated", null, "tenant")]
    [InlineData("authenticated", "user", null)]
    public async Task MissingAuthenticatedIdentityFailsClosed(string status, string? subject, string? tenant)
    {
        var resolver = new StudioPreferenceScopeResolver(new SessionService(new(
            status, subject, null, tenant, new HashSet<string>(), new HashSet<string>(), "none", null)));

        await Assert.ThrowsAsync<StudioPreferenceScopeException>(async () =>
            await resolver.ResolveAsync(new ClaimsPrincipal(), "studio-primary", "dashboard"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("host/segment")]
    public async Task MalformedStudioHostIdIsRejected(string hostId)
    {
        var resolver = new StudioPreferenceScopeResolver(new SessionService(new(
            "authenticated", "user", null, "tenant", new HashSet<string>(), new HashSet<string>(), "fresh", null)));

        await Assert.ThrowsAsync<StudioPreferenceHostException>(async () =>
            await resolver.ResolveAsync(new ClaimsPrincipal(), hostId, "dashboard"));
    }

    private sealed class SessionService(AuthSession session) : IAuthSessionService
    {
        public ValueTask<AuthSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(session);
    }
}
