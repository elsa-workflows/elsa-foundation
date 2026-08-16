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
    public void Resolves_interpolated_route_constants_without_duplicating_interpolations()
    {
        const string source = """
            public static class Routes
            {
                public const string Prefix = "/api";
                public const string Orders = $"{Prefix}/orders";
            }

            public sealed class OrdersEndpoint : EndpointWithoutRequest
            {
                public override void Configure() => Get(Routes.Orders);
            }
            """;

        var registration = Assert.Single(new FastEndpointsRegistrationScanner().Scan(source, "orders"));

        Assert.False(registration.DynamicRoute);
        Assert.Equal(["GET /api/orders"], registration.Endpoints.Select(endpoint => endpoint.ToString()));
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

    [Fact]
    public void Resolves_transitive_inheritance_and_inherited_route_configuration_but_excludes_abstract_bases()
    {
        const string source = """
            namespace Features;

            internal abstract class SharedRouteEndpoint<TRequest> : ElsaEndpoint<TRequest>
            {
                public override void Configure() => Get("/api/shared/{id}");
            }

            internal abstract class IndirectRouteEndpoint<TRequest> : SharedRouteEndpoint<TRequest> { }

            internal sealed class InheritedEndpoint : IndirectRouteEndpoint<string> { }

            internal sealed class OverriddenEndpoint : IndirectRouteEndpoint<string>
            {
                public override void Configure() => Post("/api/own");
            }

            internal sealed class CombinedEndpoint : IndirectRouteEndpoint<string>
            {
                public override void Configure()
                {
                    base.Configure();
                    Put("/api/combined");
                }
            }
            """;

        var registrations = new FastEndpointsRegistrationScanner().Scan(source, "features");

        Assert.Equal(
            [
                "Features.CombinedEndpoint",
                "Features.InheritedEndpoint",
                "Features.OverriddenEndpoint"
            ],
            registrations.Select(registration => registration.Identity));
        Assert.Equal(["GET /api/shared/{param}"], registrations.Single(registration => registration.Identity.EndsWith("InheritedEndpoint", StringComparison.Ordinal)).Endpoints.Select(endpoint => endpoint.ToString()));
        Assert.Equal(["POST /api/own"], registrations.Single(registration => registration.Identity.EndsWith("OverriddenEndpoint", StringComparison.Ordinal)).Endpoints.Select(endpoint => endpoint.ToString()));
        Assert.Equal(
            ["PUT /api/combined", "GET /api/shared/{param}"],
            registrations.Single(registration => registration.Identity.EndsWith("CombinedEndpoint", StringComparison.Ordinal)).Endpoints.Select(endpoint => endpoint.ToString()));
    }

    [Fact]
    public void Resolves_a_same_namespace_base_before_simple_name_fallback_and_fails_closed_when_ambiguous()
    {
        const string source = """
            namespace First
            {
                internal abstract class SharedEndpoint<TRequest> : ElsaEndpoint<TRequest>
                {
                    public override void Configure() => Get("/api/first");
                }

                internal sealed class FirstConcreteEndpoint : SharedEndpoint<string> { }
            }

            namespace Second
            {
                internal abstract class SharedEndpoint<TRequest> : ElsaEndpoint<TRequest>
                {
                    public override void Configure() => Get("/api/second");
                }
            }

            namespace Third
            {
                internal sealed class AmbiguousConcreteEndpoint : SharedEndpoint<string> { }
            }
            """;

        var registrations = new FastEndpointsRegistrationScanner().Scan(source, "features");

        var first = Assert.Single(registrations);
        Assert.Equal("First.FirstConcreteEndpoint", first.Identity);
        Assert.Equal(["GET /api/first"], first.Endpoints.Select(endpoint => endpoint.ToString()));
        Assert.DoesNotContain(registrations, registration => registration.Identity.Contains("Ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void Scans_multiple_methods_and_ignored_logs_sources_but_excludes_bin_and_obj_documents()
    {
        const string source = """
            public sealed class MultiMethodEndpoint : EndpointWithoutRequest
            {
                public override void Configure()
                {
                    Get("/api/one");
                    Post("/api/two");
                }
            }
            """;

        var registrations = new FastEndpointsRegistrationScanner().Scan(
        [
            new FastEndpointsSourceDocument("multi", source, "features", SourcePath: "src/Features/Endpoint.cs"),
            new FastEndpointsSourceDocument("logs", source.Replace("MultiMethodEndpoint", "LogsEndpoint", StringComparison.Ordinal), "otel", SourcePath: "src/Elsa/Diagnostics/OpenTelemetry/Endpoints/OpenTelemetry/Logs/Endpoint.cs"),
            new FastEndpointsSourceDocument("bin", source.Replace("MultiMethodEndpoint", "GeneratedEndpoint", StringComparison.Ordinal), "generated", SourcePath: "src/Features/bin/Debug/net10.0/Generated.cs"),
            new FastEndpointsSourceDocument("obj", source.Replace("MultiMethodEndpoint", "GeneratedObjEndpoint", StringComparison.Ordinal), "generated", SourcePath: "src/Features/obj/Debug/net10.0/Generated.cs")
        ]);

        Assert.Equal(2, registrations.Count);
        Assert.Contains(registrations, registration => registration.Identity == "MultiMethodEndpoint" && registration.Owner == "features");
        Assert.Contains(registrations, registration => registration.Identity == "LogsEndpoint" && registration.Owner == "otel");
        Assert.DoesNotContain(registrations, registration => registration.Identity.Contains("Generated", StringComparison.Ordinal));
        Assert.Equal(["GET /api/one", "POST /api/two"], registrations.Single(registration => registration.Identity == "MultiMethodEndpoint").Endpoints.Select(endpoint => endpoint.ToString()));
    }

    [Fact]
    public void Retirement_validation_rejects_every_registration_even_when_a_transition_exception_exists()
    {
        var registration = Assert.Single(new FastEndpointsRegistrationScanner().Scan(Source, "orders"));
        var result = TransitionExceptionValidator.ValidateRetirement([registration]);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("FirstPartyFastEndpointsRegistration", issue.Code);
        Assert.Contains("zero first-party registrations", issue.Message, StringComparison.Ordinal);
    }
}
