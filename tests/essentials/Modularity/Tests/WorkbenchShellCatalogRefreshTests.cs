using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.Schema;
using Elsa.Testing;
using Elsa.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nuplane.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// The Workbench's observer of Nuplane reconciles, <see cref="ShellCatalogRefreshOnPackagesChanged"/>: after a reconcile that
/// added, updated or removed a package it refreshes the runtime feature catalog, so the next shell reload composes the new
/// assemblies, and it reloads the active shells itself only when <c>Elsa:Shells:ReloadOnPackageChange</c> is true. That it runs
/// after Nuplane's auto-loader, which loads those assemblies, is pinned against the Workbench's real composition in
/// <see cref="HostOwnedServicesAreSharedWithShellsTests"/>.
/// </summary>
public sealed class WorkbenchShellCatalogRefreshTests
{
    private static readonly ResolvedPackage Notes = new("Elsa.Samples.Nuplane.Notes", "1.1.0", "local-packages", "/packages/notes", DateTimeOffset.UnixEpoch, "feed");
    private static readonly PackageChangeSet Unchanged = ChangeSet();
    private static readonly PackageChangeSet NotesUpdated = ChangeSet(updated: [Notes]);

    private readonly CapturingLogger _log = new();
    private readonly RefreshCountingCatalog _catalog = new();
    private readonly ScriptedShellRegistry _registry = new();
    private readonly IConfigurationRoot _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:default:Name"] = "default", ["CShells:Shells:tenant-a:Name"] = "tenant-a" })
        .Build();
    private readonly ShellCatalogRefreshOnPackagesChanged _observer;

    public WorkbenchShellCatalogRefreshTests()
    {
        _registry.Activate("default");
        _observer = new(_catalog, _registry, _configuration, new Logger<ShellCatalogRefreshOnPackagesChanged>(_log));
    }

    [Fact]
    public async Task Does_nothing_while_no_shell_is_active_because_the_first_activation_builds_the_catalog_from_what_is_loaded()
    {
        _registry.Deactivate("default");
        ReloadOnPackageChange("true");

        await ReconcileAsync(NotesUpdated);

        Assert.Equal((0, 0), (_catalog.Refreshes, _registry.Reloads));
    }

    [Theory]
    [InlineData("added")]
    [InlineData("updated")]
    [InlineData("removed")]
    public async Task Refreshes_the_catalog_after_a_reconcile_that_changed_a_package(string change)
    {
        await ReconcileAsync(change switch
        {
            "added" => ChangeSet(added: [Notes]),
            "updated" => NotesUpdated,
            _ => ChangeSet(removed: [Notes.Id])
        });

        Assert.Equal(1, _catalog.Refreshes);
    }

    [Fact]
    public async Task Skips_a_reconcile_that_changed_nothing_once_every_change_was_acted_on()
    {
        await ReconcileAsync(Unchanged);
        await ReconcileAsync(NotesUpdated);
        await ReconcileAsync(Unchanged);
        await ReconcileAsync(Unchanged);

        Assert.Equal(1, _catalog.Refreshes);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("false", 0)]
    [InlineData("yes", 0)]
    [InlineData("true", 1)]
    [InlineData("True", 1)]
    public async Task Reloads_the_active_shells_only_when_ReloadOnPackageChange_is_true(string? setting, int reloads)
    {
        ReloadOnPackageChange(setting);

        await ReconcileAsync(NotesUpdated);

        Assert.Equal((1, reloads), (_catalog.Refreshes, _registry.Reloads));
    }

    [Fact]
    public async Task A_failing_refresh_does_not_propagate_and_the_next_reconcile_tries_again_though_it_changed_nothing()
    {
        ReloadOnPackageChange("true");
        _catalog.Failure = new InvalidOperationException("The catalog could not be rebuilt.");

        await ReconcileAsync(NotesUpdated);

        Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(0, _registry.Reloads);

        _catalog.Failure = null;
        await ReconcileAsync(Unchanged);
        await ReconcileAsync(Unchanged);

        Assert.Equal((2, 1), (_catalog.Refreshes, _registry.Reloads));
    }

    [Fact]
    public async Task A_failing_reload_does_not_propagate_and_the_next_reconcile_tries_again()
    {
        ReloadOnPackageChange("true");
        _registry.Failure = new InvalidOperationException("The registry could not reload.");

        await ReconcileAsync(NotesUpdated);

        Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);

        _registry.Failure = null;
        await ReconcileAsync(Unchanged);
        await ReconcileAsync(Unchanged);

