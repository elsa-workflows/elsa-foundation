using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Test-only G3 step timer and receipt writer. Reuses <see cref="WorkbenchPersistenceCapture"/>;
/// does not inject production interceptors or budget gates.
/// </summary>
internal sealed class G3Measurement(OwnedWorkbench host)
{
    public List<MeasuredStep> Steps { get; } = [];

    internal static object Environment() => new
    {
        os = RuntimeInformation.OSDescription,
        framework = RuntimeInformation.FrameworkDescription,
        provider = "SQLite",
        tool = "EF Core Database.Command/Transaction console logs parsed as CommandCaptureInterceptor-equivalent counts; Stopwatch per logical step",
        host = "Owned Workbench, one process"
    };

    internal static readonly string[] Limits =
    [
        "SQLite Owned Workbench, one host. No PostgreSQL/SQL Server/MySQL cell.",
        "Round-trips/writes include every EF command the Workbench process logged (runtime, design, identity). runtimeRoundTrips/runtimeWrites are elsa_runtime_* only.",
        "Transactions are unmeasured: SQLite EF logged no Database.Transaction Information lines (tx counters stay 0). Parameterized EF SQL does not include ContentJson.",
        "recoverySweepSeconds=0.2 is part of the G2 journey, so sweep/timer commands can land in waiting steps.",
        "No budget, target, speedup or throughput claim. Retired benchmark gates (B01–T04 / #2892) are not restored.",
        "Third-restart retention is already proved by the G2 classes and is not re-measured here."
    ];

    internal async Task<JsonNode> StepAsync(string name, Func<Task<JsonNode>> action)
    {
        host.Capture.Mark();
        var clock = Stopwatch.StartNew();
        var result = await action();
        clock.Stop();
        Steps.Add(new MeasuredStep(name, clock.ElapsedMilliseconds, host.Capture.Delta()));
        return result;
    }

    internal Task StepVoidAsync(string name, Func<Task> action) =>
        StepAsync(name, async () =>
        {
            await action();
            return new JsonObject();
        });

    internal PersistenceCounters Totals()
    {
        Assert.True(Steps.Any(step => step.Counters.RoundTrips > 0),
            "EF command logs were not captured. The G3 harness expects Information Microsoft.EntityFrameworkCore.Database.Command lines containing 'Executed DbCommand'.");
        return new PersistenceCounters(
            RoundTrips: Steps.Sum(step => step.Counters.RoundTrips),
            RuntimeRoundTrips: Steps.Sum(step => step.Counters.RuntimeRoundTrips),
            Writes: Steps.Sum(step => step.Counters.Writes),
            RuntimeWrites: Steps.Sum(step => step.Counters.RuntimeWrites),
            TransactionsStarted: Steps.Sum(step => step.Counters.TransactionsStarted),
            TransactionsCommitted: Steps.Sum(step => step.Counters.TransactionsCommitted),
            TransactionsRolledBack: Steps.Sum(step => step.Counters.TransactionsRolledBack),
            LeaseOps: Steps.Sum(step => step.Counters.LeaseOps),
            FenceOps: Steps.Sum(step => step.Counters.FenceOps),
            SchedulerCancelCommands: Steps.Sum(step => step.Counters.SchedulerCancelCommands),
            CancelActivityScopeCommands: Steps.Sum(step => step.Counters.CancelActivityScopeCommands),
            CancelEmbeddedMentions: Steps.Sum(step => step.Counters.CancelEmbeddedMentions),
            EfCommandDurationMs: Steps.Sum(step => step.Counters.EfCommandDurationMs));
    }

    internal object Receipt(string journey, object? extra = null)
    {
        var totals = Totals();
        return new
        {
            journey,
            environment = Environment(),
            steps = Steps.Select(step => new
            {
                step.Step,
                latencyMs = step.LatencyMs,
                roundTrips = step.Counters.RoundTrips,
                runtimeRoundTrips = step.Counters.RuntimeRoundTrips,
                writes = step.Counters.Writes,
                runtimeWrites = step.Counters.RuntimeWrites,
                transactionsStarted = step.Counters.TransactionsStarted,
                transactionsCommitted = step.Counters.TransactionsCommitted,
                transactionsRolledBack = step.Counters.TransactionsRolledBack,
                leaseOps = step.Counters.LeaseOps,
                fenceOps = step.Counters.FenceOps,
                efCommandDurationMs = Math.Round(step.Counters.EfCommandDurationMs, 1)
            }),
            totals = new
            {
                roundTrips = totals.RoundTrips,
                runtimeRoundTrips = totals.RuntimeRoundTrips,
                writes = totals.Writes,
                runtimeWrites = totals.RuntimeWrites,
                transactionsStarted = totals.TransactionsStarted,
                transactionsCommitted = totals.TransactionsCommitted,
                transactionsRolledBack = totals.TransactionsRolledBack,
                leaseOps = totals.LeaseOps,
                fenceOps = totals.FenceOps,
                efCommandDurationMs = Math.Round(totals.EfCommandDurationMs, 1)
            },
            extra,
            limits = Limits
        };
    }

    internal async Task WriteAsync(ITestOutputHelper output, string fileName, object receipt)
    {
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        output.WriteLine(json);
        var evidence = System.Environment.GetEnvironmentVariable("ELSA_G3_EVIDENCE_DIRECTORY");
        if (evidence is null) return;
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, fileName), json + System.Environment.NewLine);
    }
}
