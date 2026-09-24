using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Mediator;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Proves the proposed Authoring selection in a real CShells HTTP host. The fixture's explicit
/// IDs remain the catalog contract; CShells supplies only the required runtime closure.
/// </summary>
public sealed class AuthoringCompositionHostEvidenceTests : IDisposable
{
    private const string ShellName = "default";
    private const string ResourceName = "Authoring";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-authoring-fixture-{Guid.NewGuid():N}.db");
    private readonly ITestOutputHelper output;

    public AuthoringCompositionHostEvidenceTests(ITestOutputHelper output) => this.output = output;

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    private static readonly string[] ExplicitFeatureIds =
    [
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "ApiCapabilities",
        "ActivitiesDesignApi",
        "ActivitiesDesignEntityFrameworkCore",
        "ActivitiesDesignReconciliation",
        "ClrActivityReconciliation",
        "WorkflowDesignValidations",
        "WorkflowsDesignApi",
        "WorkflowsDesignEntityFrameworkCore",
        "WorkflowsPublishing",
        "WorkflowsPublishingApi",
        "WorkflowsPublishingEntityFrameworkCore"
    ];

    [Fact]
    public async Task Proposed_authoring_fixture_activates_with_visible_runtime_closure_routes_and_shared_persistence()
    {
        await using var host = await StartHostAsync();
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        var enabledFeatures = settings.EnabledFeatures.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        output.WriteLine($"Requested feature IDs ({ExplicitFeatureIds.Length}): {string.Join(", ", ExplicitFeatureIds)}");
        output.WriteLine($"Resolved enabled features ({enabledFeatures.Length}): {string.Join(", ", enabledFeatures)}");

        // Publishing -> RuntimeTriggers -> RuntimeApi are the required additions to the 16-ID proposal.
        Assert.Equal(
            ExplicitFeatureIds.Concat(["WorkflowsRuntimeApi", "WorkflowsRuntimeTriggers"])
                .OrderBy(name => name, StringComparer.Ordinal),
            enabledFeatures);

        var endpoints = host.Services.GetServices<EndpointDataSource>()
            .Concat(shell.ServiceProvider.GetServices<EndpointDataSource>())
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(pattern => pattern is not null)
            .Select(pattern => $"/{pattern!.TrimStart('/')}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var prefix in new[] { "/design/activities", "/design/workflows", "/publishing", "/runtime" })
        {
            var routes = endpoints
                .Where(route => route.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(route => route, StringComparer.Ordinal)
                .ToArray();
            output.WriteLine($"Mounted routes {prefix} ({routes.Length}):");
            foreach (var route in routes)
                output.WriteLine($"  {route}");
        }

        Assert.Contains("/design/activities/catalog", endpoints);
        Assert.Contains("/design/workflows/definitions", endpoints);
        Assert.Contains("/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish", endpoints);
        Assert.Contains("/runtime/workflows/executables", endpoints);
        Assert.Contains("/capabilities", endpoints);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var publishingOptions = scope.ServiceProvider.GetRequiredService<PublishingEntityFrameworkCoreOptions>();
        Assert.Equal("Sqlite", publishingOptions.Provider);
        Assert.Equal(ResourceName, publishingOptions.ConnectionName);
        Assert.Null(publishingOptions.ConnectionString);

        DbContext[] contexts =
        [
            scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>(),
            scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(),
            scope.ServiceProvider.GetRequiredService<PublishingSnapshotReviewDbContext>()
        ];
        foreach (var context in contexts)
        {
            Assert.Equal(databasePath, context.Database.GetDbConnection().DataSource);
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            Assert.NotEmpty(appliedMigrations);
            Assert.Empty(pendingMigrations);
            output.WriteLine($"Persistence context {context.GetType().Name}: provider=Sqlite, resource={ResourceName}, applied={appliedMigrations.Count()}, pending={pendingMigrations.Count()}");
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                ["Elsa:Persistence:Resources:primary:ConnectionName"] = ResourceName,
                [$"ConnectionStrings:{ResourceName}"] = ConnectionString
            }.Concat(ExplicitFeatureIds.Select(id => new KeyValuePair<string, string?>(
                $"CShells:Shells:{ShellName}:Features:{id}", null))))
            .Build();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddEfPersistenceResources(configuration, typeof(AuthoringCompositionHostEvidenceTests).Assembly);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(PrimitivesFeature).Assembly,
                typeof(SerializationFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(ActivitiesDesignEntityFrameworkCoreFeature).Assembly,
                typeof(ActivitiesDesignReconciliationFeature).Assembly,
                typeof(ClrActivityReconciliationFeature).Assembly,
                typeof(WorkflowDesignValidationsFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsPublishingFeature).Assembly,
                typeof(WorkflowsPublishingApiFeature).Assembly,
                typeof(PublishingEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration)
            .WithWebRouting(options => options.EnablePathRouting = true));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }
}
