using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Acme.TriggerFaults;

/// <summary>Observes real SQLite checkpoint transactions without replacing a persistence owner.</summary>
[ShellFeature("AcceptanceTriggerCheckpointCrash")]
public sealed class CheckpointCrashFeature : IShellFeature
{
    public string OwnerToken { get; set; } = "";
    public string SourceRevision { get; set; } = "";
    public string TriggerId { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string Cut { get; set; } = "";

    public void ConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(OwnerToken) || string.IsNullOrWhiteSpace(TriggerId) || string.IsNullOrWhiteSpace(SourceKey) ||
            SourceRevision.Length is not (40 or 64) || SourceRevision.Any(c => !char.IsAsciiHexDigit(c)) ||
            Cut is not ("BeforeInitialCheckpoint" or "AfterInitialCheckpointCommit"))
            throw new InvalidOperationException("An exact owned checkpoint crash identity is required.");
        services.AddSingleton(new CheckpointCrashSettings(OwnerToken, SourceRevision,
            RuntimeStandaloneIngressSource.BuildOutboxItemId(new PersistenceScope("default"), "default", TriggerId, "Http", SourceKey), Cut));
        services.AddSingleton<CheckpointCrashObservation>();
        services.ConfigureDbContext<RuntimeSqliteDbContext>((provider, builder) =>
        {
            var observation = provider.GetRequiredService<CheckpointCrashObservation>();
            builder.AddInterceptors(new CheckpointSaveObserver(observation), new CheckpointTransactionObserver(observation));
        });
    }
}

internal sealed record CheckpointCrashSettings(string OwnerToken, string SourceRevision, string SourceOutboxItemId, string Cut);

internal sealed class CheckpointSaveObserver(CheckpointCrashObservation observation) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (data.Context is RuntimeSqliteDbContext context)
        {
            try { await observation.BeforeSaveAsync(context, cancellationToken); }
            catch (Exception exception) { observation.RecordFailure(exception); throw; }
        }
        return result;
    }
}

internal sealed class CheckpointTransactionObserver(CheckpointCrashObservation observation) : DbTransactionInterceptor
{
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
        CancellationToken cancellationToken = default) => observation.AfterCommitAsync(data.TransactionId, cancellationToken);
}

internal sealed class CheckpointCrashObservation(CheckpointCrashSettings options, IHostEnvironment environment, ILogger<CheckpointCrashObservation> logger)
{
    private readonly ConcurrentDictionary<Guid, CheckpointCut> pending = new();
    private readonly string bootId = Guid.NewGuid().ToString("N");
    private int selected;
    private int observations;

    public void RecordFailure(Exception exception) => logger.LogError(exception, "Owned checkpoint crash witness failed before its gate");

    public async Task BeforeSaveAsync(RuntimeSqliteDbContext context, CancellationToken ct)
    {
        var transaction = context.Database.CurrentTransaction;
        foreach (var entry in context.ChangeTracker.Entries<WorkflowExecutionStateEntity>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (Interlocked.Increment(ref observations) > 32) break;
            using var body = JsonDocument.Parse(entry.Entity.ContentJson);
            var actual = body.RootElement.TryGetProperty("ingressOrigin", out var origin) && origin.ValueKind == JsonValueKind.Object
                ? Decode(origin, "sourceOutboxItemId") : null;
            logger.LogInformation("Owned checkpoint witness observed state {RowState}, transaction {HasTransaction}, origin {ActualOrigin}, expected {ExpectedOrigin}",
                entry.State, transaction is not null, actual, options.SourceOutboxItemId);
        }
        if (transaction is null) return;
        if (pending.TryGetValue(transaction.TransactionId, out var staged))
        {
            var marker = context.ChangeTracker.Entries<RuntimeCheckpointCommitEntity>().SingleOrDefault(e =>
                e.State == EntityState.Added && EfRelationalIdentity.Decode(e.Entity.WorkflowExecutionId) == staged.WorkflowExecutionId);
            if (marker is not null) pending[transaction.TransactionId] = staged with { CommitId = EfRelationalIdentity.Decode(marker.Entity.CommitId) };
        }
        foreach (var entry in context.ChangeTracker.Entries<WorkflowExecutionStateEntity>().Where(e => e.State == EntityState.Added))
        {
            using var document = JsonDocument.Parse(entry.Entity.ContentJson);
            if (!document.RootElement.TryGetProperty("ingressOrigin", out var origin) || origin.ValueKind != JsonValueKind.Object ||
                Decode(origin, "sourceOutboxItemId") != options.SourceOutboxItemId) continue;
            if (Interlocked.CompareExchange(ref selected, 1, 0) != 0) return;
            var row = entry.Entity;
            // The checkpoint transaction has already conditionally deleted its consumed command.
            // A separate read-only connection observes the last committed queue, outside that transaction.
            var queued = await ReadCommittedQueueAsync(context, row, ct);
            var commits = await context.RuntimeCheckpointCommits.AsNoTracking().CountAsync(item =>
                item.ScopeKeyHash == row.ScopeKeyHash && item.WorkflowExecutionIdHash == row.WorkflowExecutionIdHash, ct);
            var persisted = await context.WorkflowExecutionStates.AsNoTracking().AnyAsync(item => item.Id == row.Id, ct);
            if (persisted || commits != 0 || queued.Length == 0)
                throw new InvalidOperationException("The crash witness requires a durable native queue before its first checkpoint.");
            if (!queued.Any(item => item.CommandKind == "Checkpoint"))
                throw new InvalidOperationException("The durable initial checkpoint command must have entered the native queue.");
            var snapshot = new CheckpointCut(EfRelationalIdentity.Decode(row.WorkflowExecutionId),
                new OriginCut(Decode(origin, "sourceOutboxItemId"), Decode(origin, "recipientId"), Decode(origin, "sourceFingerprint"),
                    Decode(origin, "selectionFingerprint")), queued, null);
            if (options.Cut == "BeforeInitialCheckpoint") await BlockAsync(snapshot, committed: false, ct);
            else pending[transaction.TransactionId] = snapshot;
        }
    }

