using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// How a difference is classified (FR-045, FR-046). Every case here is built on disk rather than through a
/// host, because the classification is the contract: a version-only difference reported as changed SQL
/// sends a reviewer looking for a schema change that does not exist, and changed SQL reported as a version
/// difference hides one that does.
/// </summary>
public sealed class ScriptCheckClassificationTests : IDisposable
{
    private const string File1 = "01-acme-widgets.sql";
    private static readonly string[] TwoMigrations = ["20260101000000_Initial", "20260102000000_AddLabel"];

    private readonly TempDirectory committed = new("elsa-cli-committed-");
    private readonly TempDirectory regenerated = new("elsa-cli-regenerated-");

    public void Dispose()
    {
        committed.Dispose();
        regenerated.Dispose();
    }

    [Fact]
    public void An_artifact_that_matches_what_regenerates_is_up_to_date()
    {
        Write(committed, "CREATE TABLE acme_widgets;\n");
        Write(regenerated, "CREATE TABLE acme_widgets;\n");

        var report = Compare();

        Assert.Equal(ToolExitCode.Success, report.ExitCode);
        Assert.Equal("up to date", report.Headline);
    }

    [Fact]
    public void A_statement_changed_with_the_same_migrations_is_reported_as_an_edit()
    {
        Write(committed, "CREATE TABLE acme_widgets_edited;\n");
        Write(regenerated, "CREATE TABLE acme_widgets;\n");

        var report = Compare();

        Assert.Equal(ToolExitCode.NegativeResult, report.ExitCode);
        Assert.Equal("SQL differs", report.Headline);
        Assert.Contains(report.Lines, line => line.Contains("a statement changed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Same byte difference, different cause: the module gained a migration since the artifact was
    /// generated, so the file is out of date rather than edited. Reported distinctly (User Story 4,
    /// scenarios 2 and 3).
    /// </summary>
    [Fact]
    public void A_module_that_changed_since_the_artifact_was_generated_is_reported_as_stale()
    {
        Write(committed, "CREATE TABLE acme_widgets;\n", ids: [TwoMigrations[0]]);
        Write(regenerated, "CREATE TABLE acme_widgets;\nALTER TABLE acme_widgets ADD Label;\n");

        var report = Compare();

        Assert.Equal(ToolExitCode.NegativeResult, report.ExitCode);
        Assert.Equal("SQL differs", report.Headline);
        Assert.Contains(report.Lines, line => line.Contains("out of date", StringComparison.Ordinal) && line.Contains("1 migration(s) and now has 2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_sql_file_the_plan_does_not_name_is_reported_as_an_orphan()
    {
        Write(committed, "CREATE TABLE acme_widgets;\n");
        Write(regenerated, "CREATE TABLE acme_widgets;\n");
        System.IO.File.WriteAllText(committed.File("09-removed-module.sql"), "SELECT 1;\n");

        var report = Compare();

        Assert.Equal(ToolExitCode.NegativeResult, report.ExitCode);
        Assert.Contains(report.Lines, line => line.StartsWith("09-removed-module.sql:", StringComparison.Ordinal) && line.Contains("orphan", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_the_plan_names_and_the_directory_lacks_is_reported_as_missing()
    {
        Write(committed, "CREATE TABLE acme_widgets;\n");
        Write(regenerated, "CREATE TABLE acme_widgets;\n");
        System.IO.File.Delete(committed.File(File1));

        Assert.Contains(Compare().Lines, line => line.Contains("names it for 'Acme.Widgets' and it is not in the directory", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("efCoreVersion")]
    [InlineData("package.version")]
    [InlineData("engine.version")]
    public void A_package_upgrade_with_no_migration_change_is_reported_as_a_version_difference(string field)
    {
        Write(committed, "CREATE TABLE acme_widgets;\n");
        Write(
            regenerated,
            "CREATE TABLE acme_widgets;\n",
            efCoreVersion: field == "efCoreVersion" ? "10.0.11" : "10.0.10",
            engineVersion: field == "engine.version" ? "10.0.1" : "10.0.0",
            packageVersion: field == "package.version" ? "4.0.0-preview.2" : "4.0.0-preview.1");

        var report = Compare();

        Assert.Equal(ToolExitCode.NegativeResult, report.ExitCode);
        Assert.Equal("manifest versions differ, SQL identical", report.Headline);
        Assert.Contains(report.Lines, line => line.Contains(field, StringComparison.Ordinal));
        Assert.Contains(report.Lines, line => line.Contains("nothing new to apply", StringComparison.Ordinal));
    }

    /// <summary>
    /// The direction that must never be mistaken for the harmless one: when a statement moved as well as a
    /// version, the report is the one that asks for review.
    /// </summary>
    [Fact]
    public void A_version_difference_never_masks_a_sql_difference()
    {
        Write(committed, "CREATE TABLE acme_widgets_edited;\n");
        Write(regenerated, "CREATE TABLE acme_widgets;\n", efCoreVersion: "10.0.11");

        var report = Compare();

        Assert.Equal("SQL differs", report.Headline);
        Assert.DoesNotContain(report.Lines, line => line.Contains("efCoreVersion", StringComparison.Ordinal));
    }

    /// <summary>
    /// A manifest field a package upgrade does not explain — here the package id itself — still reports the
    /// documented "manifest versions differ, SQL identical" headline (the spec names only two headlines), but
    /// the detail line names exactly which field moved so it is never mistaken for a version bump.
    /// </summary>
    [Fact]
    public void A_manifest_field_no_upgrade_explains_is_reported_without_claiming_a_version_moved()
    {
        Write(committed, "CREATE TABLE acme_widgets;\n");
        Write(regenerated, "CREATE TABLE acme_widgets;\n", packageId: "Acme.Widgets.Renamed");

        var report = Compare();

        Assert.Equal("manifest versions differ, SQL identical", report.Headline);
        Assert.Contains(report.Lines, line => line.Contains("modules[0].package.id", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("efCoreVersion", true)]
    [InlineData("engine.version", true)]
    [InlineData("modules[0].package.version", true)]
    [InlineData("modules[11].package.version", true)]
    [InlineData("engine.package", false)]
    [InlineData("modules[0].package.id", false)]
    [InlineData("host.name", false)]
    [InlineData("modules[0].version", false)]
    public void Only_the_fields_a_package_upgrade_moves_count_as_version_fields(string path, bool isVersion)
    {
        Assert.Equal(isVersion, ScriptCheck.IsVersionField(path));
    }

    private ScriptCheckReport Compare() =>
        ScriptCheck.Compare(committed.Path, regenerated.Path, MigrationPlan.Read(committed.Path));

    private static void Write(
        TempDirectory directory,
        string sql,
        string efCoreVersion = "10.0.10",
        string engineVersion = "10.0.0",
        string packageVersion = "4.0.0-preview.1",
        string packageId = "Acme.Widgets",
        IReadOnlyList<string>? ids = null)
    {
        System.IO.File.WriteAllText(directory.File(File1), sql);
        System.IO.File.WriteAllText(directory.File(MigrationPlan.FileName), $$"""
            {
              "schemaVersion": 1,
              "provider": "PostgreSql",
              "engine": { "package": "Npgsql.EntityFrameworkCore.PostgreSQL", "version": "{{engineVersion}}", "source": "host-deps-file" },
              "efCoreVersion": "{{efCoreVersion}}",
              "schema": null,
              "idempotent": true,
              "ordering": "dependsOn-then-name",
              "host": { "name": "Contoso.Host", "providerAgreement": "not-checked", "shell": null, "environment": "Production" },
              "modules": [
                {
                  "order": 1,
                  "module": "Acme.Widgets",
                  "file": "{{File1}}",
                  "sha256": "unused-by-the-comparison",
                  "package": { "id": "{{packageId}}", "version": "{{packageVersion}}", "source": "host-deps-file" },
                  "assembly": "Acme.Widgets",
                  "context": "WidgetsPostgreSqlDbContext",
                  "historyTable": "__EFMigrationsHistory_AcmeWidgets",
                  "migrations": {
                    "from": "0",
                    "to": "{{(ids ?? TwoMigrations)[^1]}}",
                    "count": {{(ids ?? TwoMigrations).Count}},
                    "ids": [{{string.Join(", ", (ids ?? TwoMigrations).Select(id => $"\"{id}\""))}}]
                  },
                  "dependsOn": [],
                  "postMigration": []
                }
              ]
            }
            """);
    }
}