        Assert.Equal(2, _registry.Reloads);
    }

    /// <summary>
    /// CShells keeps a shell's previous generation active when its reload fails and only says so in the result, so a reload
    /// whose result is read as a count would log a module's refusal as a successful reload.
    /// </summary>
    [Fact]
    public async Task A_refused_reload_is_reported_as_refused_with_its_command_not_counted_as_reloaded_and_tried_again()
    {
        ReloadOnPackageChange("true");
        _registry.Results =
        [
            new ReloadResult("tenant-a", null, null, null),
            // Wrapped, as an initializer's failure can arrive out of a shell's activation.
            new ReloadResult("default", null, null, new InvalidOperationException("Shell 'default' failed to activate.", new PendingRefusal()))
        ];

        await ReconcileAsync(NotesUpdated);

        Assert.Contains(_log.Entries, entry => entry is { Level: LogLevel.Information } && entry.Message.Contains("Reloaded 1 active shell(s)", StringComparison.Ordinal));
        var refusal = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("'default'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"--host \"{Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}\" --modules Orders", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IEfModuleRefusal.HostPlaceholder, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_log.Entries, entry => entry.Level == LogLevel.Error);

        _registry.Results = [new ReloadResult("tenant-a", null, null, null), new ReloadResult("default", null, null, null)];
        await ReconcileAsync(Unchanged);
        await ReconcileAsync(Unchanged);

        Assert.Equal(2, _registry.Reloads);
        Assert.Contains(_log.Entries, entry => entry.Message.Contains("Reloaded 2 active shell(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reload_failure_that_is_no_refusal_is_logged_as_an_error_naming_the_shell()
    {
        ReloadOnPackageChange("true");
        _registry.Results = [new ReloadResult("default", null, null, new InvalidOperationException("The blueprint could not be built."))];

        await ReconcileAsync(NotesUpdated);

        Assert.Contains(_log.Entries, entry => entry.Message.Contains("Reloaded 0 active shell(s)", StringComparison.Ordinal));
        var failure = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("'default'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_reconcile_propagates_its_cancellation_and_is_not_logged_as_a_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _catalog.Failure = new OperationCanceledException(cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _observer.OnPackagesReconciledAsync(NotesUpdated, [], cancelled.Token));

        Assert.DoesNotContain(_log.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    private Task ReconcileAsync(PackageChangeSet changeSet) => _observer.OnPackagesReconciledAsync(changeSet, [Notes], CancellationToken.None);

    private void ReloadOnPackageChange(string? value) => _configuration[ShellCatalogRefreshOnPackagesChanged.ReloadKey] = value;

    private static PackageChangeSet ChangeSet(ResolvedPackage[]? added = null, ResolvedPackage[]? updated = null, string[]? removed = null) =>
        new(added ?? [], updated ?? [], removed ?? [], "correlation", DateTimeOffset.UnixEpoch);

    private sealed class RefreshCountingCatalog : IRuntimeFeatureCatalog
    {
        private readonly FakeRuntimeFeatureCatalog _snapshots = new();

        public int Refreshes { get; private set; }

        public Exception? Failure { get; set; }

        public Task<IRuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            Refreshes++;
            return Failure is null ? Task.FromResult(_snapshots.CurrentSnapshot) : Task.FromException<IRuntimeFeatureCatalogSnapshot>(Failure);
        }

        public IRuntimeFeatureCatalogSnapshot CurrentSnapshot => _snapshots.CurrentSnapshot;
        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Holds which shells are active, and answers a reload of them with what a test scripts, counting each one.</summary>
    private sealed class ScriptedShellRegistry : IShellRegistry
    {
        private readonly HashSet<string> _active = new(StringComparer.Ordinal);

        public int Reloads { get; private set; }

        public IReadOnlyList<ReloadResult> Results { get; set; } = [new ReloadResult("default", null, null, null)];

        public Exception? Failure { get; set; }

        public void Activate(string name) => _active.Add(name);

        public void Deactivate(string name) => _active.Remove(name);

        public IShell? GetActive(string name) => _active.Contains(name) ? new StubShell(name) : null;

        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            Reloads++;
            return Failure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<ReloadResult>>(Failure);
        }

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }

    private sealed class StubShell(string name) : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create(name, 1);
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public IShellScope BeginScope() => throw new NotSupportedException();
        public IDrainOperation? Drain => null;
    }

    /// <summary>An EF module's refusal as this host meets it: a type of the module's own, known here only by the shared interface.</summary>
    private sealed class PendingRefusal() : InvalidOperationException(
        $"EF module 'Orders' has pending migrations: 20260930_One. Apply them out of process: dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders"), IEfModuleRefusal
    {
        public string Module => "Orders";

        public string Code => IEfModuleRefusal.PendingMigrationsCode;

        public IReadOnlyList<string> PendingMigrations => ["20260930_One"];

        public string? Command => $"dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders";
    }
}
