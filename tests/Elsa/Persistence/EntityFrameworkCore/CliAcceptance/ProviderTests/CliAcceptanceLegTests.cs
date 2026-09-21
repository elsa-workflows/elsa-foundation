using Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;
using Elsa.Workbench.Tests;
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
    /// Skipped from #1875 until #1914, because <c>persistence script --provider MySql</c> emitted
    /// <c>IF NOT EXISTS(...) BEGIN ... END;</c> at the top level, which MySQL's grammar allows only inside a
    /// stored routine: a real server rejected the artifact with a 1064 syntax error before this leg's
    /// idempotency or host-start steps ever ran. <c>EfMySqlIdempotentScript</c> hoists those guards into a
    /// per-module stored procedure, so the leg now runs exactly like the other two. One thing it exercises
    /// that the others do not: applying a MySQL artifact needs <c>CREATE ROUTINE</c> and <c>ALTER ROUTINE</c>,
    /// which is inherent to any correct idempotent MySQL script and which this leg's container user has.
    /// </summary>
    [SkippableFact]
    public Task Scripted_sql_is_idempotent_and_a_host_starts_validated_on_mysql() =>
        ProviderDatabase.RunAsync("MySql", connectionString => RunLegAsync("MySql", connectionString));

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
