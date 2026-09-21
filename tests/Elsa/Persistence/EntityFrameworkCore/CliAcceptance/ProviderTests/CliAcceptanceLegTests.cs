using Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;
using Elsa.Workbench.Tests;
using MySql.Data.MySqlClient;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests;

/// <summary>
/// The acceptance leg issue #1875 asks for: <c>dotnet elsa persistence script</c> against a built host with
/// no database connection, a raw ADO client applying the result twice against a real container -- proving
/// idempotency -- and then a real Workbench process starting under
/// <c>Elsa:Persistence:EntityFramework:Migrate:Policy=Validate</c> against that same database. Every server
/// engine claim made across #1874-#1877 rested on SQLite alone until this leg exists, because Docker pulls
/// are blocked on the machine that built them (see this issue's own text); this is what actually exercises
/// PostgreSQL, SQL Server, and MySQL.
/// </summary>
public sealed class CliAcceptanceLegTests
{
    /// <summary>
    /// Every EF module Workbench's own committed <c>shells.json</c> enables by default -- the 13-name
    /// vocabulary minus <c>Elsa3.Activities.Design.Import</c>, which that composition does not turn on. This
    /// is what lets the last step's host-wide <c>Migrate:Policy=Validate</c> mean something: every module the
    /// running host actually activates has to already be migrated, not just the modules this test happened
    /// to pick.
    /// </summary>
    private static readonly string[] Modules =
    [
        "Activities.Design",
        "Diagnostics.OpenTelemetry",
        "Diagnostics.StructuredLogs",
        "Identity.Iam",
        "Identity.ProviderConfiguration",
        "Secrets",
        "Studio.Preferences",
        "Workflows.Design",
        "Workflows.Publishing",
        "Workflows.Runtime",
        "Workflows.Runtime.Distributed.CommandTransport",
        "Workflows.Runtime.Distributed.Placement"
    ];

    /// <summary>
    /// The Workbench shell feature keys that back <see cref="Modules"/> above, read from
    /// <c>src/Apps/Elsa.Workbench/shells.json</c>: every enabled feature there whose name contains
    /// <c>EntityFrameworkCore</c> except <c>WorkflowsDashboardEntityFrameworkCore</c>, which declares no
    /// <c>Provider</c> setting of its own and is skipped by the provider-agreement comparison the same way
    /// (spec 171, research.md). Two keys -- <c>FoundationIdentityAspNetCoreIdentityEntityFrameworkCore</c>
    /// and <c>IdentityIamEntityFrameworkCore</c> -- back the same <c>Identity.Iam</c> module and context, so
    /// both must point at the same database or the host refuses to start on their disagreement, not on a
    /// pending migration.
    /// </summary>
    private static readonly string[] WorkbenchEfFeatureKeys =
    [
        "ActivitiesDesignEntityFrameworkCore",
        "DiagnosticsOpenTelemetryEntityFrameworkCore",
        "DiagnosticsStructuredLogsEntityFrameworkCore",
        "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore",
        "IdentityIamEntityFrameworkCore",
        "IdentityProviderConfigurationEntityFrameworkCore",
        "SecretsEntityFrameworkCore",
        "StudioPreferencesEntityFrameworkCore",
        "WorkflowsDesignEntityFrameworkCore",
        "WorkflowsPublishingEntityFrameworkCore",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence",
        "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence"
    ];

    [SkippableFact]
    public Task Scripted_sql_is_idempotent_and_a_host_starts_validated_on_postgresql() =>
        ProviderDatabase.RunAsync("PostgreSql", connectionString => RunLegAsync("PostgreSql", connectionString));

    [SkippableFact]
    public Task Scripted_sql_is_idempotent_and_a_host_starts_validated_on_sql_server() =>
        ProviderDatabase.RunAsync("SqlServer", connectionString => RunLegAsync("SqlServer", connectionString));

