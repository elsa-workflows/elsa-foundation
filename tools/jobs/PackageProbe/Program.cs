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

internal static partial class Probe
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
            ValidateApplicationClock(command);
            if (command.Fault is { Event: "before-transaction" } && command.Mode != "claim")
                throw new InvalidDataException("A pre-transaction barrier is only supported by the explicit direct claim probe.");
            using var faults = command.Fault is { } fault ? new ProbeEfFaults(fault) : null;
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
                options.TerminalInputRetention = command.TerminalInputRetention;
                options.TerminalResultRetention = command.TerminalResultRetention;
            });
            var worker = command.Mode == "worker";
            ConfigureApplicationClock(command, builder.Services);
            builder.Services.AddElsaJobs(options =>
            {
                options.EnableWorker = worker;
                options.WorkerScopes.Add(command.Scope);
                options.MaximumConcurrentAttempts = command.MaximumConcurrentAttempts;
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.LeaseDuration = TimeSpan.FromSeconds(command.LeaseSeconds);
                options.RenewalInterval = TimeSpan.FromMilliseconds(command.RenewalMilliseconds);
                options.ShutdownGracePeriod = TimeSpan.FromMilliseconds(command.ShutdownGraceMilliseconds);
                options.MaterializeSchedules = command.MaterializeSchedules;
                options.MaximumOccurrencesPerMaterialization = command.MaximumOccurrences;
                options.MaterializationInterval = TimeSpan.FromMilliseconds(100);
                options.MaterializationTimeout = TimeSpan.FromSeconds(5);
                if (command.Queues is { } queues)
                {
                    options.Queues.Clear();
                    foreach (var queue in queues) options.Queues.Add(queue);
                }
            });
            if (worker)
            {
                builder.Services.AddJobHandler<ReportHandler, ReportInput, ReportResult>(
                    command.UnrelatedHandler ? Support with { Handler = new("package.unrelated", "1") } : Support);
                if (!command.UnrelatedHandler)
                {
                    builder.Services.AddJobHandler<ControlledReportHandler, ControlledReportInput, ControlledReportResult>(ControlledSupport);
                    builder.Services.AddJobHandler<ReportHandler, ReportInput, ReportResult>(Support with { Handler = new("package.unrelated", "1") });
                }
            }
            using var host = builder.Build();
            await host.StartAsync(timeout.Token);
            faults?.ArmAfterHostValidation();
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
                    var request = Request(command);
                    var accepted = await services.GetRequiredService<IJobClient>().SubmitAsync(command.Scope,
                        new(command.ClientKey, request), timeout.Token);
                    // Deliberately discard the successful response before the controller can see JobId.
                    // This simulates a producer response loss, not a database commit-ack fault.
                    result = command.Mode == "submit-discard-ack" ? new { responseDiscarded = true } : accepted;
                    break;
                case "submit-batch":
                    if (command.BatchCount is < 1 or > 1500) throw new InvalidDataException("The qualification batch exceeds its bound.");
                    var confirmations = new List<JobAcceptance>();
                    for (var index = 0; index < command.BatchCount; index++)
                        confirmations.Add(await services.GetRequiredService<IJobClient>().SubmitAsync(command.Scope,
                            new(command.ClientKey + "-" + index, Request(command)), timeout.Token));
                    result = confirmations;
                    break;
                case "admit-associated":
                case "admit-origin":
                    var associated = Request(command);
                    if (command.Mode == "admit-associated" && associated.Association is null) throw new InvalidDataException("A trusted qualification association is required.");
                    var digest = JobRequestDigest.Compute(associated);
                    result = await commands.AdmitAsync(new(command.Scope, Guid.NewGuid().ToString("N"), command.ClientKey,
                        digest, associated, new("package-witness", "controlled-probe", command.ClientKey, digest)), timeout.Token);
                    break;
                case "inspect":
                    result = await queries.FindAsync(command.Scope, command.JobId, timeout.Token);
                    break;
                case "attempts":
                    result = await queries.ListAttemptsAsync(command.Scope, command.JobId, new(100, null), timeout.Token);
                    break;
                case "receipts":
                    result = await queries.ListPhysicalReceiptsAsync(command.Scope, command.JobId, new(100, null), timeout.Token);
                    break;
                case "receipt-inspections":
                    result = await queries.ListPhysicalReceiptInspectionsAsync(command.Scope, command.JobId, new(100, null), timeout.Token);
                    break;
                case "expire-payloads":
                    result = await commands.ExpirePayloadsAsync(new(command.Scope, command.MaximumJobs, command.ContinuationToken), timeout.Token);
                    break;
                case "list":
                    result = await queries.ListJobsAsync(command.Scope, new(new(100, null), null, null, null), timeout.Token);
                    break;
                case "pause":
                    result = await commands.PauseAsync(new(command.Scope, Gate(command),
                        command.HoldId, new(command.OperationId, "package-witness", "Verify durable start cutoff")), timeout.Token);
                    break;
                case "holds":
                    result = await queries.ListHoldsAsync(command.Scope, Gate(command), new(100, null), timeout.Token);
                    break;
                case "release":
                    result = await commands.ReleaseHoldAsync(new(command.Scope, Gate(command),
                        command.HoldId, command.ExpectedRevision,
                        new(command.OperationId, "package-witness", "Resume package witness")), timeout.Token);
                    break;
                case "capacity":
                    result = await commands.SetCapacityAsync(new(command.Scope, Gate(command), command.Capacity,
                        command.ExpectedRevision, new(command.OperationId, "package-witness", "Verify authorized lease capacity")), timeout.Token);
                    break;
                case "cancel":
                    result = await commands.CancelAsync(new(command.Scope, command.JobId,
                        new(command.OperationId, "package-witness", "Verify cooperative cancellation")), timeout.Token);
                    break;
                case "retry-as-new":
                    result = await commands.RetryAsNewRunAsync(new(command.Scope, command.JobId, command.ExpectedRevision,
                        Guid.NewGuid().ToString("N"), command.ClientKey,
                        new(command.OperationId, "package-witness", "Verify linked retry as new run")), timeout.Token);
                    break;
                case "save-schedule":
                    result = await commands.SaveScheduleAsync(new(command.Scope,
                        new(command.ScheduleId, Request(command), command.Recurrence ?? throw new InvalidDataException("Recurrence required."),
                            command.FirstOccurrence ?? throw new InvalidDataException("First occurrence required."), command.MissedPolicy),
                        command.ExpectedRevision, new(command.OperationId, "package-witness", "Verify recurring materialization")), timeout.Token);
                    break;
                case "schedule":
                    result = await queries.FindScheduleAsync(command.Scope, command.ScheduleId, timeout.Token);
                    break;
                case "schedules":
                    result = await queries.ListSchedulesAsync(command.Scope, new(100, null), timeout.Token);
                    break;
                case "schedule-enabled":
                    result = await commands.SetScheduleEnabledAsync(new(command.Scope, command.ScheduleId, command.Enabled,
                        command.ExpectedRevision, new(command.OperationId, "package-witness", "Verify retained schedule control")), timeout.Token);
                    break;
                case "materialize":
                    result = await commands.MaterializeDueAsync(new(command.Scope, command.MaximumOccurrences), timeout.Token);
                    break;
                case "claim":
                    result = await commands.ClaimAsync(new(command.Scope, "package-witness-lost-owner", ["default"],
                        [Support, ControlledSupport], TimeSpan.FromSeconds(command.LeaseSeconds), command.ClaimRequestId), timeout.Token);
                    break;
                case "complete-stale":
                    var lease = command.Lease ?? throw new InvalidDataException("The original lease is required.");
                    var payload = new JobPayload(Support.ResultSchema, JsonSerializer.SerializeToElement(new { stale = true }, Json));
                    result = await commands.CompleteAsync(lease,
                        new(command.ReceiptId, lease.AttemptId, lease.Owner, lease.Fence, JobAttemptOutcome.Succeeded,
                            payload, JobPayloadDigest.Compute(payload), null, false, DateTimeOffset.UtcNow), timeout.Token);
                    break;
                case "worker":
                    await WriteAsync(command.ReadyFile, ApplicationClockReady(command, services));
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
    JobAttemptLease? Lease = null, string ReceiptId = "", int TimeoutSeconds = 120,
    ControlledReportInput? ControlledInput = null, JobExecutionPolicy? Policy = null,
    JobAssociation? Association = null, string Queue = "default", DateTimeOffset? NotBefore = null,
    JobGateKey? Gate = null, int? Capacity = null, string ScheduleId = "", JobRecurrence? Recurrence = null,
    DateTimeOffset? FirstOccurrence = null, JobMissedOccurrencePolicy MissedPolicy = JobMissedOccurrencePolicy.CoalesceOne,
    bool Enabled = true, bool MaterializeSchedules = true, int MaximumOccurrences = 4,
    string[]? Queues = null, int MaximumConcurrentAttempts = 1, int LeaseSeconds = 3,
    int RenewalMilliseconds = 500, int ShutdownGraceMilliseconds = 250, ProbeFaultOptions? Fault = null,
    bool SubmitUnrelated = false, int BatchCount = 1, TimeSpan? TerminalInputRetention = null,
    TimeSpan? TerminalResultRetention = null, int MaximumJobs = 100, string? ContinuationToken = null,
    int ApplicationClockOffsetHours = 0);

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
