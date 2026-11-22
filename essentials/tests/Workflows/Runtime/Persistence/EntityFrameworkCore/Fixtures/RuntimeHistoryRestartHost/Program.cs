using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RestartFixture;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// An actual runtime consumer, not a history writer substitute. The only input is a bounded
// command over a parent-owned directory; workflow callbacks create all native/history state.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    var line = new StringBuilder(); var buffer = new char[1];
    while (await Console.In.ReadAsync(buffer.AsMemory()) != 0 && buffer[0] != '\n')
    { if (line.Length >= 16_384) throw new InvalidDataException("Fixture command exceeds its bound."); line.Append(buffer[0]); }
    var request = JsonSerializer.Deserialize<RestartRequest>(line.ToString(), json) ?? throw new InvalidDataException("Missing fixture command.");
    if (request.Operation is not ("seed" or "resume" or "purge" or "confirm") ||
        !Enum.TryParse<PersistenceMode>(request.Mode, out var mode) || mode is not (PersistenceMode.Durable or PersistenceMode.Balanced))
        throw new InvalidDataException("Unsupported fixture operation or mode.");
    var directory = Path.GetFullPath(request.Directory);
    if (!Path.GetFileName(directory).StartsWith("elsa-history-restart-", StringComparison.Ordinal) ||
        !Guid.TryParseExact(request.Nonce, "N", out _) ||
        await File.ReadAllTextAsync(Path.Combine(directory, "owner.txt")) != request.Nonce)
        throw new InvalidDataException("The fixture directory is not owned by this request.");
    var database = Path.Combine(directory, "runtime.sqlite");
    if (File.Exists(database) == (request.Operation == "seed")) throw new InvalidDataException("Unexpected fixture database lifecycle.");
    const string run = "history-restart-run";
    var executable = RuntimeEventExecutableTestFixture.Create("history-restart");
    await using var host = WorkflowExecutionHarness.Create()
        .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
        .WithFeature(services => new SerializationFeature().ConfigureServices(services))
        .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
        .WithFeature(services => new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
        .ConfigureServices(services =>
        {
            services.AddLogging(); services.AddWorkflowRuntimeHistory();
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False", Pooling = false,
                RecoveryContinuationSigningKey = "owned-history-restart-recovery-signing-key",
                HierarchyCursorSigningKey = "owned-history-restart-hierarchy-signing-key"
            });
            services.AddWorkflowStimulusRouter(); services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
        }).WithPersistenceMode(mode)
        .Build(executable.Identity, run, Enumerable.Range(1, 100).Select(index => $"restart-{request.Operation}-{index}"));
    host.Services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
    foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
    host.InitializeActivityTypes();
    var context = host.Services.GetRequiredService<RuntimeDbContext>();
    if (context.Database.HasPendingModelChanges()) throw new InvalidDataException("Fixture model has pending changes.");
    var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    if (migrations.Length != 7) throw new InvalidDataException("Fixture did not apply the seven official Runtime migrations.");
    var reader = host.Services.GetRequiredService<IRuntimeHistoryStore>();
    var occurrences = (IRuntimeHistoryOccurrenceStore)reader;
    var writer = host.Services.GetRequiredService<IRuntimeCheckpointCommitStore>();
    if (!writer.SupportsHistory || !writer.CanCommitHistory(reader)) throw new InvalidDataException("The real history pair is unsupported.");
    HistorySnapshot? before = null; HistorySnapshot? replayed = null;
    if (request.Operation == "seed")
    {
        var source = await host.PublishAsync(executable, "history-restart-source");
        await host.StartPublishedAsync(source, run, tenantId: "default", partition: new("default"));
        await host.SweepUntilQuietAsync();
    }
    else
    {
        before = await Snapshot(request.SequenceToken);
        if (request.Operation == "resume")
        {
            var wait = (await host.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(run))).Items.Single();
            var result = await host.Services.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new(run,
                wait.StimulusType, wait.StimulusHash, JsonSerializer.SerializeToElement(new { eventName = "history-restart-ready" }),
                "history-restart-delivery", "history-restart-fixture", null, null, null, wait.BookmarkId, null));
            if (result.Status != BookmarkResumeDispatchStatus.Dispatched)
                throw new InvalidDataException($"Actual resume was not dispatched: {result.Status}, {result.Reason}.");
            await host.SweepUntilQuietAsync();
        }
        else if (request.Operation == "purge")
        {
            var source = await host.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync("history-restart-source")
                ?? throw new InvalidDataException("The real publication source did not survive restart.");
            await host.StartPublishedAsync(source, run, tenantId: "default", partition: new("default"));
            await host.SweepUntilQuietAsync();
            replayed = await Snapshot(request.SequenceToken);
            await host.Services.GetRequiredService<IRuntimeHistoryRetentionStore>().PurgeThroughAsync(run, replayed.LastSequence);
        }
    }
    var after = await Snapshot(request.SequenceToken);
    using var process = Process.GetCurrentProcess();
    var assembly = Assembly.GetExecutingAssembly();
    var reply = new RestartReply("completed", process.Id, process.StartTime.ToUniversalTime().Ticks,
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly.Location))),
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        host.Services.GetRequiredService<PersistenceModeOptions>().Mode.ToString(), migrations, before, after, replayed);
    var output = JsonSerializer.Serialize(reply, json);
    if (output.Length > 65_536) throw new InvalidDataException("Fixture receipt exceeds its bound.");
    await Console.Out.WriteLineAsync(output);

    async Task<HistorySnapshot> Snapshot(string? token)
    {
        var state = await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(run)
            ?? throw new InvalidDataException("The actual workflow state is missing.");
        var activities = await host.Services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new(run));
        var activity = activities.Items.Single();
        var bookmarks = await host.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(run));
        var page = await occurrences.ReadOccurrencesAsync(new(run, sequenceToken: token, accessBinding: "history-restart-context"));
        var head = await reader.FindHeadAsync(run) ?? throw new InvalidDataException("Actual captured history is missing.");
        var events = await reader.ReadPageAsync(new(run, limit: 100, accessBinding: "history-restart-context"));
        if (events.NextContinuationToken is not null) throw new InvalidDataException("The one-activity fixture exceeded its bounded history page.");
        var native = events.Items.Where(item => item.Authority == RuntimeHistoryAuthority.CommittedTransition).ToArray();
        if (native.Any(item => item.PhysicalCommitId is null)) throw new InvalidDataException("Committed evidence omitted its physical authority.");
        var groups = native.Where(item => item.CheckpointId is not null).Select(item => new CheckpointGroup(item.CheckpointId!, item.PhysicalCommitId!))
            .Distinct().OrderBy(item => item.Logical, StringComparer.Ordinal).ThenBy(item => item.Physical, StringComparer.Ordinal).ToArray();
        var first = page.Items.SingleOrDefault();
        if (first is not null && first.Observation.ActivityExecutionId != activity.InvocationId)
            throw new InvalidDataException("The recorded occurrence does not match the actual activity.");
        var opened = context.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync();
        long markers;
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM elsa_runtime_checkpoint_commit";
            markers = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
        return new(state.Status.ToString(), activity.Status.ToString(), bookmarks.Items.Count,
            page.Availability.ToString(), page.SequenceToken, page.UpperSequence, page.TotalCount, first?.Position,
            first?.Observation.ActivityExecutionId, first?.Observation.HistoryEventId, first?.Observation.Sequence,
            first?.Observation.PhysicalCommitId, first?.Observation.RecordedAt, first is null ? null : Hash(first.Observation),
            head.LastSequence, head.PurgedThroughSequence, markers, Hash(native), groups);
    }
    string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, json)));
    return 0;
}
catch (Exception error)
{
    var detail = error.ToString();
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { status = "error", error = detail[..Math.Min(detail.Length, 16_384)] }, json));
    return 1;
}
