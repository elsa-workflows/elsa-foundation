using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Caching.Memory;
using Elsa.Expressions;
using Elsa.Expressions.Liquid;
using Elsa.Locking.FileSystem;
using Elsa.Notifications;
using Elsa.Serialization;
using Elsa.Tasks;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Elsa.Server;
using Nuplane;
using Nuplane.Abstractions;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;
using Nuplane.Sources.Directory.Configuration;
using System.Reflection;
using Nuplane.Loading.Hosting.Builder;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

//builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
//{
//    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
//    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
//});

//builder.Services.AddSingleton<NuplaneFeatureAssemblyProvider>();


var assemblies = new Assembly[]
{
    typeof(SqliteWorkflowDefinitionPersistenceShellFeature).Assembly,
    typeof(ExpressionsFeature).Assembly,
    typeof(LiquidExpressionsFeature).Assembly,
    typeof(NotificationsFeature).Assembly,
    typeof(MemoryCacheFeature).Assembly,
    typeof(FileSystemLockingFeature).Assembly,
    typeof(SerializationFeature).Assembly,
    typeof(TasksFeature).Assembly
};

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithHostAssemblies()
        //.WithAssemblyProvider<NuplaneFeatureAssemblyProvider>()
        .WithAssemblies(assemblies)
        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
            options.ExcludePaths = ["/admin"];
        });
});

builder.Services.AddHostedService<ShellRegisteredTaskRunnerHostedService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Message = "Modular Web App is running. Drop module .nupkg files into src/ModularWebApp.Host/packages and call POST /admin/reconcile then POST /admin/reload-shells."
}));

//app.MapGet("/admin/catalog", async (IActivePackageCatalog activePackageCatalog, CancellationToken cancellationToken) =>
//{
//    var snapshot = await activePackageCatalog.GetActivePackagesAsync(cancellationToken);

//    return Results.Ok(snapshot.Packages.Select(x => new
//    {
//        x.PackageId,
//        x.Version,
//        x.FeedName,
//        x.SourceName
//    }));
//});

//app.MapPost("/admin/reconcile", async (IReconciliationService reconciliationService, CancellationToken cancellationToken) =>
//{
//    var trigger = new ReconciliationTrigger(TriggerType.Manual, "admin/reconcile");
//    var result = await reconciliationService.TriggerAsync(trigger, cancellationToken);
//    return Results.Ok(result);
//});

app.MapGet("/test/add-workflow", async ([FromServices] IAddCommand<WorkflowDefinition> addCommand, [FromServices] IWorkflowDefinitionQueries queries, CancellationToken c) =>
{
    var entityToAdd = TestWorkflowDefinition.Entity;
    await addCommand.AddAsync(entityToAdd, c);

    var filter = new WorkflowDefinitionFilter { Id = entityToAdd.Id, DefinitionId = entityToAdd.DefinitionId };
    var result = await queries.FindAsync(filter, c);

    return Results.Ok(result);
});

app.MapShells();
app.Run();