    /// <summary>
    /// Quarantined pending #1914, not a container-unavailable self-skip: <c>persistence script --provider
    /// MySql</c> emits <c>IF NOT EXISTS(...) BEGIN ... END;</c> at the top level, which MySQL's grammar does
    /// not allow outside a stored routine, so a real server rejects the scripted output with a 1064 syntax
    /// error before this leg's idempotency or host-start steps ever run. The defect lives in
    /// <c>MySql.EntityFrameworkCore</c>'s own idempotent generator, not in this leg or its splitting logic;
    /// see <see cref="Applying_the_generated_mysql_script_still_fails_with_1914s_syntax_error"/>, which pins
    /// the failure against a real container and goes red the moment #1914 is fixed -- that is the signal to
    /// delete this skip.
    /// </summary>
    [SkippableFact(Skip = "Quarantined pending #1914: `persistence script --provider MySql` emits SQL MySQL cannot execute (IF NOT EXISTS(...) BEGIN...END outside a routine, ERROR 1064). Not a container-unavailable skip -- see Applying_the_generated_mysql_script_still_fails_with_1914s_syntax_error, which fails once #1914 is fixed.")]
    public Task Scripted_sql_is_idempotent_and_a_host_starts_validated_on_mysql() =>
        ProviderDatabase.RunAsync("MySql", connectionString => RunLegAsync("MySql", connectionString));

    /// <summary>
    /// Pins #1914: applying the MySQL script generated by <c>persistence script</c> to a real MySQL container
    /// currently fails with the top-level <c>IF NOT EXISTS</c> syntax error the issue describes. Self-skips
    /// like every other container test when no Docker is available -- distinct from that self-skip, this test
    /// running and passing means "the container was there and the defect still reproduces". If #1914 is fixed
    /// upstream, applying the script stops throwing, <see cref="Assert.ThrowsAsync{T}(Func{Task})"/> fails,
    /// and that failure is the signal to delete
    /// <see cref="Scripted_sql_is_idempotent_and_a_host_starts_validated_on_mysql"/>'s quarantine above. Same
    /// pattern #1872 used for its named Secrets design-time-factory exception.
    /// </summary>
    [SkippableFact]
    public Task Applying_the_generated_mysql_script_still_fails_with_1914s_syntax_error() =>
        ProviderDatabase.RunAsync("MySql", PinMySqlScriptFailureAsync);

    private static async Task PinMySqlScriptFailureAsync(string connectionString)
    {
        var output = Directory.CreateTempSubdirectory("elsa-cli-acceptance-1914-").FullName;
        try
        {
            var script = DotnetElsaTool.Script("MySql", Modules, output);
            Assert.True(script.ExitCode == 0, $"script exited {script.ExitCode}.\nstdout: {script.Output}\nstderr: {script.Error}");

            var scripts = Directory.EnumerateFiles(output, "*.sql")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(File.ReadAllText)
                .ToArray();

            var exception = await Assert.ThrowsAsync<MySqlException>(() =>
                RawScriptApplication.ApplyAsync("MySql", connectionString, scripts));
            Assert.Contains("IF NOT EXISTS", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static async Task RunLegAsync(string provider, string connectionString)
    {
        var output = Directory.CreateTempSubdirectory("elsa-cli-acceptance-").FullName;
        try
        {
            // Step 1: script, against a built host, with no database connection open at all.
            var script = DotnetElsaTool.Script(provider, Modules, output);
            Assert.True(script.ExitCode == 0, $"script exited {script.ExitCode}.\nstdout: {script.Output}\nstderr: {script.Error}");

            var scripts = Directory.EnumerateFiles(output, "*.sql")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(File.ReadAllText)
                .ToArray();
            Assert.Equal(Modules.Length, scripts.Length);

            // Step 2: a raw client applies the result twice. The idempotent generator's own guarantee is
            // that the second run changes nothing; an exception here -- a duplicate object, a broken split --
            // is exactly the regression this leg exists to catch.
            await RawScriptApplication.ApplyAsync(provider, connectionString, scripts);
            await RawScriptApplication.ApplyAsync(provider, connectionString, scripts);

            // Step 3: a real Workbench process starts under Migrate:Policy=Validate against that same
            // database. Reaching a ready response at all is the proof: Validate throws on any module that
            // still reports a pending migration.
            var settings = new Dictionary<string, string>
            {
                ["Elsa:Persistence:EntityFramework:Migrate:Policy"] = "Validate"
            };
            foreach (var key in WorkbenchEfFeatureKeys)
            {
                settings[$"CShells:Shells:default:Features:{key}:Provider"] = provider;
                settings[$"CShells:Shells:default:Features:{key}:ConnectionString"] = connectionString;
            }

            var shell = WorkbenchShell.Development with { Settings = settings };
            await using var workbench = await WorkbenchProcess.StartAsync(shell);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }
}
