using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>
/// A feature whose data only the family's version 2 holds (spec 182, FR-001): dormant until this host observes 2 as
/// finalized. It is composed and mapped like any other; what it serves asks the shared dormancy check first.
/// </summary>
[ShellFeature(name: FeedModule.OrdersFeature, DisplayName = "Feed module fixture orders", DependsOn = new object[] { FeedModule.EntityFrameworkCoreFeature })]
[RequiresSchemaVersion(FeedModule.Family, FeedModule.CurrentVersion)]
public sealed class FeedModuleOrdersFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddScoped<FeedModuleOrders>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        endpoints.MapGet(FeedModule.OrdersPath, async (FeedModuleOrders orders, CancellationToken cancellationToken) =>
        {
            try
            {
                await orders.PlaceAsync(cancellationToken);
                return Results.Ok($"{FeedModule.OrdersFeature} serves: {FeedModule.Family} is at {FeedModule.CurrentVersion}.");
            }
            catch (SchemaDormancyRefusedException refusal)
            {
                return Results.Conflict($"{refusal.Code}: {refusal.Reason}");
            }
        });
}

/// <summary>
/// Where the feature accepts data only version 2 holds: it asks the host's shared dormancy check before any row changes,
/// as spec 182's FR-002 has every such operation do.
/// </summary>
/// <exception cref="SchemaDormancyRefusedException">The feature is dormant.</exception>
public sealed class FeedModuleOrders(ISchemaDormancyCheck check)
{
    public async Task PlaceAsync(CancellationToken cancellationToken = default) =>
        await check.EnsureAvailableAsync(SchemaVersionRequirement.DeclaredBy(typeof(FeedModuleOrdersFeature)), FeedModule.OrdersFeature, cancellationToken);
}
