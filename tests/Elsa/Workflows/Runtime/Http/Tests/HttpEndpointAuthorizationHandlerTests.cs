using System.Security.Claims;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// Unit coverage for the two shipped <c>IHttpEndpointAuthorizationHandler</c> implementations (spec 089 sub-unit
/// C, T008). The <see cref="AuthenticationBasedHttpEndpointAuthorizationHandler"/> is exercised over both the
/// "middleware already authenticated" path (pre-populated <see cref="HttpContext.User"/>) and the explicit
/// per-shell <c>AuthenticateAsync</c> path (a fake <see cref="IAuthenticationService"/> in RequestServices),
/// pinning the fail-closed contract on every failure mode.
/// </summary>
public class HttpEndpointAuthorizationHandlerTests
{
    private const string PolicyName = "endpoint-policy";

    [Fact]
    public async Task Authenticated_user_without_policy_is_authorized()
    {
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: false));
        var httpContext = NewHttpContext(user: Authenticated());

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext));

        Assert.True(authorized);
    }

    [Fact]
    public async Task Anonymous_user_without_authentication_is_denied()
    {
        // Anonymous principal + an authentication service that fails: fail closed.
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: true));
        var httpContext = NewHttpContext(user: Anonymous(), authentication: FakeAuthenticationService.Failing());

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext));

        Assert.False(authorized);
    }

    [Fact]
    public async Task Anonymous_user_authenticated_explicitly_via_shell_scheme_is_authorized()
    {
        // Upstream middleware left the principal anonymous, but the shell's default scheme authenticates the
        // caller — the handler picks that up via AuthenticateAsync and populates HttpContext.User.
        var principal = Authenticated();
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: true));
        var httpContext = NewHttpContext(user: Anonymous(), authentication: FakeAuthenticationService.Succeeding(principal));

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext));

        Assert.True(authorized);
        Assert.Same(principal, httpContext.User);
    }

    [Fact]
    public async Task Policy_success_authorizes()
    {
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: true));
        var httpContext = NewHttpContext(user: Authenticated());

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext, PolicyName));

        Assert.True(authorized);
    }

    [Fact]
    public async Task Policy_failure_denies()
    {
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: false));
        var httpContext = NewHttpContext(user: Authenticated());

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext, PolicyName));

        Assert.False(authorized);
    }

    [Fact]
    public async Task Authentication_service_throwing_fails_closed()
    {
        // A missing/misconfigured scheme surfaces as a throw from AuthenticateAsync; the handler must deny.
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: true));
        var httpContext = NewHttpContext(user: Anonymous(), authentication: FakeAuthenticationService.Throwing());

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext, PolicyName));

        Assert.False(authorized);
    }

    [Fact]
    public async Task AllowAnonymous_handler_authorizes_everything()
    {
        var handler = new AllowAnonymousHttpEndpointAuthorizationHandler();
        var anonymous = NewHttpContext(user: Anonymous());

        Assert.True(await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(anonymous)));
        Assert.True(await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(anonymous, PolicyName)));
    }

    private static HttpContext NewHttpContext(ClaimsPrincipal user, IAuthenticationService? authentication = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authentication ?? FakeAuthenticationService.Failing());
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.User = user;
        return httpContext;
    }

    private static ClaimsPrincipal Authenticated() => new(new ClaimsIdentity(authenticationType: "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>An <see cref="IAuthorizationService"/> that returns a fixed success/failure without policy lookup.</summary>
    private sealed class StubAuthorizationService(bool succeed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    /// <summary>
    /// A minimal <see cref="IAuthenticationService"/> exercising the handler's explicit <c>AuthenticateAsync</c>
    /// path: it can succeed with a given principal, fail (no ticket), or throw (missing scheme).
    /// </summary>
    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private readonly Func<AuthenticateResult> _authenticate;

        private FakeAuthenticationService(Func<AuthenticateResult> authenticate) => _authenticate = authenticate;

        public static FakeAuthenticationService Succeeding(ClaimsPrincipal principal) =>
            new(() => AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));

        public static FakeAuthenticationService Failing() => new(AuthenticateResult.NoResult);

        public static FakeAuthenticationService Throwing() =>
            new(() => throw new InvalidOperationException("No authentication scheme configured."));

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(_authenticate());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
