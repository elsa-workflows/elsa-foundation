using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Jobs;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// This file is copied into a clean, external PackageReference console application by the
// committed packaging driver. It must not reference a source project or a test double.
return await Probe.RunAsync(args);

internal static class Probe
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, WriteIndented = true
    };
    internal static readonly JobHandlerSupport Support = new(new("package.report", "1"),
        new("package.report.input", 1), new("package.report.result", 1), "package.report.implementation.v1");

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass one owned JSON command file.");
        var command = JsonSerializer.Deserialize<ProbeCommand>(await File.ReadAllTextAsync(args[0]), Json)
            ?? throw new InvalidDataException("The probe command is missing.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = [], ContentRootPath = AppContext.BaseDirectory, DisableDefaults = true
            });
            builder.Logging.AddSimpleConsole(options => options.SingleLine = true).SetMinimumLevel(LogLevel.Warning);
            builder.Services.Configure<EfMigrateOptions>(options =>
                options.Policy = command.Mode == "initialize" ? EfMigratePolicy.AutoMigrate : EfMigratePolicy.Validate);
            builder.Services.AddJobsEntityFrameworkCore(options =>
            {
                options.Provider = command.Provider;
                options.ConnectionString = command.ConnectionString;
                options.Schema = command.Schema;
            });
            var worker = command.Mode == "worker";
            builder.Services.AddElsaJobs(options =>
            {
                options.EnableWorker = worker;
                options.WorkerScopes.Add(command.Scope);
                options.MaximumConcurrentAttempts = 1;
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.LeaseDuration = TimeSpan.FromSeconds(3);
                options.RenewalInterval = TimeSpan.FromMilliseconds(500);
                options.ShutdownGracePeriod = TimeSpan.FromMilliseconds(250);
            });
            if (worker)
                builder.Services.AddJobHandler<ReportHandler, ReportInput, ReportResult>(
                    command.UnrelatedHandler ? Support with { Handler = new("package.unrelated", "1") } : Support);
            using var host = builder.Build();
            await host.StartAsync(timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var commands = services.GetRequiredService<IJobCommands>();
            var queries = services.GetRequiredService<IJobQueries>();
            object? result;
            switch (command.Mode)
            {
                case "initialize": result = new { initialized = true }; break;
                case "provider-version":
                    var connectionType = Type.GetType(command.Provider == "Sqlite"
                        ? "Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite"
                        : "Npgsql.NpgsqlConnection, Npgsql", throwOnError: true)!;
                    await using (var connection = (DbConnection)Activator.CreateInstance(connectionType, command.ConnectionString)!)
                    {
                        await connection.OpenAsync(timeout.Token);
                        await using var versionCommand = connection.CreateCommand();
                        versionCommand.CommandText = command.Provider == "Sqlite" ? "SELECT sqlite_version()" : "SELECT version()";
                        result = new { provider = command.Provider, version = (string)(await versionCommand.ExecuteScalarAsync(timeout.Token))! };
                    }
                    break;
                case "submit":
                case "submit-discard-ack":
                    var input = command.Input ?? throw new InvalidDataException("Report input required.");
                    var request = new JobRequest(Support.Handler,
                        new(Support.InputSchema, JsonSerializer.SerializeToElement(input, Json)), Support.ResultSchema,
                        "default", new(new(3, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(1), 2, 0, true),
                            TimeSpan.FromMinutes(2), TimeSpan.FromDays(30)), null, null);
                    var accepted = await services.GetRequiredService<IJobClient>().SubmitAsync(command.Scope,
                        new(command.ClientKey, request), timeout.Token);
                    // Deliberately discard the successful response before the controller can see JobId.
                    // This simulates a producer response loss, not a database commit-ack fault.
                    result = command.Mode == "submit-discard-ack" ? new { responseDiscarded = true } : accepted;
                    break;
                case "inspect":
                    result = await queries.FindAsync(command.Scope, command.JobId, timeout.Token);
                    break;
                case "attempts":
                    result = await queries.ListAttemptsAsync(command.Scope, command.JobId, new(100, null), timeout.Token);
                    break;
                case "list":
                    result = await queries.ListJobsAsync(command.Scope, new(new(100, null), null, null, null), timeout.Token);
                    break;
                case "pause":
                    result = await commands.PauseAsync(new(command.Scope, new(JobGateKind.Queue, "default"),
                        command.HoldId, new(command.OperationId, "package-witness", "Verify durable start cutoff")), timeout.Token);
                    break;
                case "holds":
                    result = await queries.ListHoldsAsync(command.Scope, new(JobGateKind.Queue, "default"), new(100, null), timeout.Token);
                    break;
                case "release":
                    result = await commands.ReleaseHoldAsync(new(command.Scope, new(JobGateKind.Queue, "default"),
                        command.HoldId, command.ExpectedRevision,
                        new(command.OperationId, "package-witness", "Resume package witness")), timeout.Token);
                    break;
                case "claim":
                    result = await commands.ClaimAsync(new(command.Scope, "package-witness-lost-owner", ["default"],
                        [Support], TimeSpan.FromSeconds(1), command.ClaimRequestId), timeout.Token);
                    break;
                case "complete-stale":
                    var lease = command.Lease ?? throw new InvalidDataException("The original lease is required.");
                    var payload = new JobPayload(Support.ResultSchema, JsonSerializer.SerializeToElement(new { stale = true }, Json));
                    result = await commands.CompleteAsync(lease,
                        new(command.ReceiptId, lease.AttemptId, lease.Owner, lease.Fence, JobAttemptOutcome.Succeeded,
                            payload, JobPayloadDigest.Compute(payload), null, false, DateTimeOffset.UtcNow), timeout.Token);
                    break;
                case "worker":
                    await WriteAsync(command.ReadyFile, new { ready = true, processId = Environment.ProcessId });
                    while (!File.Exists(command.StopFile)) await Task.Delay(25, timeout.Token);
                    result = new { stopped = true };
                    break;
                default: throw new ArgumentException("Unknown package probe mode.");
            }
            await WriteAsync(command.OutputFile, new { ok = true, value = result });
            await host.StopAsync(timeout.Token);
            return 0;
        }
        catch (JobSubmissionConflictException)
        {
            await WriteAsync(command.OutputFile, new { ok = false, code = "jobs.submission.conflict" });
            return 0;
        }
        catch (Exception error)
        {
            // A local witness receipt never dumps input, connection strings or raw provider errors.
            await WriteAsync(command.OutputFile, new { ok = false, code = error is JobException job ? job.Code : error.GetType().Name });
            return 1;
        }
    }

    internal static async Task WriteAsync<T>(string path, T value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Json));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record ProbeCommand(string Mode, string Provider, string ConnectionString, string? Schema,
    JobScope Scope, string OutputFile, string JobId = "", string ClientKey = "", ReportInput? Input = null,
    bool UnrelatedHandler = false, string ReadyFile = "", string StopFile = "", string HoldId = "",
    string OperationId = "", long ExpectedRevision = 0, string ClaimRequestId = "",
    JobAttemptLease? Lease = null, string ReceiptId = "", int TimeoutSeconds = 120);

internal sealed record ReportLine(string Product, int Quantity, decimal UnitPrice);
internal sealed record ReportInput(string Name, ReportLine[] Lines, string EntryDirectory, string? ReleaseFile = null);
internal sealed record ReportResult(string Name, int Lines, int Units, decimal Subtotal, string InvocationKey);

internal sealed class ReportHandler : IJobHandler<ReportInput, ReportResult>
{
    public ReportHandler() { }

    public async ValueTask<ReportResult> ExecuteAsync(ReportInput input, JobExecutionContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(input.EntryDirectory);
        await Probe.WriteAsync(Path.Combine(input.EntryDirectory, context.AttemptId + ".json"),
            new { context.JobId, context.AttemptId, context.AttemptNumber, context.IdempotencyKey });
        await context.Progress.ReportAsync(new(0.5, "Calculating report totals", null), cancellationToken);
        while (input.ReleaseFile is not null && !File.Exists(input.ReleaseFile))
            await Task.Delay(25, cancellationToken);
        return new(input.Name, input.Lines.Length, input.Lines.Sum(line => line.Quantity),
            input.Lines.Sum(line => line.Quantity * line.UnitPrice), context.IdempotencyKey);
    }
}
