using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// A real CShells web host with one shell that composes a single feature. Activating that shell runs its
/// <see cref="IShellInitializer"/>s from a scope off the shell's provider, which is the only lifecycle hook the composed
/// <c>Elsa.Workbench</c> server runs for shell-scoped services: shell-scoped hosted services do not start.
/// </summary>
internal static class ShellActivationHost
{
    private const string ShellName = "identity-activation-probe";

    /// <param name="configureServices">Root registrations; CShells copies them into the shell's provider.</param>
    public static Task<WebApplication> StartAsync<TFeature>(
        string environment,
        Action<TFeature>? configureFeature = null,
        Action<IServiceCollection>? configureServices = null) where TFeature : class, IShellFeature =>
        StartAsync(environment, builder =>
        {
            configureServices?.Invoke(builder.Services);
            builder.Services.AddCShellsAspNetCore(shells => shells
                .WithAssemblies(typeof(TFeature).Assembly)
                .AddShell(ShellName, shell => shell.WithFeature<TFeature>(configureFeature ?? (_ => { }))));
        });

    /// <summary>
    /// Composes the shell from configuration, as a <c>shells.json</c> does: each setting binds onto the feature property
    /// of the same name, and a setting that matches no property is silently ignored.
    /// </summary>
    public static Task<WebApplication> StartFromConfigurationAsync<TFeature>(
        string environment,
        string featureName,
        IReadOnlyDictionary<string, string?> settings) where TFeature : class, IShellFeature =>
        StartAsync(environment, builder =>
        {
            builder.Configuration.AddInMemoryCollection(settings.Select(setting =>
                KeyValuePair.Create($"CShells:Shells:{ShellName}:Features:{featureName}:{setting.Key}", setting.Value)));
            builder.Services.AddCShellsAspNetCore(shells => shells
                .WithAssemblies(typeof(TFeature).Assembly)
                .WithConfigurationProvider(builder.Configuration));
        });

    public static Task<IShell> ActivateShellAsync(this WebApplication host) =>
        host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

    private static async Task<WebApplication> StartAsync(string environment, Action<WebApplicationBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        configure(builder);

        var app = builder.Build();
        app.MapShells();
        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }
}
