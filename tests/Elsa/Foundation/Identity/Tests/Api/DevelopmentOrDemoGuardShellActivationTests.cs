using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Proves the <see cref="DevelopmentOrDemoGuard"/> fires on its <b>real production path</b>: the CShells
/// shell-activation lifecycle. In the composed <c>Elsa.Server</c> host, shell-scoped
/// <see cref="IHostedService"/>s do NOT run — only <see cref="IShellInitializer"/>s do, resolved from a scope
/// off the shell's provider. This test drives exactly that path (a real CShells host, a shell activated via
/// <see cref="IShellRegistry.GetOrActivateAsync"/> → <c>RunInitializersAsync</c> → the guard's
/// <see cref="IShellInitializer.InitializeAsync"/>) rather than the plain <see cref="IHostedService.StartAsync"/>
/// path the other guard tests exercise. It also confirms the guard can resolve <see cref="IHostEnvironment"/>
/// in the shell scope (the shell provider copies the root service descriptors), so the guard actually throws
/// rather than silently failing open.
/// </summary>
public sealed class DevelopmentOrDemoGuardShellActivationTests
{
    private const string ShellName = "identity-guard-probe";

    [Fact]
    public async Task DevDemo_Flag_In_Production_Aborts_Shell_Activation_With_The_Actionable_Message()
    {
        await using var host = await StartHostAsync(Environments.Production);

        var registry = host.Services.GetRequiredService<IShellRegistry>();

        // Activating the shell runs its initializers — the guard's real production path. It must abort.
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => registry.GetOrActivateAsync(ShellName));

        var message = Flatten(exception);
        Assert.Contains("IsDevelopmentOrDemo", message, StringComparison.Ordinal);
        Assert.Contains("not 'Development'", message, StringComparison.Ordinal);
        Assert.Contains("no insecure escape hatch in production", message, StringComparison.Ordinal);
        // Proves the guard resolved IHostEnvironment in the shell scope and saw Production (not the null/
        // fail-open path): the message names the actual environment rather than the "could not resolve" text.
        Assert.Contains("environment is 'Production'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevDemo_Flag_In_Development_Activates_The_Shell_Cleanly()
    {
        await using var host = await StartHostAsync(Environments.Development);

        var registry = host.Services.GetRequiredService<IShellRegistry>();

        // Same shell, same dev/demo flag, but Development environment — activation must succeed (no throw).
        var shell = await registry.GetOrActivateAsync(ShellName);
        Assert.NotNull(shell);
    }

    private static async Task<WebApplication> StartHostAsync(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddCShellsAspNetCore(shells =>
        {
            shells
                .WithAssemblies(typeof(GuardProbeFeature).Assembly)
                .AddShell(ShellName, shell => shell.WithFeature<GuardProbeFeature>());
        });

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }

    /// <summary>Flattens an exception chain (activation may wrap the guard's exception) into one string.</summary>
    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add(current.Message);
        return string.Join(" | ", parts);
    }

    /// <summary>
    /// A minimal shell feature that registers only the <see cref="DevelopmentOrDemoGuard"/> (as both an
    /// <see cref="IHostedService"/> and an <see cref="IShellInitializer"/>) exactly as the real identity
    /// features do — isolating the guard's shell-activation behaviour without composing the full OpenIddict/EF
    /// stack.
    /// </summary>
    public sealed class GuardProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddIdentityDevelopmentOrDemoGuard("FoundationIdentityOpenIddict");
    }
}
