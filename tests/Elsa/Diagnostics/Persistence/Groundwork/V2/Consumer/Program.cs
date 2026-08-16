using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;

var databasePath = Path.Combine(Path.GetTempPath(), "elsa-groundwork-e2-v2-" + Guid.NewGuid().ToString("N") + ".db");

try
{
    using var connection = new SqliteProviderFactory().Create("Data Source=" + databasePath);
    RunOrdinaryManifestJourney(connection);
    RunProviderSequenceBoundaryProof(connection);
    RunRetentionBoundaryProof();
    RunScopeContractProof(connection);
    Console.WriteLine("E2 v2 SQLite diagnostics consumer passed.");
}
finally
{
    if (File.Exists(databasePath))
        File.Delete(databasePath);
}

static void RunOrdinaryManifestJourney(IStorageProviderConnection connection)
{
    var unit = OrdinaryLogUnit("e2_structured_logs");
    var applied = connection.Schema.Apply(unit);
    Require(applied.Applied, "The ordinary structured-log manifest did not apply.");
    Require(connection.Schema.Diff(unit).IsEmpty, "The applied ordinary manifest was not a schema no-op on verification.");

    var session = connection.OpenSession(unit, StorageAccess.Global);
    var values = LogValues("entry-1", "ordinary payload", "worker");
    var operation = new OperationId(DateTimeOffset.UtcNow, "structured-log-operation-1");

    var inserted = session.Append(operation, values);
    Require(inserted.Status == WriteOutcomeStatus.Inserted, "The ordinary append did not insert the structured-log row.");

    var replayed = session.Append(operation, values);
    Require(replayed.Status == WriteOutcomeStatus.Replayed, "The repeated append did not replay the durable operation.");

    var stored = session.Read(new StorageKey(new Dictionary<string, object?> { ["entry_id"] = "entry-1" }));
    Require(stored is not null, "The appended structured-log row could not be read by its key.");
    Require(stored!.Values.Values["payload"] is "ordinary payload", "The appended payload did not round-trip through the public Store API.");

    var category = new ColumnRef(new TableId(unit.Name), "category", QueryType.String, isNullable: false, maxLength: 200);
    var query = new QueryRequest(
        new TableId(unit.Name),
        new Predicate.Equal(category, QueryConstant.Of(category, "worker")),
        [],
        Projection.All,
        Paging.None);
    var rows = session.Query(query).Rows;
    Require(rows.Count == 1, "The ordinary structured-log query did not return exactly one category match.");
    Require(rows[0]["payload"] is "ordinary payload", "The ordinary structured-log query returned the wrong payload.");

    Console.WriteLine("[GREEN] ordinary manifest/schema + append replay + query/payload");
}

static void RunProviderSequenceBoundaryProof(IStorageProviderConnection connection)
{
    var invalidShape = AssertDeclarationRefusal(
        () => StorageUnit.Declare("e2_invalid_sequence", "e2_invalid_sequence")
            .Int64("sequence", column => column.Required().ProviderSequence())
            .String("stream", column => column.Required())
            .String("payload", 4096, column => column.Required())
            .Key("sequence", "stream")
            .Build(),
        "GW-PORT-005");
    Console.WriteLine($"[RED] ProviderSequence composite-key shape refused: {invalidShape.Message}");

    var unit = StorageUnit.Declare("e2_provider_sequence", "e2_provider_sequence")
        .Int64("sequence", column => column.Required().ProviderSequence())
        .String("payload", 4096, column => column.Required())
        .Key("sequence")
        .AppendIdempotency(TimeSpan.FromHours(1), "e2_sequence_operations")
        .Build();
    Require(connection.Schema.Apply(unit).Applied, "The valid ProviderSequence manifest did not apply.");

    var session = connection.OpenSession(unit, StorageAccess.Global);
    var operation = new OperationId(DateTimeOffset.UtcNow, "provider-sequence-operation-1");
    var values = new StorageValues(new Dictionary<string, object?> { ["payload"] = "sequence payload" });
    var outcome = session.AppendWithOutcomes(
        operation,
        values);
    Require(outcome.Status == WriteOutcomeStatus.Inserted, "The ProviderSequence exact append did not insert.");
    var returnedSequence = outcome.Outcomes.Single().GeneratedValue<long>("sequence");
    Require(returnedSequence > 0, "The exact ProviderSequence append returned an invalid generated sequence.");

    var replayed = session.AppendWithOutcomes(operation, values);
    Require(replayed.Status == WriteOutcomeStatus.Replayed, "The repeated exact ProviderSequence append did not replay.");
    Require(replayed.Outcomes.Single().GeneratedValue<long>("sequence") == returnedSequence,
        "The exact ProviderSequence replay did not return the original generated sequence.");

    var row = session.Query(new QueryRequest(
        new TableId(unit.Name),
        new Predicate.AlwaysTrue(),
        [],
        Projection.All,
        Paging.None)).Rows.Single();
    var generatedSequence = Convert.ToInt64(row["sequence"]);
    Require(generatedSequence == returnedSequence, "The persisted sequence did not match the exact append outcome.");
    Console.WriteLine($"[GREEN] ProviderSequence exact append/replay returned the authoritative cursor: sequence={returnedSequence}.");
}

