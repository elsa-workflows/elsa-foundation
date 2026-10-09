using CShells.Features;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Nuplane;
using CShells;
using Elsa.Persistence.Schema;
using Elsa.Testing;
using Elsa.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Events;
using Nuplane.Observability;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// The Workbench's CShells.Nuplane profile: after a reconcile that
/// added, updated or removed a package it refreshes the runtime feature catalog, so the next shell reload composes the new
/// assemblies, and it reloads the active shells itself only when <c>Elsa:Shells:ReloadOnPackageChange</c> is true. That it runs
/// after Nuplane's auto-loader, which loads those assemblies, is pinned against the Workbench's real composition in
/// <see cref="HostOwnedServicesAreSharedWithShellsTests"/>.
/// </summary>
public sealed class WorkbenchShellCatalogRefreshTests : IDisposable
{
    private static readonly ResolvedPackage Notes = new("Elsa.Samples.Nuplane.Notes", "1.1.0", "local-packages", "/packages/notes", DateTimeOffset.UnixEpoch, "feed");
    private static readonly PackageChangeSet Unchanged = ChangeSet();
    private static readonly PackageChangeSet NotesUpdated = ChangeSet(updated: [Notes]);

    private readonly NuplaneHostTestComposition.NuplaneHostTestFixture _fixture;
    private CapturingLogger _log => _fixture.Logger;
    private NuplaneHostTestComposition.RefreshCountingCatalog _catalog => _fixture.Catalog;
    private NuplaneHostTestComposition.ScriptedShellRegistry _registry => _fixture.Registry;
    private IConfigurationRoot _configuration => _fixture.Configuration;
    private INuplaneObserver _observer => _fixture.Observer;
    private IObserverEventDispatcher _dispatcher => _fixture.Dispatcher;
    private NuplaneHostTestComposition.FollowingObserver _following => _fixture.Following;

    public WorkbenchShellCatalogRefreshTests() => _fixture = NuplaneHostTestComposition.CreateAdapter(false, "default");

    public void Dispose() => _fixture.Dispose();

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

    [Fact(DisplayName = "Changing the reload setting alone schedules no catalog or shell work")]
    public void ConfigurationChange_WithoutAnEligibleCompletion_DoesNotRefreshOrReload()
    {
        ReloadOnPackageChange("true");

        Assert.Equal((0, 0), (_catalog.Refreshes, _registry.Reloads));
        Assert.Equal(0, _following.ReconciledCalls);
    }

    [Fact]
    public async Task A_failing_refresh_does_not_propagate_and_the_next_reconcile_tries_again_though_it_changed_nothing()
    {
        ReloadOnPackageChange("true");
        _catalog.Failure = new InvalidOperationException("The catalog could not be rebuilt.");
        var callsBeforeFailure = _following.ReconciledCalls;

        await ReconcileAsync(NotesUpdated);

        Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(_log.Exceptions, exception => ReferenceEquals(exception, _catalog.Failure));
        AssertFailureDispatchOrder(_catalog.Failure!);
        Assert.Equal(0, _registry.Reloads);
        Assert.Equal(callsBeforeFailure + 1, _following.ReconciledCalls);

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
        var callsBeforeFailure = _following.ReconciledCalls;

        await ReconcileAsync(NotesUpdated);

        Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(_log.Exceptions, exception => ReferenceEquals(exception, _registry.Failure));
        AssertFailureDispatchOrder(_registry.Failure!);
        Assert.Equal(callsBeforeFailure + 1, _following.ReconciledCalls);

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
            new ReloadResult("default", null, null, new AggregateException(
                "Shell 'default' failed to activate.",
                new InvalidOperationException("Initializer wrapped the refusal.", new AggregateException(new PendingRefusal()))))
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

    private Task ReconcileAsync(PackageChangeSet changeSet) => _dispatcher.PublishReconciledAsync(changeSet, [Notes], CancellationToken.None);

    private void ReloadOnPackageChange(string? value) =>
        _fixture.SetReloadOnPackageChange(bool.TryParse(value, out var enabled) ? enabled : null);

    private void AssertFailureDispatchOrder(Exception expectedException)
    {
        var entries = _log.Entries;
        var errorIndex = entries.ToList().FindIndex(entry => entry.Level == LogLevel.Error);
        var warningIndex = entries.ToList().FindIndex(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Observer callback error", StringComparison.Ordinal));
        Assert.True(errorIndex >= 0 && warningIndex > errorIndex, "The adapter must log its original error before the dispatcher reports it and proceeds.");
        Assert.Same(expectedException, _log.Exceptions[errorIndex]);
        Assert.Contains("OnPackagesReconciledAsync", entries[errorIndex].Message, StringComparison.Ordinal);
        Assert.Contains("correlation", entries[errorIndex].Message, StringComparison.Ordinal);
        Assert.Contains("OnPackagesReconciledAsync", entries[warningIndex].Message, StringComparison.Ordinal);
        Assert.Contains("correlation", entries[warningIndex].Message, StringComparison.Ordinal);
        Assert.True(_following.ReconciledCalls > 0, "The dispatcher must continue to the following observer after isolating the adapter failure.");
    }

    private static PackageChangeSet ChangeSet(ResolvedPackage[]? added = null, ResolvedPackage[]? updated = null, string[]? removed = null) =>
        new(added ?? [], updated ?? [], removed ?? [], "correlation", DateTimeOffset.UnixEpoch);

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
