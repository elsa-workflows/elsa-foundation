using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.FastEndpoints.Features;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Graph.Design;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Security;
using Elsa.Api.FastEndpoints;
using Elsa.Expressions;
using Elsa.Expressions.Api;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Mediator;
using Elsa.Mediator.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Api;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class DomainManagementApiCompositionTests
{
    private static readonly string[] ExpectedCapabilities =
    [
        "elsa.api.activity-design",
        "elsa.api.expressions",
        "elsa.api.publishing",
        "elsa.api.runtime",
        "elsa.api.workflow-design"
    ];

    [Fact]
    public async Task Custom_host_exposes_representative_domain_journeys_without_Elsa_Server()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: true);

        Assert.DoesNotContain(
            typeof(DomainManagementApiCompositionTests).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Elsa.Workbench", StringComparison.Ordinal));

        await host.AssertJourneyAsync(HttpMethod.Get, "/design/workflows/definitions");
        await host.AssertJourneyAsync(HttpMethod.Get, "/design/activities/catalog");
        await host.AssertJourneyAsync(HttpMethod.Get, "/expressions/descriptors");
        await host.AssertJourneyAsync(HttpMethod.Post, "/publishing/workflows/version-1/publish", new { });
        await host.AssertJourneyAsync(HttpMethod.Get, "/runtime/workflows/executables");

        var expressions = await host.Client.GetFromJsonAsync<ExpressionDescriptorsResponse>("/expressions/descriptors");
        Assert.NotNull(expressions);
        Assert.Equal(new[] { "Input", "Literal", "Object", "Variable" }, expressions.Items.Select(x => x.Type));
        Assert.Equal(
            new[]
            {
                ExpressionEditingModeView.Reference,
                ExpressionEditingModeView.Literal,
                ExpressionEditingModeView.Structured,
                ExpressionEditingModeView.Reference
            },
            expressions.Items.Select(x => x.EditingMode));

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.NotNull(capabilities);
        Assert.Equal(ExpectedCapabilities, capabilities.Capabilities.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Representative_host_manifest_is_stable_reviewed_and_permission_owned()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: true, allowAnonymous: false);

        var captures = Enumerable.Range(0, 10)
            .Select(_ => new EndpointManifestBuilder(host.EndpointDataSources).BuildJson())
            .ToArray();
        Assert.Single(captures.Distinct(StringComparer.Ordinal));

        var baselinePath = Path.Join(RepoRoot, "tests", "Elsa", "Architecture", "Baselines", "endpoint-manifest.json");
        Assert.Equal(BaselineFile.Read(baselinePath), captures[0]);

        var manifest = new EndpointManifestBuilder(host.EndpointDataSources).Build();
        var permissions = new PermissionOwnershipValidator(host.Services.GetServices<IPermissionContributor>())
            .Validate(manifest.Entries);
        Assert.True(permissions.IsValid, string.Join(Environment.NewLine, permissions.Issues.Select(issue =>
            $"{issue.Code}: {issue.Endpoint}: {issue.Message}")));
    }

    [Fact]
    public async Task Omitted_domain_has_neither_routes_nor_capability_while_installed_domains_remain_available()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        await host.AssertJourneyAsync(HttpMethod.Get, "/design/workflows/definitions");
        await host.AssertJourneyAsync(HttpMethod.Get, "/design/activities/catalog");
        await host.AssertJourneyAsync(HttpMethod.Post, "/publishing/workflows/version-1/publish", new { });
        await host.AssertJourneyAsync(HttpMethod.Get, "/runtime/workflows/executables");

        var omittedRoute = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/expressions/descriptors"));
        Assert.Equal(HttpStatusCode.NotFound, omittedRoute.StatusCode);

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.NotNull(capabilities);
        Assert.Equal(
            ExpectedCapabilities.Where(x => x != "elsa.api.expressions"),
            capabilities.Capabilities.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Activity_availability_diagnostics_is_retrievable_without_request_input()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        var diagnostics = await host.Client.GetFromJsonAsync<ActivityAvailabilityDiagnostics>(
            "/design/activities/availability/diagnostics");

        Assert.NotNull(diagnostics);
        Assert.Empty(diagnostics.Items);
        Assert.Empty(diagnostics.Sets);
    }

    [Fact]
    public async Task Stock_server_shell_advertises_one_graph_design_provider_through_authoring_capabilities()
    {
        var enabledFeatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "ActivitiesDesignApi",
            "ActivitiesGraphDesign",
            "ApiCapabilities",
            "Expressions",
            "FastEndpoints",
            "FoundationIdentityAbstractions",
            "Mediator",
            "WorkflowsPublishingApi"
        };
        var overrides = StockServerFeatureNames()
            .Where(feature => !enabledFeatures.Contains(feature))
            .ToDictionary(
                feature => $"CShells:Shells:default:Features:{feature}",
                _ => (string?)"false",
                StringComparer.Ordinal);
        overrides["CShells:Shells:default:Features:ApiSecurity:AllowAnonymous"] = "true";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration
            .AddJsonFile(StockServerConfigurationPath)
            .AddInMemoryCollection(overrides);
        builder.Services.Configure<FoundationIdentityOptions>(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(FastEndpointsFeature).Assembly,
                typeof(ApiSecurityFeature).Assembly,
                typeof(FoundationIdentityAbstractionsFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(GraphActivitiesDesignFeature).Assembly,
                // spec 145: the publish engine split moved the workflow-publish feature out of
                // WorkflowsPublishingApi into the endpoint-free WorkflowsPublishing engine, which both
                // WorkflowsPublishingApi and GraphActivitiesDesign now DependsOn. Seed the engine and its
                // transitive Runtime-triggers/Events feature assemblies so shell composition can discover them.
                typeof(Elsa.Workflows.Publishing.WorkflowsPublishingFeature).Assembly,
                typeof(Elsa.Workflows.Runtime.Api.WorkflowsRuntimeTriggersFeature).Assembly,
                typeof(Elsa.Events.EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(DomainManagementApiCompositionTests).Assembly)
            .WithConfigurationProvider(builder.Configuration)
            .WithWebRouting(options => options.EnablePathRouting = true));

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
                    new Claim(IdentityClaimTypes.Permission, HttpContextActivityDesignAuthorizationContext.AuthorPermission)
                ],
                "test"));
            await next(context);
        });
        app.MapShells();
        await app.StartAsync();

        var registry = app.Services.GetRequiredService<IShellRegistry>();
        var shell = await registry.GetOrActivateAsync("default");
        var shellSettings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        Assert.Contains("FastEndpoints", shellSettings.EnabledFeatures);
        Assert.Contains("ActivitiesDesignApi", shellSettings.EnabledFeatures);
        Assert.Contains("ActivitiesGraphDesign", shellSettings.EnabledFeatures);
        var routePatterns = app.Services.GetServices<EndpointDataSource>()
            .Concat(shell.ServiceProvider.GetServices<EndpointDataSource>())
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(pattern => pattern is not null)
            .ToArray();
        Assert.Contains(
            "design/activities/authoring-capabilities",
            routePatterns,
            StringComparer.Ordinal);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var capabilities = await client.GetFromJsonAsync<ActivityAuthoringCapabilitiesView>(
            "/design/activities/authoring-capabilities");

        Assert.NotNull(capabilities);
        var provider = Assert.Single(capabilities.Providers);
        Assert.Equal("elsa.activity-graph", provider.ProviderKey);
        Assert.Equal("Activity Graph", provider.DisplayName);
        Assert.Equal(["1", "2"], provider.ManifestSchemas.Select(x => x.SchemaVersion).Order(StringComparer.Ordinal));
        Assert.All(provider.ManifestSchemas, schema => Assert.True(schema.IsAuthorable));
        Assert.Empty(provider.RequiredOutcomes);
    }

    private static IReadOnlyCollection<string> StockServerFeatureNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(StockServerConfigurationPath));
        return document.RootElement
            .GetProperty("CShells")
            .GetProperty("Shells")
            .GetProperty("default")
            .GetProperty("Features")
            .EnumerateObject()
            .Select(feature => feature.Name)
            .ToArray();
    }

    private static string StockServerConfigurationPath =>
        Path.Combine(RepoRoot, "src", "Apps", "Elsa.Workbench", "shells.json");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }

    [ShellFeature(name: "ApiCapabilities")]
    public sealed class ApiCapabilitiesDependencyFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }
    }

    [ShellFeature(name: "WorkflowsPublishingApi")]
    public sealed class WorkflowsPublishingDependencyFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        }
    }

    private sealed class CustomManagementHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        private static readonly string[] CommonEndpointTypes =
        [
            "Elsa.Api.Capabilities.Endpoints.GetCapabilities",
            "Elsa.Activities.Design.Api.Endpoints.Catalog.List",
            "Elsa.Activities.Design.Api.Endpoints.Availability.ListDiagnostics",
            "Elsa.Activities.Design.Api.Endpoints.AuthoringCapabilities.Get",
            "Elsa.Workflows.Design.Api.Endpoints.Definitions.List",
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishWorkflowEndpoint",
            "Elsa.Workflows.Runtime.Api.Endpoints.ListWorkflowExecutablesEndpoint"
        ];

        public HttpClient Client { get; } = client;
        public IServiceProvider Services => app.Services;
        public IReadOnlyList<EndpointDataSource> EndpointDataSources => app.Services.GetServices<EndpointDataSource>()
            .Select(source => new RepresentativeEndpointDataSource(source.Endpoints
                .Where(endpoint => endpoint is not RouteEndpoint route || route.RoutePattern.RawText != "_test_url_cache_")
                .ToArray()))
            .ToArray();

        public static async Task<CustomManagementHost> StartAsync(bool includeExpressions, bool allowAnonymous = true)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            new ApiCapabilitiesFeature().ConfigureServices(builder.Services);
            new ActivitiesDesignApiFeature().ConfigureServices(builder.Services);
            new WorkflowsDesignApiFeature().ConfigureServices(builder.Services);
            new WorkflowsPublishingApiFeature().ConfigureServices(builder.Services);
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);

            var endpointTypes = CommonEndpointTypes.ToHashSet(StringComparer.Ordinal);
            var assemblies = new List<System.Reflection.Assembly>
            {
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(WorkflowsPublishingApiFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly
            };

            if (includeExpressions)
            {
                new ExpressionsFeature().ConfigureServices(builder.Services);
                new ExpressionsApiFeature().ConfigureServices(builder.Services);
                assemblies.Add(typeof(ExpressionsApiFeature).Assembly);
                endpointTypes.Add("Elsa.Expressions.Api.Endpoints.ListExpressionDescriptors");
            }

            builder.Services.AddSingleton<IRequestSender, JourneyRequestSender>();
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = assemblies.ToArray();
                options.Filter = type => type.FullName is not null && endpointTypes.Contains(type.FullName);
            });

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("elsa.identity.permission", HttpContextActivityDesignAuthorizationContext.AuthorPermission)],
                    "test"));
                await next(context);
            });
            app.UseFastEndpoints(options => options.Endpoints.Configurator = allowAnonymous
                ? endpoint => endpoint.AllowAnonymous()
                : null);
            await app.StartAsync();
            return new CustomManagementHost(app, app.GetTestClient());
        }

        public async Task AssertJourneyAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await Client.SendAsync(request);

            Assert.True(
                response.IsSuccessStatusCode,
                $"Representative {method} {path} journey returned {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class RepresentativeEndpointDataSource(IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> Endpoints => endpoints;
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }

    private sealed class JourneyRequestSender(IServiceProvider services) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is ListExpressionDescriptors expressionDescriptors)
                return await HandleExpressionDescriptors<T>(expressionDescriptors, cancellationToken);
            if (request is GetActivityAuthoringCapabilities authoringCapabilities)
            {
                var handler = services.GetRequiredService<
                    IRequestHandler<GetActivityAuthoringCapabilities, ActivityAuthoringCapabilitiesView>>();
                return (T)(object)await handler.Handle(authoringCapabilities, cancellationToken);
            }

            object response = typeof(T) switch
            {
                var type when type == typeof(WorkflowDefinitionListView) => new WorkflowDefinitionListView([]),
                var type when type == typeof(ActivityAuthoringCatalogView) => new ActivityAuthoringCatalogView([]),
                var type when type == typeof(ActivityAvailabilityDiagnostics) => new ActivityAvailabilityDiagnostics([], []),
                var type when type == typeof(PublishedWorkflowView) => new PublishedWorkflowView(
                    "publication-1", "definition-1", "version-1", "version-1", "artifact-1", "default",
                    PublicationStatusView.Active, "reference-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                    null, "1.0.0", "hash-1", "root-1", 1, true),
                var type when type == typeof(Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView) =>
                    new Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView([]),
                _ => throw new InvalidOperationException($"Unexpected representative journey response type '{typeof(T)}'.")
            };

            return (T)response;
        }

        private async Task<T> HandleExpressionDescriptors<T>(
            ListExpressionDescriptors request,
            CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<IRequestHandler<ListExpressionDescriptors, ExpressionDescriptorsResponse>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }
    }
}
