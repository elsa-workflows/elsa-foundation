using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Nuplane;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nuplane;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Reconciliation;
using Nuplane.Sources.Directory.Configuration;

namespace Elsa.Foundation.Hosting;

/// <summary>Opt-in composition for Foundation shell hosting in an existing ASP.NET Core application.</summary>
public static class FoundationShellHostingExtensions
{
    /// <summary>Registers Nuplane feed and assembly-loading services with Foundation's integration profile.</summary>
    /// <remarks>Call before <see cref="AddFoundationShellHosting"/>. Relative feed paths use the host's content root.</remarks>
    public static void AddFoundationNuplane(this IHostApplicationBuilder builder, IConfiguration? configuration = null)
    {
        builder.AddNuplaneHostIntegration(configuration);
        builder.Services.ConfigureOptions<NuplaneIntegrationOptionsSetup>();
    }

    /// <summary>Registers Nuplane feeds and assembly loading from the host's <c>Nuplane</c> configuration.</summary>
    /// <remarks>
    /// Relative feed paths use the host's content root. Host-specific <see cref="NuplaneIntegrationOptions"/> profiles remain
    /// the caller's responsibility. Supply <paramref name="configuration"/> when the host uses a different configuration root.
    /// </remarks>
    public static void AddNuplaneHostIntegration(this IHostApplicationBuilder builder, IConfiguration? configuration = null)
    {
        var hostConfiguration = configuration ?? builder.Configuration;
        var nuplaneConfiguration = hostConfiguration.GetSection("Nuplane");
        builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
        {
            nuplane.UseBasePath(builder.Environment.ContentRootPath);
            nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
            nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
        });
        builder.Services.AddSingleton<IOptionsChangeTokenSource<NuplaneIntegrationOptions>>(
            new ConfigurationChangeTokenSource<NuplaneIntegrationOptions>(Options.DefaultName, hostConfiguration.GetSection("Elsa:Shells")));
    }

    /// <summary>Registers CShells with Nuplane discovery, configuration blueprints, and Foundation eager startup and retry.</summary>
    /// <remarks>
    /// Call once, after <see cref="AddFoundationNuplane"/> and after loading shell configuration. No routes or authentication
    /// are installed. The returned tracker is shared between the host and its shells for optional diagnostics.
    /// </remarks>
    public static ShellActivationTracker AddFoundationShellHosting(this IHostApplicationBuilder builder, Action<CShellsBuilder>? configure = null)
    {
        var services = builder.Services;
        services.AddCShellsAspNetCore(shells =>
        {
            shells.WithNuplaneFeatureDiscovery().ShareSingletonWithShells<IReconciliationTriggerIngress>();
            configure?.Invoke(shells);
            shells.WithConfigurationProvider(builder.Configuration);
        });

        services.AddShellActivationRunner();
        var tracker = new ShellActivationTracker();
        services.AddSingleton(tracker);
        if (EagerShellActivationHostedService.IsEnabled(builder.Configuration))
            services.AddHostedService<EagerShellActivationHostedService>();

        return tracker;
    }
}