using System.Text.Json;
using System.Security.Cryptography;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// This command reads only a private coherent SQLite snapshot supplied by the e2e preflight wrapper.
// The public provider reader owns schema, identity projections and delivery-state validation.
const string publishKind = "Elsa.Activities.Primitives.PublishStimulus";
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args.Length != 2 || !File.Exists(args[0]))
        throw new ArgumentException("A private SQLite snapshot and its expected SHA256 are required.");
    await VerifySnapshotAsync(args[0], args[1]);
    var source = new Uri(Path.GetFullPath(args[0])).AbsoluteUri + "?immutable=1";
    var connectionString = new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly }.ToString();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA query_only=ON";
        await command.ExecuteNonQueryAsync();
    }
    await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
    var publishes = new List<object>();
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    var sourceRows = 0;
    var offset = 0;
    var blocked = false;
    while (true)
    {
        // Offset paging on this immutable private snapshot includes invalid empty/NULL IDs. A lower-bound
        // cursor could hide those rows before the checked reader gets a chance to refuse them.
        var rows = await context.RuntimePostCommitOutbox.AsNoTracking().OrderBy(row => row.Id).Skip(offset).Take(256).ToArrayAsync();
        if (rows.Length == 0) break;
        foreach (var row in rows)
        {
            sourceRows++;
            ArgumentException.ThrowIfNullOrWhiteSpace(row.Id);
            using var wire = JsonDocument.Parse(row.ContentJson);
            if (row.SchemaVersion == "1.0.0" && wire.RootElement.TryGetProperty("recipientSnapshot", out var cohort) &&
                cohort.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("A cohort cannot be protected by the legacy outbox family.");
            var scope = EfRelationalIdentity.Decode(row.ScopeKey);
            var identity = EfRelationalIdentity.Decode(row.OutboxItemId);
            var store = new EfRuntimePostCommitOutboxStore(context, new ScopedAccess(new PersistenceScope(scope)));
            var item = await store.FindAsync(identity) ?? throw new InvalidDataException("The enumerated source cannot be reloaded by its checked identity.");
            var physicalId = RuntimePostCommitOutboxIdentity.CreateProjectionValue(item.OutboxItemId);
            if (row.Id != EfRelationalIdentity.HashLengthFramed(scope, physicalId))
                throw new InvalidDataException("The enumerated physical source differs from the checked source.");
            if (item.Intent.Kind != publishKind) continue;
            var classification = Classify(item);
            counts[classification] = counts.GetValueOrDefault(classification) + 1;
            blocked |= classification is "LegacyRecoveryRequired" or "LegacyScopeOnlyRecoveryRequired" or "ProtocolRecoveryRequired";
            publishes.Add(new
            {
                SourceIdentitySha256 = EfRelationalIdentity.Hash(item.OutboxItemId).ToLowerInvariant(),
                ScopeIdentitySha256 = EfRelationalIdentity.Hash(scope).ToLowerInvariant(),
                FamilyVersion = row.SchemaVersion,
                Status = item.Status.ToString(),
                item.DeliveryAttemptCount,
                item.DeliveryFencingToken,
                FirstAttemptRecorded = RuntimePostCommitOutboxClaimTransitions.ReadFirstDeliveryAttemptedAt(item) is not null,
                Classification = classification
            });
        }
        offset += rows.Length;
    }
    await VerifySnapshotAsync(args[0], args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        OutboxRows = sourceRows,
        PublishRows = publishes.Count,
        IdentityDigestEncoding = "UTF-16LE code units",
        Classifications = counts,
        Status = blocked ? "RecoveryRequired" : "NoLegacyPublishResponsibilityOwed",
        ProviderChecked = true,
        DeliveryActivationQualified = false,
        Publishes = publishes
    }, json));
    return blocked ? 2 : 0;
}
catch (Exception exception)
{
    // Provider messages may contain retained source content; only the type enters the portable diagnostic.
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Status = "InventoryUnavailable",
        FailureType = exception.GetType().Name,
        Reason = "The supplied store could not be classified by the runtime provider; responsibility is unresolved.",
        ProviderChecked = false
    }, json));
    return 2;
}

// Checked format classification only. Accepted protocol data can be inventoried without claiming that
// its recipients have run or that writers/GC are quiescent. Delivered without an accepted snapshot cannot
// establish that a declared PublishStimulus followed the required first-selection protocol.
static string Classify(RuntimePostCommitOutboxItem item)
{
    if (item.StimulusDeliveryProtocolVersion is null)
    {
        if (item.StimulusDeliverySourceScope is not null)
            return item.Status == RuntimePostCommitOutboxStatus.Delivered ? "LegacyScopeOnlyDelivered" : "LegacyScopeOnlyRecoveryRequired";
        return item.Status == RuntimePostCommitOutboxStatus.Delivered ? "LegacyDelivered" : "LegacyRecoveryRequired";
    }
    if (item.RecipientSnapshot is not { } snapshot)
        return item.Status == RuntimePostCommitOutboxStatus.Delivered ? "ProtocolRecoveryRequired" : "ProtocolAwaitingSelection";
    if (snapshot.RetentionAcceptanceVersion != StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion)
        return "ProtocolRecoveryRequired";
    return item.Status == RuntimePostCommitOutboxStatus.Delivered ? "ProtocolDelivered" : "ProtocolCohortRecorded";
}

static async Task VerifySnapshotAsync(string path, string expectedSha256)
{
    if (File.Exists(path + "-wal") || File.Exists(path + "-shm"))
        throw new InvalidDataException("A private closed snapshot without live WAL sidecars is required.");
    await using var stream = File.OpenRead(path);
    var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    if (!StringComparer.Ordinal.Equals(actual, expectedSha256))
        throw new InvalidDataException("The private snapshot changed or differs from its expected hash.");
}

internal sealed class ScopedAccess(PersistenceScope scope) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope);
}
