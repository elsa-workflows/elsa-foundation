using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Xunit;
using static Elsa.Modularity.EntityFramework.Tests.ActivationGuardHarness;

namespace Elsa.Modularity.EntityFramework.Tests;

/// <summary>
/// Spec 171 User Story 3 and FR-067–FR-069: what the guard refuses, what it lets through, and what it
/// never says while doing either.
/// </summary>
public sealed class EfPendingMigrationActivationGuardTests : IDisposable
{
    /// <summary>
    /// A value that appears nowhere but inside a connection string, so a test can tell "this refusal does
    /// not name the connection" apart from "this driver happened not to echo one today".
    /// </summary>
    private const string Sentinel = "SENTINEL-8c31f7";

    private readonly ActivationGuardHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Refuses_a_feature_whose_module_has_a_pending_migration_under_validate()
    {
        var connection = ConnectionTo(_harness.Database("pending"));

        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(Enabled(SecretsFeature, connection: connection)));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Equal(SecretsFeature, refusal.Feature);
        // Pinned whole rather than by keyword: this message is built only from the feature name, the module
        // name and fixed text, and an exact match is what keeps a later edit from interpolating a driver's
        // message — the one realistic way a credential would reach an operator (FR-061).
        Assert.Equal(
            $"Feature '{SecretsFeature}' depends on EF module 'Secrets' which has migrations that are not applied " +
            "to its database, and this host runs 'Elsa:Persistence:EntityFramework:Migrate:Policy=Validate'. " +
            $"Nothing was saved. Apply them out of process, then enable the feature again:{Environment.NewLine}" +
            // SQLite cannot be scripted idempotently (ADR 0076 D5), so `apply` is what the operator is told
            // to run there; a `script` command would name one that refuses.
            $"  dotnet elsa persistence apply --modules Secrets --provider Sqlite --connection-env ELSA_CONNECTION{Environment.NewLine}" +
            // Secrets declares a post-migration action, so the whole path is named, not only its first step.
            "  dotnet elsa persistence post-migrate --modules Secrets --provider Sqlite",
            refusal.Reason);
    }

    [Fact]
    public async Task Allows_the_same_feature_under_automigrate()
    {
        var connection = ConnectionTo(_harness.Database("pending"));

        var decision = await _harness.Guard(EfMigratePolicy.AutoMigrate)
            .EvaluateAsync(Request(Enabled(SecretsFeature, connection: connection)));

        Assert.True(decision.IsAllowed);
    }

    /// <summary>User Story 3, scenario 3: the same request, retried after the operator applied the migrations.</summary>
    [Fact]
    public async Task Allows_the_same_feature_once_its_migrations_are_applied_out_of_process()
    {
        var connection = ConnectionTo(_harness.Database("applied"));
        var guard = _harness.Guard(EfMigratePolicy.Validate);
        var request = Request(Enabled(SecretsFeature, connection: connection));
        Assert.False((await guard.EvaluateAsync(request)).IsAllowed);

        await ApplyMigrationsAsync("Secrets", connection);

        Assert.True((await guard.EvaluateAsync(request)).IsAllowed);
    }

    /// <summary>
    /// User Story 3, scenario 5: an unreadable database fails closed, and says so in its own words — every
    /// migration is treated as pending rather than the schema being assumed fine.
    /// </summary>
    [Fact]
    public async Task Refuses_a_feature_whose_database_cannot_be_read_and_never_names_the_connection()
    {
        var connection = ConnectionTo(_harness.UnreadableDatabase($"unreadable-{Sentinel}"));

        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(Enabled(SecretsFeature, connection: connection)));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Equal(SecretsFeature, refusal.Feature);
        Assert.Contains($"Feature '{SecretsFeature}' depends on EF module 'Secrets' whose database could not be reached", refusal.Reason, StringComparison.Ordinal);
        // The failure's type, not its message: a driver's text never enters the refusal at all. This is the
        // assertion that pins it — a corrupt SQLite file's own message happens to quote nothing back, so the
        // sentinel check below cannot fail here however the message were built. The test after this one
        // drives a failure that does quote the connection back.
        Assert.Contains($"({nameof(SqliteException)})", refusal.Reason, StringComparison.Ordinal);
        Assert.EndsWith("dotnet elsa persistence post-migrate --modules Secrets --provider Sqlite", refusal.Reason, StringComparison.Ordinal);
        AssertNothingLeaked(connection, decision);
    }

    /// <summary>
    /// The leak path that actually exists: a driver failure whose own message quotes the connection string
    /// back. SQLite's connection-string parser does exactly that for a keyword it does not know, so this
    /// drives a real echo rather than trusting that no driver ever produces one — the assertion that the
    /// refusal is free of it then means something, where against a failure that echoes nothing it would not.
    /// </summary>
    [Fact]
    public async Task Never_carries_a_driver_message_that_echoes_the_connection_into_a_refusal()
    {
        var connection = $"{ConnectionTo(_harness.Database("echo"))};Unknown{Sentinel}=1";

        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(Enabled(SecretsFeature, connection: connection)));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Contains("whose database could not be reached", refusal.Reason, StringComparison.Ordinal);
        AssertNothingLeaked(connection, decision);
    }

    /// <summary>
    /// FR-069's other half, proved by a connection whose use throws rather than by reading the code: the
    /// same unreadable database that refuses under Validate passes under AutoMigrate, which it could not do
    /// if the guard had opened it.
    /// </summary>
    [Fact]
    public async Task Does_not_open_the_database_at_all_under_automigrate()
    {
        var connection = ConnectionTo(_harness.UnreadableDatabase("unreadable"));
        var request = Request(Enabled(SecretsFeature, connection: connection));

        Assert.False((await _harness.Guard(EfMigratePolicy.Validate).EvaluateAsync(request)).IsAllowed);
        Assert.True((await _harness.Guard(EfMigratePolicy.AutoMigrate).EvaluateAsync(request)).IsAllowed);
    }

    /// <summary>
    /// An engine this host cannot bind is a refusal whatever the policy is (FR-069) — the one thing
    /// AutoMigrate does not wave through. This test project references exactly one provider package, so
    /// SqlServer is genuinely unbindable here rather than mocked into being.
    /// </summary>
    [Theory]
    [InlineData(EfMigratePolicy.Validate)]
    [InlineData(EfMigratePolicy.AutoMigrate)]
    public async Task Refuses_a_provider_engine_that_cannot_bind_under_either_policy(EfMigratePolicy policy)
    {
        var decision = await _harness.Guard(policy)
            .EvaluateAsync(Request(Enabled(SecretsFeature, provider: "SqlServer", connection: $"Server=.;Password={Sentinel}")));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Contains("'SqlServer' provider engine could not be bound", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", refusal.Reason, StringComparison.Ordinal);
        AssertNothingLeaked($"Server=.;Password={Sentinel}", decision);
    }

    /// <summary>A connection this host cannot resolve is refused too, and names the setting rather than a value.</summary>
    [Fact]
    public async Task Refuses_a_feature_whose_connection_name_resolves_to_nothing()
    {
        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(Enabled(SecretsFeature, connectionName: "NoSuchConnection")));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Contains("whose database connection could not be resolved", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("'NoSuchConnection'", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-065: the dashboard feature has no Provider or connection of its own, and neither of the features
    /// that register its two modules' migrations is enabled, so the guard passes and leaves the refusal to
    /// the feature's own startup check.
    /// </summary>
    [Fact]
    public async Task Passes_a_feature_with_no_settings_of_its_own_when_no_enabled_feature_registers_its_modules()
    {
        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(EnabledWithoutSettings(DashboardFeature)));

        Assert.True(decision.IsAllowed);
    }

    /// <summary>
    /// The other half of FR-065: with a registering feature enabled, that feature's configuration is what
    /// the dashboard is evaluated through — and the refusal names both, so an operator can see why a
    /// feature carrying no connection settings was refused over a database.
    /// </summary>
    [Fact]
    public async Task Evaluates_a_feature_with_no_settings_of_its_own_through_the_enabled_feature_that_registers_its_module()
    {
        var connection = ConnectionTo(_harness.Database("runtime-pending"));

        var decision = await _harness.Guard(EfMigratePolicy.Validate).EvaluateAsync(Request(
            EnabledWithoutSettings(DashboardFeature),
            Enabled(RuntimeFeature, connection: connection)));

        Assert.Equal(
            new[] { DashboardFeature, RuntimeFeature },
            decision.Refusals.Select(refusal => refusal.Feature).Order(StringComparer.Ordinal).ToArray());
        var dashboard = Assert.Single(decision.Refusals, refusal => refusal.Feature == DashboardFeature);
        Assert.Contains($"depends on EF module 'Workflows.Runtime' (configured by enabled feature '{RuntimeFeature}'", dashboard.Reason, StringComparison.Ordinal);
        // Its other module has no enabled registering feature, so it produces nothing at all.
        Assert.DoesNotContain("Workflows.Design", dashboard.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignores_a_feature_no_UsesEfModule_maps_to_a_module()
    {
        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(Enabled("SomeFeatureWithNoEfModule", connection: ConnectionTo(_harness.Database("unused")))));

        Assert.True(decision.IsAllowed);
    }

    /// <summary>A feature the request turns off is not being activated, whatever its module's schema says.</summary>
    [Fact]
    public async Task Ignores_a_feature_the_request_disables()
    {
        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(Disabled(SecretsFeature, connection: ConnectionTo(_harness.Database("pending")))));

        Assert.True(decision.IsAllowed);
    }

    /// <summary>
    /// Finding 2: the guard evaluates every feature the request leaves enabled, not only the ones it turns
    /// on, so a request that disables an unrelated feature is still refused by an already-enabled feature
    /// whose module has a pending migration. Kept on purpose — refusing early beats saving a shell whose
    /// reload would then fail, and disabling the offending feature is still how an operator recovers — but
    /// unspecified by the FRs, so this pins it rather than leaving it to change silently.
    /// </summary>
    [Fact]
    public async Task Refuses_a_request_that_only_disables_an_unrelated_feature_while_another_enabled_feature_has_a_pending_migration()
    {
        var connection = ConnectionTo(_harness.Database("pending"));

        var decision = await _harness.Guard(EfMigratePolicy.Validate).EvaluateAsync(Request(
            Enabled(SecretsFeature, connection: connection),
            Disabled("SomeUnrelatedFeatureWithNoEfModule")));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Equal(SecretsFeature, refusal.Feature);
    }

    /// <summary>
    /// Finding 1: the guard's own container only ever carries the host's configuration (it is composed
    /// there, not into any shell's), so the policy it must honor is the one a shell would actually resolve —
    /// its own <c>Configuration</c> node when that node defines the <c>Migrate</c> section, the host's
    /// otherwise (see <c>EfMigrateOptions</c>). A shell that declares AutoMigrate over a host running
    /// Validate must not be falsely refused, and must not open the database to find that out.
    /// </summary>
    [Fact]
    public async Task Allows_a_shell_that_declares_automigrate_over_a_host_running_validate_without_opening_the_database()
    {
        var connection = ConnectionTo(_harness.UnreadableDatabase("shell-automigrate"));

        var decision = await _harness.Guard(EfMigratePolicy.Validate)
            .EvaluateAsync(Request(EfMigratePolicy.AutoMigrate, Enabled(SecretsFeature, connection: connection)));

        Assert.True(decision.IsAllowed);
    }

    /// <summary>
    /// Finding 1's other direction: a shell that declares Validate over a host running AutoMigrate must be
    /// refused here, rather than silently passing and falling back to the later, post-save Prepare-phase
    /// refusal the spec treats as the worse path.
    /// </summary>
    [Fact]
    public async Task Refuses_a_shell_that_declares_validate_over_a_host_running_automigrate()
    {
        var connection = ConnectionTo(_harness.Database("shell-validate"));

        var decision = await _harness.Guard(EfMigratePolicy.AutoMigrate)
            .EvaluateAsync(Request(EfMigratePolicy.Validate, Enabled(SecretsFeature, connection: connection)));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Equal(SecretsFeature, refusal.Feature);
    }

    /// <summary>
    /// A cancelled request is not a refused one: reporting the operator's own cancellation as a refusal
    /// would name a schema problem that was never diagnosed.
    /// </summary>
    [Fact]
    public async Task Propagates_cancellation_instead_of_reporting_it_as_a_refusal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _harness.Guard(EfMigratePolicy.Validate).EvaluateAsync(
                Request(Enabled(SecretsFeature, connection: ConnectionTo(_harness.Database("pending")))),
                cancellation.Token));
    }

    /// <summary>
    /// Every place a refusal can be read: the decision, the exception the feature-management service builds
    /// from it including any inner exception, and every log line written through the guard's own container.
    /// </summary>
    private void AssertNothingLeaked(string connection, FeatureActivationDecision decision)
    {
        var exception = new FeatureActivationRefusedException(decision.Refusals);
        foreach (var text in decision.Refusals.Select(refusal => refusal.Reason).Append(exception.ToString()).Concat(_harness.Logs.Lines))
        {
            Assert.DoesNotContain(Sentinel, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(connection, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Data Source", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
