using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Activities;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.JavaScript.Rendering;
using Elsa.Mediator;
using Elsa.Server.Constants;


//using Elsa.Expressions.JavaScript.Activities;
//using Elsa.Expressions.JavaScript;
using Elsa.Server.Nuplane;
using Elsa.Server.Shells;
using FastEndpoints;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
});
builder.Services.AddNuplaneAdmin();
builder.Services.AddSingleton<NuplaneFeatureAssemblyProvider>();

builder.Services.AddFastEndpoints(opts =>
{
    opts.DisableAutoDiscovery = true;
    opts.Assemblies = [typeof(Program).Assembly];
});

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithHostAssemblies()
        .WithAssemblyProvider<NuplaneFeatureAssemblyProvider>()

        .WithAssemblies(
            typeof(ExpressionsFeature).Assembly,
            typeof(JavaScriptFeature).Assembly,
            typeof(JintFeature).Assembly,
            typeof(JavaScriptActivitiesFeature).Assembly,
            typeof(JavaScriptActivitiesEndpointsFeature).Assembly,
            typeof(JavaScriptRenderingFeature).Assembly,
            typeof(JavaScriptRenderingEndpointsFeature).Assembly,

            typeof(MediatorFeature).Assembly
        )

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

builder.Services.AddHostedService<ShellRegisteredTaskRunnerHostedService>();

var app = builder.Build();

// Map host-owned admin endpoints at root, before MapShells, so they're
// attached to the global route builder and not subject to shell lifecycle.
app.UseFastEndpoints(c =>
{
    c.Serializer.RequestDeserializer = async (req, modelType, _, ct) =>
    {
        var options = JsonSerializerOptionsCache.Instance;
        return await JsonSerializer.DeserializeAsync(req.Body, modelType, options, ct);
    };
    c.Serializer.ResponseSerializer = (resp, dto, contentType, _, ct) =>
    {
        var options = JsonSerializerOptionsCache.Instance;
        resp.ContentType = contentType;
        return JsonSerializer.SerializeAsync(resp.Body, dto, dto?.GetType() ?? typeof(object), options, ct);
    };
});

app.MapShells();
app.Run();
