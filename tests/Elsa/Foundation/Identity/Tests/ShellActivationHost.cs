using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    public static async Task<WebApplication> StartAsync<TFeature>(
        string environment,
        Action<TFeature>? configureFeature = null,
        Action<IServiceCollection>? configureServices = null) where TFeature : class, IShellFeature
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        configureServices?.Invoke(builder.Services);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(TFeature).Assembly)
            .AddShell(ShellName, shell => shell.WithFeature<TFeature>(configureFeature ?? (_ => { }))));

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

    public static Task<IShell> ActivateShellAsync(this WebApplication host) =>
        host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
}