static void RunRetentionBoundaryProof()
{
    var refusal = AssertDeclarationRefusal(
        () => StorageUnit.Declare("e2_zero_retention", "e2_zero_retention")
            .String("entry_id", column => column.Required())
            .Timestamp("occurred_at", column => column.Required())
            .String("payload", 4096, column => column.Required())
            .Key("entry_id")
            .Retention(0, "occurred_at")
            .Build(),
        "GW-PORT-007");
    Console.WriteLine($"[RED] KeepNewest(0) is refused, so an Elsa TrimAsync(0)-equivalent is not expressible: {refusal.Message}");
}

static void RunScopeContractProof(IStorageProviderConnection connection)
{
    var unit = StorageUnit.Declare("e2_scoped_logs", "e2_scoped_logs")
        .String("entry_id", column => column.Required())
        .String("payload", 4096, column => column.Required())
        .Key("entry_id")
        .Scoped()
        .AppendIdempotency(TimeSpan.FromHours(1), "e2_scoped_operations")
        .Build();
    Require(connection.Schema.Apply(unit).Applied, "The scoped manifest did not apply.");

    var first = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("scope-a")));
    var second = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("scope-b")));
    var operation = new OperationId(DateTimeOffset.UtcNow, "same-operation-in-two-scopes");
    Require(first.Append(operation, Values("entry-a", "scope-a payload")).Status == WriteOutcomeStatus.Inserted, "The first scope append did not insert.");
    Require(second.Append(operation, Values("entry-b", "scope-b payload")).Status == WriteOutcomeStatus.Inserted, "The second scope append did not insert independently.");

    var firstRows = first.Query(AllRows(unit)).Rows;
    var secondRows = second.Query(AllRows(unit)).Rows;
    Require(firstRows.Count == 1 && firstRows[0]["payload"] is "scope-a payload", "The first scope observed rows from another scope.");
    Require(secondRows.Count == 1 && secondRows[0]["payload"] is "scope-b payload", "The second scope observed rows from another scope.");

    var globalRefusal = Catch<InvalidOperationException>(() => connection.OpenSession(unit, StorageAccess.Global));
    Require(globalRefusal.Message.Contains("requires Scoped access", StringComparison.Ordinal), "Global access to a scoped unit did not produce the provider-neutral scope refusal.");
    Console.WriteLine("[GREEN] scoped access contract: independent idempotency ledgers, isolation, and global-access refusal");
}

static StorageUnit OrdinaryLogUnit(string name) => StorageUnit.Declare("e2_structured_logs", name)
    .String("entry_id", column => column.Required())
    .Timestamp("occurred_at", column => column.Required())
    .String("level", column => column.Required())
    .String("category", column => column.Required())
    .String("payload", 4096, column => column.Required())
    .Key("entry_id")
    .AppendIdempotency(TimeSpan.FromHours(1), "e2_structured_log_operations")
    .Build();

static StorageValues LogValues(string entryId, string payload, string category) => new(new Dictionary<string, object?>
{
    ["entry_id"] = entryId,
    ["occurred_at"] = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
    ["level"] = "Information",
    ["category"] = category,
    ["payload"] = payload
});

static StorageValues Values(string entryId, string payload) => new(new Dictionary<string, object?>
{
    ["entry_id"] = entryId,
    ["payload"] = payload
});

static QueryRequest AllRows(StorageUnit unit) => new(
    new TableId(unit.Name),
    new Predicate.AlwaysTrue(),
    [],
    Projection.All,
    Paging.None);

static DeclarationFinding AssertDeclarationRefusal(Func<StorageUnit> build, string code)
{
    var exception = CatchBuild<DeclarationBuildException>(build);
    var finding = exception.Findings.SingleOrDefault(item => item.Code == code);
    Require(finding is not null, $"The declaration refusal did not include {code}: {exception.Message}");
    return finding!;
}

static TException Catch<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static TException CatchBuild<TException>(Func<StorageUnit> build) where TException : Exception
{
    try
    {
        _ = build();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
