using CShells.AspNetCore.Configuration;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Api.Contracts;
using CShells.AspNetCore.Extensions;
using CShells.AspNetCore.Middleware;
using CShells.AspNetCore.Resolution;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Resolution;
using Elsa.Studio.Api.Features;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Studio.Core.Contracts;

namespace Elsa.Studio.Hosting;

/// <summary>Application composition entry points for reusable Studio hosting.</summary>
public static class StudioHostingExtensions
{
    /// <summary>Registers Studio hosting. Call once before Build; callbacks override Studio:Hosting configuration.</summary>
    /// <exception cref="StudioHostingException">The configured shell file cannot be loaded.</exception>
    /// <exception cref="ArgumentException">An explicit hosting option is invalid.</exception>
    public static StudioBuilder AddElsaStudio(this WebApplicationBuilder builder, Action<StudioHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Services.Any(service => service.ServiceType == typeof(StudioBuilder)))
            throw new InvalidOperationException("AddElsaStudio may only be called once per application.");
        var options = new StudioHostingOptions();
        builder.Configuration.GetSection("Studio:Hosting").Bind(options);
        configure?.Invoke(options);
        var settings = StudioHostingSettings.Create(options, builder.Environment, builder.Configuration);
        if (settings.ShellsFile is not null)
        {
            try
            {
                ((IConfigurationBuilder)builder.Configuration).Add(new StudioJsonConfigurationSource(settings.ShellsFile, optional: false));
            }
            catch (Exception error) when (error is IOException or FormatException or UnauthorizedAccessException)
            {
                throw new StudioHostingException($"Could not load Studio shell configuration '{settings.ShellsFile}'.", error);
            }
        }
        settings = settings with { AuthEnabled = builder.Configuration.GetValue("Studio:Auth:Enabled", false) };
        builder.WebHost.UseStaticWebAssets();
        builder.Services.AddSingleton(settings);
        builder.Services.TryAddScoped<IStudioModuleManifestProvider, SelectedShellModuleManifestProvider>();
        builder.Services.AddSingleton<IConfigurationRoot>(builder.Configuration);
        ServiceDescriptor? blueprintSource = null;
        ServiceDescriptor? shellRegistry = null;
        builder.Services.AddSingleton<IOptions<StudioHostingOptions>>(_ => Options.Create(new StudioHostingOptions
        {
            ShellName = settings.ShellName,
            // A replacement registry/provider controls its own configuration. Addons must not write an unused file.
            ShellsFile = ReferenceEquals(blueprintSource, builder.Services.LastOrDefault(service => service.ServiceType == typeof(IShellBlueprintProvider)))
                && ReferenceEquals(shellRegistry, builder.Services.LastOrDefault(service => service.ServiceType == typeof(IShellRegistry)))
                ? settings.ShellsFile : null,
            IndexFile = settings.IndexFile, QueryTokenPathPrefixes = settings.QueryTokenPathPrefixes.ToArray()
        }));
        var gate = new StudioConfigurationMutationGate();
        builder.Services.AddSingleton(gate);
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        StudioBackendAuthorityAccessor.Add(builder.Services, builder.Configuration);
        builder.Services.AddStudioBridgeAuth(builder.Configuration, settings);
        builder.Services.AddStudioBackendManagementBridge(builder.Configuration);
        CShellsBuilder? shellBuilder = null;
        builder.Services.AddCShellsAspNetCore(shells =>
        {
            shellBuilder = shells;
            shells.WithAssemblies(typeof(StudioApiFeature).Assembly)
                .WithConfigurationProvider(builder.Configuration)
                .WithWebRouting(routing => routing.EnablePathRouting = true)
                .ConfigureResolverPipeline(pipeline => pipeline
                    .Use<WebRoutingShellResolver>(0)
                    .UseFallback<StudioSelectedShellFallbackResolver>())
                .ShareSingletonWithShells<StudioBridgeSessionCache>()
                .ShareSingletonWithShells<TimeProvider>()
                .ShareSingletonWithShells<StudioBackendManagementOptions>()
                .ShareSingletonWithShells<StudioSessionTransport>();
            shells.ShareSingletonWithShells<IStudioBackendAuthorityAccessor>();
        });
        blueprintSource = builder.Services.Last(service => service.ServiceType == typeof(IShellBlueprintProvider));
        shellRegistry = builder.Services.Last(service => service.ServiceType == typeof(IShellRegistry));
        builder.Services.AddSingleton(provider => new DefaultShellResolver(provider.GetServices<IShellResolverStrategy>(), provider.GetRequiredService<IOptions<ShellResolverOptions>>().Value));
        builder.Services.Replace(ServiceDescriptor.Singleton<IShellResolver, StudioShellResolver>());
        // Ownership may change at the same host/path/method after reload. A cached shell ID would bypass this guard.
        builder.Services.PostConfigure<ShellMiddlewareOptions>(middleware => middleware.EnableCaching = false);
        builder.Services.AddHostedService<StudioHostingStartup>();
        var studio = new StudioBuilder(builder, shellBuilder ?? throw new InvalidOperationException("CShells registration did not run."), gate);
        builder.Services.AddSingleton(studio);
        return studio;
    }

    /// <summary>Installs pre-routing activation, routing, authentication, authorization and static/shell middleware.</summary>
    public static WebApplication UseElsaStudio(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<StudioHostingSettings>();
        app.UseStudioShellActivation();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseStudioStaticFiles();
        // CShells exposes this as IApplicationBuilder middleware, rather than an endpoint-route-builder extension.
        app.MapShells();
        return app;
    }

    /// <summary>Maps runtime settings, prioritized discovery, unknown API404 and the configured SPA fallback.</summary>
    public static IEndpointRouteBuilder MapElsaStudio(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.ServiceProvider.GetRequiredService<StudioHostingSettings>();
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        endpoints.MapGet("/studio-runtime.js", (Func<HttpContext, Task<IResult>>)(context => StudioRuntimeEndpoint.ReadAsync(context, configuration, settings)));
        endpoints.MapActiveShellStudioApi();
        endpoints.MapStudioBackendManagementBridge();
        endpoints.MapFallback("/_elsa/{**path}", () => Results.NotFound());
        endpoints.MapStudioFallback(settings.IndexFile);
        return endpoints;
    }
}
