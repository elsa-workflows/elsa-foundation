using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Scheduling.Tests;
using Elsa.Activities.Testing;
using Elsa.Cluster.Readability;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.TimerRestartFixture;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

// Actual published Delay and owned SQLite state. No timer, bookmark, policy or journal rows are seeded.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    var line = new StringBuilder(); var buffer = new char[1];
    while (await Console.In.ReadAsync(buffer.AsMemory()) != 0 && buffer[0] != '\n')
    { if (line.Length >= 16_384) throw new InvalidDataException("Fixture command exceeds its bound."); line.Append(buffer[0]); }
    var request = JsonSerializer.Deserialize<TimerRestartRequest>(line.ToString(), json) ?? throw new InvalidDataException("Missing command.");
    if (request.Phase is not ("seed" or "fire" or "confirm") || !Enum.TryParse<PersistenceMode>(request.Mode, out var mode) ||
        mode is not (PersistenceMode.Durable or PersistenceMode.Balanced or PersistenceMode.Ephemeral))
        throw new InvalidDataException("Unsupported fixture phase or mode.");
    var directory = Path.GetFullPath(request.Directory);
    if (!Path.GetFileName(directory).StartsWith("elsa-timer-history-restart-", StringComparison.Ordinal) ||
        !Guid.TryParseExact(request.Nonce, "N", out _) || await File.ReadAllTextAsync(Path.Combine(directory, "owner.txt")) != request.Nonce)
        throw new InvalidDataException("The directory is not owned by this request.");
    var database = Path.Combine(directory, "runtime.sqlite");
    if (File.Exists(database) != (request.Phase != "seed")) throw new InvalidDataException("Unexpected database lifecycle.");
    const string run = "timer-history-restart-run";
    var clock = new FakeTimeProvider(WorkflowExecutionHarness.Timestamp + (request.Phase == "seed" ? TimeSpan.Zero : TimeSpan.FromMinutes(6)));
    var executable = DelayTestExecutable.Create(TimeSpan.FromMinutes(5), WorkflowExecutionHarness.Timestamp);
    await using var host = WorkflowExecutionHarness.Create()
        .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
        .WithFeature(services => new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
        .WithFeature(services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
        .ConfigureServices(services =>
        {
            services.AddLogging(); services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.AddEfSchemaReadability();
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False", Pooling = false,
                RecoveryContinuationSigningKey = "owned-timer-restart-recovery-key-at-least-32-bytes",
                HierarchyCursorSigningKey = "owned-timer-restart-hierarchy-key-at-least-32-bytes"
            });
            services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
        }).WithPersistenceMode(mode)
        .Build(executable.Identity, run, Enumerable.Range(1, 200).Select(index => $"timer-restart-{request.Phase}-{index}"));
    host.Services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
    foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
    host.InitializeActivityTypes();
    var context = host.Services.GetRequiredService<RuntimeDbContext>();
    if (context.Database.HasPendingModelChanges()) throw new InvalidDataException("Fixture model has pending changes.");
    var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    if (migrations.Length != 8) throw new InvalidDataException("The eight official Runtime migrations were not applied.");
    var gate = host.Services.GetRequiredService<EfSchemaFinalizationGates>().FindModuleGate(context.GetType())
        ?? throw new InvalidDataException("Missing actual schema gate.");
    var version = gate.StateOf(RuntimeOperationalStateEfModule.SchemaFamily)?.WriteVersion;
    if (!Equals(version, RuntimeOperationalStateEfModule.TimerHistorySchemaVersion)) throw new InvalidDataException("Timer format1.2 is unavailable.");
    var source = (IDurableTimerHistoryStore)host.Services.GetRequiredService<IDurableTimerStore>();
    var history = host.Services.GetRequiredService<IRuntimeHistoryStore>();
    var pump = host.Services.GetServices<IRecurringTask>().OfType<DurableTimerPumpTask>().Single();
    TimerRestartSnapshot? before = null;
    if (request.Phase == "seed")
    {
        var published = await host.PublishAsync(executable, "timer-history-restart-source");
        await host.StartPublishedAsync(published, run, tenantId: "default", partition: new("default"));
        await host.SweepUntilQuietAsync();
    }
    else
    {
        before = await Snapshot();
        await pump.ExecuteAsync(CancellationToken.None);
        await host.SweepUntilQuietAsync();
    }
    var after = await Snapshot();
    if (request.Phase != "seed")
    {
        await pump.ExecuteAsync(CancellationToken.None); await host.SweepUntilQuietAsync();
        var repeated = await Snapshot();
        if (!after.EventIds.SequenceEqual(repeated.EventIds)) throw new InvalidDataException("A duplicate pump changed retained history.");
        after = repeated;
    }
    using var process = Process.GetCurrentProcess(); var assembly = Assembly.GetExecutingAssembly();
    var reply = new TimerRestartReply("completed", process.Id, process.StartTime.ToUniversalTime().Ticks,
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location))),
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        host.Services.GetRequiredService<PersistenceModeOptions>().Mode.ToString(), migrations, version!.ToString()!, before, after);
    var output = JsonSerializer.Serialize(reply, json);
    if (output.Length > 65_536) throw new InvalidDataException("Fixture receipt exceeds its bound.");
    await Console.Out.WriteLineAsync(output);
    return 0;

    async Task<TimerRestartSnapshot> Snapshot()
    {
        var native = await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(run)
            ?? throw new InvalidDataException("Actual native state is missing.");
        if (native.HistoryCaptureEnabledAtAdmission != true || native.HistoryCapturePolicy is null ||
            !DurableTimerHistoryFacts.Kinds.All(kind => native.HistoryCapturePolicy.SupportedKinds.Contains(kind, StringComparer.Ordinal)))
            throw new InvalidDataException("The admitted policy did not include actual timer capture: " + JsonSerializer.Serialize(new { native.HistoryCaptureEnabledAtAdmission, native.HistoryCapturePolicy, timerOwnerSupported = source.SupportsHistoryResponsibility }, json));
        var activity = (await host.Services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new(run))).Items.Single();
        var bookmarks = (await host.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(run))).Items;
        var timer = (await source.ListPageAsync(new(run))).Items.SingleOrDefault();
        if (timer is not null && (timer.ActivityExecutionId != activity.InvocationId || timer.ExecutionScopeId != activity.ExecutionScopeId ||
            timer.HistoryResponsibility is null || !timer.HistoryResponsibility.Policy.IsEquivalentTo(native.HistoryCapturePolicy)))
            throw new InvalidDataException("The actual timer and native occurrence differ.");
        var page = await history.ReadPageAsync(new(run, activityExecutionId: activity.InvocationId, limit: 100));
        if (page.Head is null || page.NextContinuationToken is not null) throw new InvalidDataException("Missing or oversized actual history.");
        var scheduled = page.Items.SingleOrDefault(item => item.Name == "TimerScheduled");
        var owed = timer?.HistoryResponsibility?.PendingEvents.SingleOrDefault(item => item.Name == "TimerScheduled");
        var dispatched = page.Items.SingleOrDefault(item => item.Name == "TimerDispatched");
        if (scheduled is not null && scheduled.Authority != RuntimeHistoryAuthority.DeliveryObservation ||
            dispatched is not null && dispatched.Authority != RuntimeHistoryAuthority.DeliveryObservation)
            throw new InvalidDataException("Timer observations claimed native execution authority.");
        return new(native.Status.ToString(), activity.Status.ToString(), activity.InvocationId, bookmarks.Count,
            timer is not null, await source.HasPendingHistoryAsync(run), scheduled?.HistoryEventId ?? owed?.HistoryEventId,
            scheduled?.OccurredAt ?? owed?.OccurredAt, scheduled?.RecordedAt, dispatched?.HistoryEventId,
            dispatched?.Causation?.HistoryEventId, page.Head.LastSequence, page.Items.Select(item => item.Name).ToArray(),
            page.Items.Select(item => item.HistoryEventId).ToArray(), page.Head.CapturedFromStart, page.Head.HasGaps);
    }
}
catch (Exception error)
{
    var detail = error.ToString();
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { status = "error", error = detail[..Math.Min(detail.Length, 16_384)] }, json));
    return 1;
}
