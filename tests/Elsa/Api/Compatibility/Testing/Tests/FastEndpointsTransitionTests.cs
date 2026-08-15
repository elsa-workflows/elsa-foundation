using Elsa.Api.Compatibility.Testing.Transitions;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class FastEndpointsTransitionTests
{
    private const string Source = """
        namespace Features;
        public sealed class GetOrdersEndpoint : EndpointWithoutRequest
        {
            public override void Configure() => Get("/api/orders/{id:int}");
        }
        """;

    [Fact]
    public void Scans_exact_registration_identity_route_and_method()
    {
        var registration = Assert.Single(new FastEndpointsRegistrationScanner().Scan(Source, "orders"));

        Assert.Equal("Features.GetOrdersEndpoint", registration.Identity);
        Assert.Equal("orders", registration.Owner);
        Assert.Equal(["GET /api/orders/{param:int}"], registration.Endpoints.Select(endpoint => endpoint.ToString()));
    }

    [Fact]
    public void Reconciles_new_expanded_stale_ambiguous_and_dynamic_mutations()
    {
        var scanner = new FastEndpointsRegistrationScanner();
        var registration = Assert.Single(scanner.Scan(Source, "orders"));
        var exact = new FastEndpointsTransitionException(registration.Identity, "orders", registration.Endpoints, "orders-team", "#123",
            SourceHash: registration.SourceHash);

        Assert.True(TransitionExceptionValidator.Reconcile([registration], [exact]).IsValid);
        Assert.Contains("NewRegistration", TransitionExceptionValidator.Reconcile([registration with { Identity = "Features.NewEndpoint" }], [exact]).Issues.Select(x => x.Code));
        Assert.Contains("ExpandedRegistration", TransitionExceptionValidator.Reconcile([registration with { Endpoints = [.. registration.Endpoints, new("/api/orders", "POST")] }], [exact]).Issues.Select(x => x.Code));
        Assert.Contains("StaleException", TransitionExceptionValidator.Reconcile([], [exact]).Issues.Select(x => x.Code));
        Assert.Contains("AmbiguousException", TransitionExceptionValidator.Reconcile([registration], [exact, exact]).Issues.Select(x => x.Code));

        var dynamic = Assert.Single(scanner.Scan(Source, "orders", dynamicallyUnloadable: true));
        Assert.Contains("DynamicUnloadableRegistration", TransitionExceptionValidator.Reconcile([dynamic], [exact]).Issues.Select(x => x.Code));
    }

    [Fact]
    public void Rejects_dynamic_route_registration_without_an_exact_literal()
    {
        const string source = """
            public sealed class DynamicEndpoint : EndpointWithoutRequest
            {
                public override void Configure() { Get(GetRoute()); }
                private static string GetRoute() => "/dynamic";
            }
            """;

        var registration = Assert.Single(new FastEndpointsRegistrationScanner().Scan(source));
        Assert.True(registration.DynamicRoute);
        Assert.Contains("DynamicRegistration", TransitionExceptionValidator.Reconcile([registration], []).Issues.Select(x => x.Code));
    }

    [Fact]
    public void Resolves_cross_document_constants_and_rejects_remaining_dynamic_routes()
    {
        var scanner = new FastEndpointsRegistrationScanner();
        var literal = Assert.Single(scanner.Scan(Source, "orders"));
        var literalException = new FastEndpointsTransitionException(
            literal.Identity, literal.Owner, literal.Endpoints, "orders-team", "#123", SourceHash: literal.SourceHash);
        var editedLiteral = Assert.Single(scanner.Scan(Source + Environment.NewLine + "// implementation note", "orders"));

        Assert.NotEqual(literal.SourceHash, editedLiteral.SourceHash);
        Assert.True(TransitionExceptionValidator.Reconcile([editedLiteral], [literalException]).IsValid);

        const string constantsSource = """
            public static class RouteNames
            {
                public const string Dynamic = "/dynamic";
            }
            """;
        const string endpointSource = """
            public sealed class DynamicEndpoint : EndpointWithoutRequest
            {
                public override void Configure() => Get(RouteNames.Dynamic);
            }
            """;
        var resolved = Assert.Single(scanner.Scan(
        [
            new("routes", constantsSource, "orders"),
            new("endpoint", endpointSource, "orders")
        ]));

        Assert.False(resolved.DynamicRoute);
        Assert.Equal(["GET /dynamic"], resolved.Endpoints.Select(endpoint => endpoint.ToString()));

        const string unresolvedSource = """
            public sealed class UnresolvedEndpoint : EndpointWithoutRequest
            {
                public override void Configure() => Get(BuildRoute());
            }
            """;
        var unresolved = Assert.Single(scanner.Scan(unresolvedSource, "orders"));
        var unresolvedException = new FastEndpointsTransitionException(
            unresolved.Identity, unresolved.Owner, unresolved.Endpoints, "orders-team", "#123", SourceHash: unresolved.SourceHash);

        Assert.True(unresolved.DynamicRoute);
        Assert.True(TransitionExceptionValidator.Reconcile([unresolved], [unresolvedException]).IsValid);
        Assert.Contains("DynamicRegistration",
            TransitionExceptionValidator.Reconcile(
                [unresolved with { SourceHash = new string('0', unresolved.SourceHash.Length) }],
                [unresolvedException]).Issues.Select(x => x.Code));
    }

    [Fact]
    public void Rejects_duplicate_discovered_registration_identities()
    {
        var registration = Assert.Single(new FastEndpointsRegistrationScanner().Scan(Source, "orders"));
        var exact = new FastEndpointsTransitionException(
            registration.Identity, registration.Owner, registration.Endpoints, "orders-team", "#123", SourceHash: registration.SourceHash);

        var result = TransitionExceptionValidator.Reconcile([registration, registration], [exact]);

        Assert.Contains("DuplicateRegistration", result.Issues.Select(issue => issue.Code));
    }
}
