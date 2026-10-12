using System.Security.Claims;
using Elsa.Http.Core.Exceptions;
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
    public async Task Anonymous_user_authenticated_explicitly_via_shell_scheme_is_authorized_without_mutating_User()
    {
        // Upstream middleware left the principal anonymous, but the shell's default scheme authenticates the
        // caller — the handler picks that up via AuthenticateAsync. #592 item 11: evaluating the authorize
        // predicate must NOT rewrite HttpContext.User (a side effect of the request principal), so the context's
        // User stays the original anonymous principal.
        var principal = Authenticated();
        var anonymous = Anonymous();
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: true));
        var httpContext = NewHttpContext(user: anonymous, authentication: FakeAuthenticationService.Succeeding(principal));

        var authorized = await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext));

        Assert.True(authorized);
        Assert.Same(anonymous, httpContext.User);
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
    public async Task Authentication_service_throwing_surfaces_as_configuration_exception()
    {
        // #592 item 11: a missing/misconfigured scheme surfaces as a throw from AuthenticateAsync. That is an
        // operator configuration error, distinct from an anonymous caller — the handler wraps it in
        // HttpEndpointAuthorizationConfigurationException (→ 500) rather than swallowing it into a 401 denial.
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new StubAuthorizationService(succeed: true));
        var httpContext = NewHttpContext(user: Anonymous(), authentication: FakeAuthenticationService.Throwing());

        await Assert.ThrowsAsync<HttpEndpointAuthorizationConfigurationException>(
            async () => await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext, PolicyName)));
    }

    [Fact]
    public async Task Policy_evaluation_throwing_surfaces_as_configuration_exception()
    {
        // #592 item 11: an unregistered/misconfigured policy throws from IAuthorizationService — a configuration
        // error, not a denial. It surfaces distinctly so the middleware returns 500, not 401.
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new ThrowingAuthorizationService());
        var httpContext = NewHttpContext(user: Authenticated());

        await Assert.ThrowsAsync<HttpEndpointAuthorizationConfigurationException>(
            async () => await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext, PolicyName)));
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_not_swallowed_into_a_denial()
    {
        // #592 item 11: OperationCanceledException (client abort) must propagate, not become a 401.
        var handler = new AuthenticationBasedHttpEndpointAuthorizationHandler(new CancelingAuthorizationService());
        var httpContext = NewHttpContext(user: Authenticated());

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await handler.AuthorizeAsync(new AuthorizeHttpEndpointContext(httpContext, PolicyName)));
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

    /// <summary>An <see cref="IAuthorizationService"/> whose policy evaluation throws (unregistered/misconfigured policy).</summary>
    private sealed class ThrowingAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            throw new InvalidOperationException("No policy registered.");

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            throw new InvalidOperationException($"No policy named '{policyName}' registered.");
    }

    /// <summary>An <see cref="IAuthorizationService"/> whose policy evaluation cancels (proves cancellation propagates).</summary>
    private sealed class CancelingAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            throw new OperationCanceledException();

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            throw new OperationCanceledException();
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