    public async Task AfterCommitAsync(Guid transactionId, CancellationToken ct)
    {
        if (pending.TryRemove(transactionId, out var snapshot))
        {
            if (snapshot.CommitId is null) throw new InvalidOperationException("The committed checkpoint must contain its actual immutable commit marker.");
            await BlockAsync(snapshot, committed: true, ct);
        }
    }

    private static async Task<QueueCut[]> ReadCommittedQueueAsync(RuntimeSqliteDbContext context, WorkflowExecutionStateEntity state, CancellationToken ct)
    {
        var selected = context.Database.GetDbConnection();
        var settings = new DbConnectionStringBuilder { ConnectionString = selected.ConnectionString };
        settings["Mode"] = "ReadOnly"; settings["Cache"] = "Private";
        var factory = DbProviderFactories.GetFactory(selected)
            ?? throw new InvalidOperationException("The selected SQLite connection does not expose its public provider factory.");
        await using var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException("The selected SQLite provider cannot create a read-only observation connection.");
        connection.ConnectionString = settings.ConnectionString;
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT WorkItemId, ContentJson, ClaimToken, ClaimOwnerId, VisibleAfterUtcTicks FROM elsa_runtime_scheduler_work_item WHERE ScopeKeyHash = $scope AND WorkflowExecutionIdHash = $workflow";
        var scope = command.CreateParameter(); scope.ParameterName = "$scope"; scope.Value = state.ScopeKeyHash;
        var workflow = command.CreateParameter(); workflow.ParameterName = "$workflow"; workflow.Value = state.WorkflowExecutionIdHash;
        command.Parameters.Add(scope); command.Parameters.Add(workflow);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var queue = new List<QueueCut>();
        while (await reader.ReadAsync(ct))
        {
            using var body = JsonDocument.Parse(reader.GetString(1));
            queue.Add(new QueueCut(EfRelationalIdentity.Decode(reader.GetString(0)), body.RootElement.GetProperty("commandKind").GetString()!,
                reader.GetInt64(2), reader.IsDBNull(3) ? null : EfRelationalIdentity.Decode(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        }
        return queue.ToArray();
    }

    private async Task BlockAsync(CheckpointCut snapshot, bool committed, CancellationToken ct)
    {
        var root = Path.GetFullPath(environment.ContentRootPath);
        var ownership = Path.Combine(root, ".ux-acceptance-owner.json");
        if (!File.Exists(ownership) || new FileInfo(ownership).LinkTarget is not null)
            throw new InvalidOperationException("Missing regular owned-host marker.");
        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(ownership, ct)))
            if (document.RootElement.GetProperty("ownerToken").GetString() != options.OwnerToken ||
                !document.RootElement.GetProperty("checkpointFixture").GetBoolean())
                throw new InvalidOperationException("Foreign checkpoint crash fixture.");
        var directory = Path.Combine(root, "checkpoint-crash");
        if (Directory.Exists(directory) && new DirectoryInfo(directory).LinkTarget is not null)
            throw new InvalidOperationException("The crash witness cannot traverse a symbolic directory.");
        Directory.CreateDirectory(directory);
        var marker = new { processId = Environment.ProcessId, bootId, options.SourceRevision, options.SourceOutboxItemId,
            options.Cut, snapshot.WorkflowExecutionId, snapshot.Origin, snapshot.CommitId, durableQueueBeforeCheckpoint = snapshot.Queue,
            checkpointTransactionCommitted = committed, acknowledgementReturned = false, recordedAt = DateTimeOffset.UtcNow };
        var path = Path.Combine(directory, "cut.json"); var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ct);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(180)) await Task.Delay(200, ct);
        throw new TimeoutException("The owned checkpoint crash witness was not terminated within its bounded wait.");
    }

    private static string Decode(JsonElement value, string name) => EfRelationalIdentity.Decode(value.GetProperty(name).GetString()!);
    private sealed record QueueCut(string WorkItemId, string CommandKind, long ClaimToken, string? ClaimOwnerId, long? VisibleAfterUtcTicks);
    private sealed record OriginCut(string SourceOutboxItemId, string RecipientId, string SourceFingerprint, string SelectionFingerprint);
    private sealed record CheckpointCut(string WorkflowExecutionId, OriginCut Origin, QueueCut[] Queue, string? CommitId);
}
