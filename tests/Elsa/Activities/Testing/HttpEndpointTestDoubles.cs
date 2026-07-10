using System.Collections;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Testing;

/// <summary>
/// An <see cref="IRouteMatcher"/> over the real ASP.NET <see cref="TemplateParser"/>/<see cref="TemplateMatcher"/>
/// — a verbatim equivalent of the production <c>Elsa.Http.Services.RouteMatcher</c> (which is internal to
/// Elsa.Http). Matching semantics are therefore identical to production, including the precompiled-matcher
/// overload the route table populates (issue #592 item 6). Shared across the HTTP unit/integration test projects
/// (#592 item 18).
/// </summary>
public sealed class TestRouteMatcher : IRouteMatcher
{
    public RouteValueDictionary? Match(string routeTemplate, string route) => Match(Compile(routeTemplate), route);

    public RouteValueDictionary? Match(HttpRouteData routeData, string route) =>
        Match(routeData.CompiledMatcher as TemplateMatcher ?? Compile(routeData.Route), route);

    private static RouteValueDictionary? Match(TemplateMatcher matcher, string route)
    {
        var values = new RouteValueDictionary();
        return matcher.TryMatch(route, values) ? values : null;
    }

    private static TemplateMatcher Compile(string routeTemplate)
    {
        var template = TemplateParser.Parse(routeTemplate);
        return new TemplateMatcher(template, GetDefaults(template));
    }

    private static RouteValueDictionary GetDefaults(RouteTemplate parsedTemplate)
    {
        var result = new RouteValueDictionary();

        foreach (var parameter in parsedTemplate.Parameters)
            if (parameter.DefaultValue != null)
                result.Add(parameter.Name!, parameter.DefaultValue);

        return result;
    }
}

/// <summary>
/// An <see cref="IRouteTable"/> test double that delegates to the <b>production</b> <see cref="RouteTable"/> over
/// a private <see cref="IMemoryCache"/>. Using the real implementation (rather than a list-backed stand-in that
/// preserved insertion order) means the double cannot offer an ordering or matching guarantee production lacks:
/// specificity ordering and precompiled matchers (issue #592 items 1 + 6) are exercised exactly as at runtime.
/// Register as a singleton in an integration host so every scope (the middleware's request scope, the startup
/// task's scope, the index observer's fresh scope) reads and mutates the same table, exactly like production.
/// Shared across the HTTP test projects (#592 item 18).
/// </summary>
public sealed class FakeRouteTable : IRouteTable
{
    private readonly RouteTable _inner = new(new MemoryCache(new MemoryCacheOptions()), NullLogger<RouteTable>.Instance);

    public FakeRouteTable()
    {
    }

    public FakeRouteTable(params string[] templates) => _inner.Refresh(templates).AsTask().GetAwaiter().GetResult();

    public ValueTask Add(string route) => _inner.Add(route);
    public ValueTask Add(HttpRouteData httpRouteData) => _inner.Add(httpRouteData);
    public ValueTask Remove(string route) => _inner.Remove(route);
    public ValueTask AddRange(IEnumerable<string> routes) => _inner.AddRange(routes);
    public ValueTask Refresh(IEnumerable<string> routes) => _inner.Refresh(routes);
    public ValueTask Refresh(IEnumerable<HttpRouteData> routes) => _inner.Refresh(routes);
    public ValueTask RemoveRange(IEnumerable<string> routes) => _inner.RemoveRange(routes);
    public IEnumerator<HttpRouteData> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A minimal test authentication scheme (the standard ASP.NET test-handler pattern) honoring an
/// <c>Authorization: Test &lt;name&gt;</c> header: any such header authenticates a caller named <c>&lt;name&gt;</c>;
/// its absence yields no result (anonymous). It stands in for the shell's authentication stack so the real
/// <c>AuthenticationBasedHttpEndpointAuthorizationHandler</c> can resolve a principal (spec 089 C, T010). Hoisted
/// out of the integration fixture so it can be shared (#592 item 18).
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = header.ToString();
        const string prefix = "Test ";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length <= prefix.Length)
            return Task.FromResult(AuthenticateResult.Fail("Malformed test authorization header."));

        var name = value[prefix.Length..].Trim();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
