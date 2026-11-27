using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// What a run that never needed a record leaves in a real runtime database when the host turns completion records off
/// under Ephemeral: one released ownership row, which every run in every mode leaves, and nothing else.
/// </summary>
public sealed class EphemeralNoTraceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_run_that_was_never_committed_leaves_only_its_released_ownership_row(bool recordCompletion)
    {
        var capture = new CommandCaptureInterceptor();
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(
            PersistenceMode.Ephemeral,
            maxActivitiesPerCommit: 50,
            services => services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(capture)),
            recordCompletion);
        var executable = ReferenceWorkflows.WriteLineSequence();
        await fixture.PublishAsync(executable);
        var rowsBefore = await CountRowsPerTableAsync(fixture);
        var commandsBefore = capture.Commands.Count;

        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        output.WriteLine($"EF commands, RecordCompletion {recordCompletion}: {capture.Commands.Count - commandsBefore}.");
        var rowsAfter = await CountRowsPerTableAsync(fixture);
        var grown = rowsAfter.Where(table => table.Value != rowsBefore.GetValueOrDefault(table.Key)).Select(table => table.Key).ToArray();
        var execution = await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId);
        if (recordCompletion)
        {
            Assert.Equal(WorkflowExecutionStatus.Completed, execution?.Status);
            Assert.Contains(grown, table => table.Contains("workflow_execution", StringComparison.Ordinal));
        }
        else
        {
            Assert.Null(execution);
            Assert.Equal([LivenessTable], grown);
        }
    }

    private const string LivenessTable = "elsa_runtime_execution_liveness_state";

    private static async Task<Dictionary<string, long>> CountRowsPerTableAsync(HttpEndpointHostFixture fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            counts[table] = (long)(await count.ExecuteScalarAsync())!;
        }

        return counts;
    }
}
