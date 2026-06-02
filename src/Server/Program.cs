using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Elsa.Caching.Memory;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Locking.FileSystem;
using Elsa.Mapping;
using Elsa.Mediator;
using Elsa.Primitives;
using Elsa.Serialization;
using Elsa.Server.Shells;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;

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
            typeof(MappingFeature).Assembly,
            typeof(FileSystemLockingFeature).Assembly,
            typeof(SerializationFeature).Assembly,
            typeof(TasksFeature).Assembly,
            typeof(MemoryCacheFeature).Assembly,
            typeof(MediatorFeature).Assembly,
            typeof(EventsFeature).Assembly,
            typeof(ExpressionsFeature).Assembly,
            typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly,
            typeof(WorkflowsDesignApiFeature).Assembly,
            typeof(SqliteActivitiesDesignPersistenceShellFeature).Assembly,
            typeof(ActivitiesDesignApiFeature).Assembly
        )

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

builder.Services.AddHostedService<ShellRegisteredTaskRunnerHostedService>();

var app = builder.Build();

app.MapShells();
app.Run();
