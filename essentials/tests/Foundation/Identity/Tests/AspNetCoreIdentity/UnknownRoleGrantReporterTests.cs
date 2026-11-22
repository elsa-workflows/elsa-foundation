using System.Collections.Concurrent;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// A role grant that matches no registered permission authorizes nothing, so startup must name it: once per role and
/// grant, on the plain-host path and on the shell-activation path, and never for a grant that does reach a permission.
/// Only the identity catalog is registered here, so a grant for another feature's permission is unknown too.
/// </summary>
public sealed class UnknownRoleGrantReporterTests
{
    private const string ReporterCategory = "Elsa.Foundation.Identity.AspNetCoreIdentity.Services.UnknownRoleGrantReporter";
    private readonly RecordingLoggerProvider _logs = new();
    private readonly InMemoryIdentityStore _store = new();

    [Fact]
    public async Task Each_unknown_grant_is_reported_once_naming_its_role_and_grant()
    {
        await SaveRoleAsync("operators", "Operators", "identity.users.manage", "identity/users:read", "identity/*");
        await SaveRoleAsync("auditors", "Auditors", "workflows/runtime:read");
        using var host = await StartHostAsync();

        // A host that also runs the shell hook against the same container must not report a second time.
        foreach (var initializer in host.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();

        Assert.Equal(
            [
                ("Auditors", "workflows/runtime:read"),
                ("Operators", "identity.users.manage"),
                ("Operators", "identity/*")
            ],
            UnknownGrants());
    }

    [Fact]
    public async Task Grants_that_reach_a_registered_permission_are_not_reported()
    {
        await SaveRoleAsync(
            "administrators",
            "Administrators",
            "*",
            "*:*",
            "*:manage",
            "identity/*:read",
            "identity/users:*",
            "identity/users:manage",
            "IDENTITY/ROLES:READ");
        using var host = await StartHostAsync();

        Assert.Empty(Warnings());
    }

    [Fact]
    public async Task An_unreadable_role_store_is_reported_without_stopping_startup()
    {
        using var host = await StartHostAsync(services => services.AddSingleton<IRoleStore, ThrowingRoleStore>());

        var warning = Assert.Single(Warnings());
        Assert.Contains("Could not check the permission grants", warning.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public async Task Shell_activation_reports_unknown_grants()
    {
        // Shell-scoped hosted services do not run, so this proves the shell-initializer registration, the only one the
        // composed Elsa.Workbench host reaches.
        await SaveRoleAsync("operators", "Operators", "identity.users.manage");
        await using var host = await ShellActivationHost.StartAsync<ReporterProbeFeature>(
            Environments.Development,
            configureServices: RegisterSeededStoreAndLogs);

        await host.ActivateShellAsync();

        Assert.Equal([("Operators", "identity.users.manage")], UnknownGrants());
    }

    private async Task<IHost> StartHostAsync(Action<IServiceCollection>? configure = null)
    {
        // Production: the substrate alone does not compose ASP.NET Core Identity's managers, which Development's build-time
        // validation would demand although nothing here resolves them.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        RegisterSeededStoreAndLogs(builder.Services);
        configure?.Invoke(builder.Services);
        builder.Services.AddFoundationAspNetCoreIdentity();
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private void RegisterSeededStoreAndLogs(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(_logs);
        services.AddSingleton<IUserStore>(_store);
    }

    private ValueTask SaveRoleAsync(string id, string name, params string[] grants) =>
        ((IRoleStore)_store).SaveAsync(new RoleRecord(
            id,
            AspNetCoreIdentityDefaults.DefaultTenantId,
            name,
            null,
            new HashSet<string>(grants, StringComparer.Ordinal),
            System: false));

    private IReadOnlyList<LogEntry> Warnings() =>
        [.. _logs.Entries.Where(entry => entry.Category == ReporterCategory && entry.Level == LogLevel.Warning)];

    private IReadOnlyList<(string Role, string Grant)> UnknownGrants() =>
    [
        .. Warnings()
            .Select(entry => ((string)entry.State["RoleName"]!, (string)entry.State["Grant"]!))
            .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
            .ThenBy(pair => pair.Item2, StringComparer.Ordinal)
    ];

    public sealed class ReporterProbeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => services.AddFoundationAspNetCoreIdentity();
    }

    private sealed class ThrowingRoleStore : IRoleStore
    {
        public ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The role store is unreachable.");

        public ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The role store is unreachable.");

        public ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The role store is unreachable.");
    }

    private sealed record LogEntry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> State);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
                provider.Entries.Enqueue(new LogEntry(
                    category,
                    logLevel,
                    formatter(state, exception),
                    exception,
                    values.ToDictionary(pair => pair.Key, pair => pair.Value)));
            }
        }
    }
}
