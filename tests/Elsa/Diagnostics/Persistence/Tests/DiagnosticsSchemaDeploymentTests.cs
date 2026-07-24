extern alias DiagnosticsGroundwork;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.DiagnosticRecords;
using Microsoft.Data.Sqlite;
using Xunit;
using DiagnosticsDeploymentSchema = DiagnosticsGroundwork::Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkDeploymentSchema;
using DiagnosticsManifestSource = DiagnosticsGroundwork::Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkStorageManifestSource;

namespace Elsa.Diagnostics.Persistence.Tests;

/// <summary>
/// Contract tests for the one pre-start Diagnostics deployment source. They intentionally drive the local
/// Groundwork.Tool process rather than re-implementing its planning or mutation behavior in Elsa.
/// </summary>
public sealed class DiagnosticsSchemaDeploymentTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-diagnostics-schema-{Guid.NewGuid():N}");

    [Fact]
    public void Deployment_source_declares_the_document_manifest_and_all_diagnostic_streams()
    {
        var source = new DiagnosticsDeploymentSchema();
        var diagnosticSource = Assert.IsAssignableFrom<IDiagnosticRecordDeploymentManifestSource>(source);
        var deployment = diagnosticSource.CreateDeploymentManifest();

        Assert.True(source.CreateManifest().HasSameDefinitionAs(deployment.Storage));
        Assert.Equal(5, deployment.Streams.Count);
        Assert.Equal(deployment.Streams.Count, deployment.Streams.Select(stream => stream.Stream.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(deployment.Streams, DiagnosticRecordStreamDefinitionValidator.ValidateAndThrow);
    }

    [Fact]
    public async Task Validate_of_missing_combined_schema_is_read_only_and_lists_every_pending_stream()
    {
        var database = Database("missing.db");
        var result = await RunToolAsync("validate", database);

        Assert.NotEqual(0, result.ExitCode);
        using var report = result.ParseReport();
        Assert.Equal("blocked", report.RootElement.GetProperty("outcome").GetString());
        Assert.False(report.RootElement.GetProperty("targetMutated").GetBoolean());
        Assert.Equal(5, DiagnosticRecords(report.RootElement)
            .GetProperty("pendingStreams").GetArrayLength());
        Assert.False(await TableExistsAsync(database, "groundwork_diagnostic_records"));
    }

    [Fact]
    public async Task Apply_then_validate_materializes_the_combined_schema_and_is_idempotent()
    {
        var database = Database("apply.db");

        var applied = await RunToolAsync("apply", database, "--safe");
        Assert.Equal(0, applied.ExitCode);
        using (var report = applied.ParseReport())
        {
            Assert.Equal("applied", report.RootElement.GetProperty("outcome").GetString());
            Assert.True(report.RootElement.GetProperty("targetMutated").GetBoolean());
            Assert.Equal(0, DiagnosticRecords(report.RootElement)
                .GetProperty("pendingStreams").GetArrayLength());
        }

        var validated = await RunToolAsync("validate", database);
        Assert.Equal(0, validated.ExitCode);
        using (var report = validated.ParseReport())
        {
            Assert.Equal("ready", report.RootElement.GetProperty("outcome").GetString());
            Assert.False(report.RootElement.GetProperty("targetMutated").GetBoolean());
            Assert.Equal(0, DiagnosticRecords(report.RootElement)
                .GetProperty("pendingStreams").GetArrayLength());
        }
        Assert.True(await TableExistsAsync(database, "groundwork_diagnostic_records"));
        Assert.True(await TableExistsAsync(database, "groundwork_diagnostic_trim_operations"));
    }

    [Fact]
    public async Task Validate_detects_diagnostic_schema_drift_without_repairing_it()
    {
        var database = Database("drift.db");
        var applied = await RunToolAsync("apply", database, "--safe");
        Assert.Equal(0, applied.ExitCode);

        Assert.True(await TableExistsAsync(database, "groundwork_diagnostic_trim_operations"));
        await ExecuteAsync(database, "DROP TABLE groundwork_diagnostic_trim_operations;");
        var validation = await RunToolAsync("validate", database);

        Assert.NotEqual(0, validation.ExitCode);
        using var report = validation.ParseReport();
        Assert.Equal("blocked", report.RootElement.GetProperty("outcome").GetString());
        Assert.False(report.RootElement.GetProperty("targetMutated").GetBoolean());
        Assert.Contains(report.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("code").GetString() == "GW-DIAG-DEPLOY-001");
        Assert.False(await TableExistsAsync(database, "groundwork_diagnostic_trim_operations"));
    }

    [Fact]
    public async Task Declared_diagnostics_capability_mismatch_is_rejected_before_any_schema_mutation()
    {
        var declaration = await new DiagnosticsManifestSource().CreateDeclarationAsync();
        var provider = new ProviderIdentity("groundwork-sqlite", "test");
        var result = new ProviderCapabilityValidator().ValidateRuntimeFit(
            declaration.Manifest,
            new ProviderCapabilityReport(
                provider,
                new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit },
                new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit },
                IndexCapabilities.All,
                Enum.GetValues<PortableQueryOperation>().ToHashSet(),
                Enum.GetValues<ConcurrencyKind>()
                    .Where(mode => mode != ConcurrencyKind.Optimistic)
                    .ToHashSet(),
                []));

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Code == "GW-CAP-005");
    }

    [Fact]
    public async Task Concurrent_apply_reaches_one_ready_target_even_when_multiple_stages_report_mutation()
    {
        var database = Database("concurrent.db");
        var applications = await Task.WhenAll(
            RunToolAsync("apply", database, "--safe"),
            RunToolAsync("apply", database, "--safe"));

        Assert.All(applications, application => Assert.Equal(0, application.ExitCode));
        var reports = applications.Select(application => application.ParseReport()).ToArray();
        try
        {
            Assert.Contains(reports, report => report.RootElement.GetProperty("targetMutated").GetBoolean());
            Assert.All(reports, report => Assert.True(
                new[] { "applied", "ready" }.Contains(report.RootElement.GetProperty("outcome").GetString())));
        }
        finally
        {
            foreach (var report in reports)
                report.Dispose();
        }

        var validation = await RunToolAsync("validate", database);
        Assert.Equal(0, validation.ExitCode);
        using var ready = validation.ParseReport();
        Assert.Equal("ready", ready.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, DiagnosticRecords(ready.RootElement)
            .GetProperty("pendingStreams").GetArrayLength());
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<SchemaToolResult> RunToolAsync(
        string command,
        string database,
        string? option = null,
        string provider = "sqlite")
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "tool", "run", "groundwork", "--", command,
                     "--manifest-assembly", typeof(DiagnosticsDeploymentSchema).Assembly.Location,
                     "--manifest-type", typeof(DiagnosticsDeploymentSchema).FullName!,
                     "--provider", provider,
                     "--connection", $"Data Source={database}",
                     "--output", "json"
                 })
            start.ArgumentList.Add(argument);
        if (option is not null)
            start.ArgumentList.Add(option);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Groundwork.Tool.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, await output, await error);
    }

    private string Database(string name) => Path.Combine(_directory, name);

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(string database, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        command.Parameters.AddWithValue("@name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../.."));

    private static JsonElement DiagnosticRecords(JsonElement report)
    {
        var records = report.GetProperty("diagnosticRecords");
        Assert.Equal(JsonValueKind.Object, records.ValueKind);
        return records;
    }

    private sealed record SchemaToolResult(int ExitCode, string Output, string Error)
    {
        public JsonDocument ParseReport()
        {
            try
            {
                return JsonDocument.Parse(Output);
            }
            catch (JsonException exception)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Groundwork.Tool did not emit a JSON deployment report.{Environment.NewLine}" +
                    $"stdout:{Environment.NewLine}{Output}{Environment.NewLine}" +
                    $"stderr:{Environment.NewLine}{Error}",
                    exception);
            }
        }
    }
}
