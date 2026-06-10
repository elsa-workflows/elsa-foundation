using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Activities.Composition.Runtime;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Caching.Memory;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.Newtonsoft;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

//builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
//{
//    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
//    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
//});
//builder.Services.AddNuplaneAdmin();
//builder.Services.AddSingleton<NuplaneFeatureAssemblyProvider>();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithHostAssemblies()
        //.WithAssemblyProvider<NuplaneFeatureAssemblyProvider>()

        .WithAssemblies(
            typeof(PrimitivesFeature).Assembly,
            typeof(FileSystemLockingFeature).Assembly,
            typeof(SerializationFeature).Assembly,
            typeof(NewtonsoftSerializationFeature).Assembly,
            typeof(TasksFeature).Assembly,
            typeof(MemoryCacheFeature).Assembly,
            typeof(MediatorFeature).Assembly,
            typeof(EventsFeature).Assembly,
            typeof(ExpressionsFeature).Assembly,
            typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly,
            typeof(WorkflowsDesignApiFeature).Assembly,
            typeof(SqliteActivitiesDesignPersistenceShellFeature).Assembly,
            typeof(ActivitiesDesignApiFeature).Assembly,

            // Construction seam (Runtime side): the dispatch factory + registry, the CLR kind, and the
            // Workflow kind. These populate the constructor registry the bridge dispatches through.
            typeof(ActivitiesRuntimeFeature).Assembly,
            typeof(ActivitiesPrimitivesFeature).Assembly,
            typeof(ActivitiesCompositionRuntimeFeature).Assembly,

            // Reconciliation (Design side): the universal pass + the CLR assembly scanner source, which
            // populate the catalog with WriteLine + WorkflowDefinitionActivity as CLR rows at startup.
            typeof(ActivitiesDesignReconciliationFeature).Assembly,
            typeof(ClrActivityReconciliationFeature).Assembly,

            // The bridge: publishing endpoints that construct a live activity from a catalog row.
            typeof(WorkflowsPublishingApiFeature).Assembly,

            // Runtime vertical slice: execute published WorkflowExecutable artifacts.
            typeof(WorkflowsRuntimeApiFeature).Assembly
        )

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

var app = builder.Build();

app.MapShells();
app.Run();
