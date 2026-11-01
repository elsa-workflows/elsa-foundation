using System.Diagnostics;
using System.Reflection;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Features;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Tests;

public sealed class ConnectionsLifecycleTests
{
    private const string ShellName = "connections-persistence";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Separate_processes_restore_committed_versions_status_and_references(bool consumerContext)
    {
        var path = NewPath();
        try
        {
            Assert.Contains("metadata-committed", await RunProbeAsync("write", path, consumerContext));
            Assert.Contains("metadata-restored", await RunProbeAsync("read", path, consumerContext));
        }
        finally { DeleteFiles(path); }
    }

    [Fact]
    public async Task Normal_shell_activation_and_reload_preserve_metadata_and_resolvable_credentials()
    {
        var path = NewPath();
        try
        {
            await using var host = await StartHostAsync(path, EfMigratePolicy.AutoMigrate);
            var registry = host.Services.GetRequiredService<IShellRegistry>();
            var first = await registry.GetOrActivateAsync(ShellName);
            using (var scope = first.ServiceProvider.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
                await manager.CreateAsync("tenant", Request());
                Assert.True((await scope.ServiceProvider.GetRequiredService<IConnectionRepository>().GetCapabilitiesAsync()).IsDurable);
            }
            var reload = await registry.ReloadAsync(ShellName);
            if (reload.Drain is not null) await reload.Drain.WaitAsync();
            var second = registry.GetActive(ShellName) ?? await registry.GetOrActivateAsync(ShellName);
            Assert.NotSame(first, second);
            using (var scope = second.ServiceProvider.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
                Assert.NotNull(await manager.FindAsync("tenant", "mail"));
                var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
                await using (var resolved = await resolver.ResolveAsync(new() { TenantId = "tenant", Name = "mail", RequiredType = ConnectionTypeNames.Http }))
                    Assert.Equal("fixture-only-token", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
                await manager.RevokeAsync("tenant", "mail");
                await Assert.ThrowsAsync<ConnectionUnavailableException>(async () => await resolver.ResolveAsync(new() { TenantId = "tenant", Name = "mail", RequiredType = ConnectionTypeNames.Http }));
            }
        }
        finally { DeleteFiles(path); }
    }

    [Fact]
    public async Task Validate_policy_refuses_an_unmigrated_connection_database()
    {
        var path = NewPath();
        try
        {
            // Secrets is already current, so the refusal must come from Connections.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSecretsEntityFrameworkCore(new() { ConnectionString = "Data Source=" + path + ";Pooling=False" });
            await using (var provider = services.BuildServiceProvider())
            {
                await using var scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SecretsDbContext>().Database.MigrateAsync();
            }
            await using var host = await StartHostAsync(path, EfMigratePolicy.Validate);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));
            Assert.Contains("pending migrations", failure.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Connections", failure.ToString(), StringComparison.Ordinal);
        }
        finally { DeleteFiles(path); }
    }

    internal static CreateConnectionRequest Request() => new()
    {
        Name = "mail", TypeName = ConnectionTypeNames.Http, AuthScheme = HttpAuthSchemes.Bearer,
        Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "example.test" },
        SecretFields = new Dictionary<string, ConnectionSecretFieldInput> { [HttpConnectionSecretFields.Token] = new() { Value = "fixture-only-token" } }
    };

    private static async Task<WebApplication> StartHostAsync(string path, EfMigratePolicy policy)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(ConnectionsEntityFrameworkCoreFeature).Assembly, typeof(ConnectionsFeature).Assembly,
                typeof(SecretsEntityFrameworkCoreFeature).Assembly, typeof(SecretsFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithConfiguration($"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}", policy.ToString())
                .WithFeature<SecretsFeature>(feature => feature.EncryptionKey = "connections-lifecycle-fixture-key")
                .WithFeature<SecretsEntityFrameworkCoreFeature>(feature => feature.ConnectionString = "Data Source=" + path + ";Pooling=False")
                .WithFeature<ConnectionsEntityFrameworkCoreFeature>(feature => feature.ConnectionString = "Data Source=" + path + ";Pooling=False")));
        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }

    private static async Task<string> RunProbeAsync(string operation, string path, bool consumerContext)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Elsa.Server.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = typeof(ConnectionsLifecycleTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = Path.Combine(root!.FullName, "Essentials/tests/Connections/Persistence/EntityFrameworkCore/RestartProbe/bin", configuration,
            "net10.0/Elsa.Connections.Persistence.EntityFrameworkCore.RestartProbe.dll");
        Assert.True(File.Exists(assembly), "The restart probe must be built through the test project's build-only reference.");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(operation);
        start.ArgumentList.Add(path);
        if (consumerContext) start.ArgumentList.Add("--consumer-context");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var text = await output + await error;
        Assert.True(process.ExitCode == 0, text);
        return text;
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), "connections-lifecycle-" + Guid.NewGuid().ToString("N") + ".db");
    private static void DeleteFiles(string path) { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
}
