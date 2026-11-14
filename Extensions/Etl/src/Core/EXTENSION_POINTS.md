# ETL Core extension points

Owner project: `Elsa.Etl.Core`
Domain: `Elsa.Etl`

## Safe binding discovery

`IEtlBindingCatalog` lists only current binding aliases and roles in an exact `EtlScope`. Its exclusive ordinal
alias/role boundary and finite limit allow authoring hosts to discover permitted choices without disclosing
paths, revision handles, digests or credentials. The default `HostFileBindings` adapter lives in the runtime
implementation; another host may implement this metadata-only contract without starting runtime workers.

`ContainsBinding(scope, alias, role)` checks current approval for one exact role in that same scope without
opening a file. Authoring validation and new publication use it; an exact replay of an existing immutable
publication does not depend on today's catalog. This check does not reserve a revision or replace runtime admission.

This port does not resolve file paths, admit runs, select input revisions or provide arbitrary storage access.
The finite runtime owns those operations and preserves the existing pinned revision/publication rules.

## Connector binding approval

`IEtlConnectorBindingCatalog` is an additive v2 catalog exposing alias, role and connector kind. It does not
replace the file-only v1 catalog. A current alias/role pair is unique across file and HTTP kinds within one
scope. `IEtlConnectorBindingResolver` resolves a complete binding set for a validated `EtlPipelineV2`, or a
stable value-free failure code; it performs no source GET or destination POST. Connections metadata resolution
can involve storage I/O. New publication checks approval again; immutable replay uses the retained publication.

The default runtime adapter snapshots `EtlRuntimeFeature.HttpBindings`. Its opaque revision handles commit
the exact scope, endpoint/path policy, shard seeds, and resolved connection identity and non-secret settings.
Exact retained revision resolution never substitutes the current alias revision. Authenticated resolution
requires the retained tenant and public Connections Core providers; anonymous bindings compose without them.
No credential is read during validation or pinning. The short-lived connection is disposed after each resolution;
request execution must obtain a new exact-pin resolution and authenticate each request separately. Credential
source references are not exposed by this public port, so changes to those references remain part of permitted
credential rotation. The [HTTP profile](../../../../specs/996-http-etl/contracts/http-profile.md) defines the
transport and publication boundaries. Binding ports do not themselves execute work; the shared worker consumes their exact pins.

## Runtime inspection bounds

`IEtlRuntimeStore.MaximumPageSize` advertises the positive maximum accepted `take` of each list call. A consumer
may compose multiple bounded, exclusive keyset pages to serve its own bounded response; it must not silently
increase the provider's cap or interpret an expired claim as worker termination. The runtime API uses this
capability for at most 201 retained rows including lookahead. Manifest materialization streams pages under the
same limit. See the [finite ETL contract](../../../../specs/255-finite-file-etl/contracts/journey.md).

`IEtlConnectorRuntimeInspector` is a separate optional read-only capability for consistent retained connector
snapshots and bounded partition pages. It has the same positive cap and short-page/exhaustion contract.
Run snapshots contain internal invocation data; public consumers must project approved fields. Partition
projections carry typed source progress and safe receipt/retry metadata. Counts represent settled route
bodies, and neither expiry nor acknowledgement proves physical termination or accepted progress. The SQLite
provider reads a consistent transaction and aggregates indexed delivery-mechanism columns (HTTP or file) without materializing the
historical output documents. See the [inspection checkpoint](../../../../specs/996-http-etl/evidence/connector-inspection-checkpoint.md).

## Connector artifact format

`EtlPipelineV2` is a versioned data contract, not a connector-provider registration surface. The supported
source, pagination and destination variants are explicit. `EtlArtifactV2Json` provides bounded, strict
serialization independently of `EtlPipeline` / `EtlArtifactJson` v1. Model declarations contain host binding
aliases and selectors, not endpoint or credential values. Artifact validity and host runtime admission are
separate decisions; a successfully decoded document is not proof that the configured runtime can execute it.

The runtime applies independent `EtlRuntimeFeature.HttpLimits` and live-attempt capacity ceilings, and checks
mapping/filter/lookup semantics without design services. A compiler-valid artifact can exceed a particular
host's ceilings. A missing lookup does not reserve a synthetic lookup allowance. The existing file-only store
contract still rejects v2 admissions. An optional `IEtlConnectorRuntimeStore` supplies typed connector transitions;
the shared worker discovers it and uses the same physical attempt pool, heartbeat and terminal delivery. A selected
file-only replacement/decorator remains usable and receives a durable v2-unavailable refusal. See the
[worker checkpoint](../../../../specs/996-http-etl/evidence/http-worker-checkpoint.md). A connector provider's
`CommitConnectorPreparationAsync` must settle a zero-record file run (CSV, JSON Lines, or a SQL source's snapshot) as
success in the same transaction, because no partition exists to complete it; an identical replay returns `Replay`
([spec 1190](../../../../specs/1190-etl-jsonl-input/spec.md) FR-011). A provider must also accept `EtlConnectorKind.Csv` route
outputs as file routes ([spec 1191](../../../../specs/1191-etl-csv-delivery/spec.md)): `RecordConnectorFilePublicationAsync`
matches a CSV publication against `EtlRuntimeIdentity.ConnectorFileOutputKey(effectId, kind)` (a `.csv` key, refusing the
`.jsonl` one), and a CSV route counts as a file publication for delivery, acceptance and inspection state.
A provider keeps SQL source watermark checkpoints ([spec 1199](../../../../specs/1199-etl-sql-watermark/spec.md)): `FindConnectorSourceWatermarkAsync` returns the checkpoint for a key, and the transaction that completes a run whose prepared run carries a `SourceWatermark` raises that checkpoint to it, never lowering it; a run settled by a failure leaves it unchanged. The interface's default returns null, so a store without checkpoints reads every row.
A provider must accept a SQL source's `EtlConnectorKind.Sql` source pin, which carries no expected digest, and a prepared
snapshot whose hash it cannot compare to a pin: that hash is the snapshot's anchor ([spec 1198](../../../../specs/1198-etl-sql-source/spec.md)).
A provider likewise accepts `EtlConnectorKind.Sql` route outputs ([spec 1197](../../../../specs/1197-etl-sql-destination/spec.md)):
the runtime records an applied SQL route through `RecordConnectorFilePublicationAsync` under
`EtlRuntimeIdentity.ConnectorSqlEffectKey(effectId)`, and it counts as a recorded publication (effect mechanism 2).
A provider accepts `EtlConnectorKind.RabbitMq` route outputs the same way ([spec 1200](../../../../specs/1200-etl-rabbitmq-delivery/spec.md)):
the runtime records a route whose messages the broker confirmed under `EtlRuntimeIdentity.ConnectorRabbitMqEffectKey(effectId)`
(an `.amqp` key), and it counts as a recorded publication (effect mechanism 2).
A provider accepts a RabbitMQ source's `EtlConnectorKind.RabbitMq` source pin, which carries no expected digest, and a
prepared snapshot anchored on its own hash, as for a SQL source ([spec 1201](../../../../specs/1201-etl-rabbitmq-source/spec.md));
the runtime acknowledges the source's messages only after `CommitConnectorPreparationAsync` returns `Applied` or `Replay`.

`EtlPipelineV2.Continuous` is an optional, init-only run policy; a null policy is never serialized, so finite artifacts
keep their bytes and identities. `EtlContinuousRunPolicy.FindViolation` is the one rule every reader applies (HTTP
source with token or next-link pagination, interval 1–86,400 s). For continuous runs a connector provider implements two
members whose default bodies refuse ([spec 1192](../../../../specs/1192-etl-continuous-http/spec.md)):
`RecordConnectorHttpCaughtUpAsync` validates the live latest in-flight attempt under the lease fence and its receipt
window, then clears the request journal, releases its input and source-metadata reservations, retains no page and
schedules the partition's next poll (at once when completion was requested after the attempt started); when completion
was requested at or before the attempt started it changes nothing and returns `Final`, and the caller retains the
response as a final page whose unmoved cursor is normalized away. `RequestConnectorRunCompletionAsync` records a durable
request and makes only idle partitions (cleared journal, no owed batch) due at once; retry waits keep their schedule.
A provider's audits must accept an idle partition's eligibility time without a waiting journal for continuous runs only.
A provider that keeps the refusing defaults still admits a continuous artifact, whose first caught-up response then
fails the run with `etl.http.source-transition-refused`; such a provider should not be offered continuous pipelines.
`AdvanceEmptyHttpPageAsync` always delays a continuous shard's next request by one interval after a moving empty page.
`RequestConnectorRunCompletionAsync` reports `NotContinuous` for a finite run whether or not it has settled; `Terminal`
is only for a settled continuous run. `EtlConnectorRunInspection.Continuous` / `CompletionRequestedAt` and
`EtlConnectorPartitionInspection.NextPollAt` are init-only, so existing inspector implementations keep compiling;
`NextPollAt` is set only for an idle partition and equals its `NotBefore`.

A continuous provider also keeps each shard's state bounded ([spec 1196](../../../../specs/1196-etl-continuous-compaction/spec.md)).
`RecordConnectorHttpCaughtUpAsync` increments the shard's poll generation (not on the `Final` path), and a claim copies
it onto `EtlConnectorPartitionLease.PollGeneration`, which is 0 for every finite shard. Page-cache keys use the
`ConnectorHttpPageId` overload with that generation; generation 0 is the finite identity. Every claim in a continuous
run deletes, for every shard of the run, the rows below the predecessor of its current request before the claim commits
(budgets are per run, so compacting only the claimed shard is not enough), keeps lifetime counts
through a compacted summary, and audits retained history from that base. A provider must not apply the authored
`MaxPagesPerPartition` to a continuous shard's page sequence.

Connector providers distinguish temporary outstanding-capacity pressure (`BudgetExceeded`) from a transition
that cannot fit the retained-input/output/metadata limits (`PermanentBudgetExceeded`). The worker yields for
temporary pressure and settles a safe fenced diagnostic for a proven permanent shortage. Terminal failure keeps
unaccepted descriptors and accounting as evidence; it never fabricates acceptance to clear counters. Providers
must retain the request/page history needed to classify irreversible growth and must not mistake another live
partition's releasable reservation for permanent exhaustion.

## SQL engines

**`IEtlSqlEngine`** — adapter keyed by provider name (§2.7), layer: Core contract, no default implementation
([specs 1197](../../../../specs/1197-etl-sql-destination/spec.md), [1198](../../../../specs/1198-etl-sql-source/spec.md) and [1199](../../../../specs/1199-etl-sql-watermark/spec.md)).

```csharp
public interface IEtlSqlEngine
{
    string Provider { get; }
    int MaxParameters { get; }
    string? SessionSetup { get; }
    DbConnection CreateConnection(string connectionString);
    EtlSqlTarget DescribeTarget(string connectionString);
    string CreateLedger(string ledgerTable);
    string InsertLedger(string ledgerTable);
    string ReadLedgerHash(string ledgerTable);
    string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns);
    string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row);
    string Parameter(string column, int row);
    EtlSqlErrorKind Classify(Exception exception);
    IsolationLevel SnapshotIsolation { get; }
    string? SnapshotSetup { get; }
    string? SnapshotLock(string table);
    bool IsLockRefusal(Exception exception);
    bool ExtremeDatesAreInfinite { get; }
    string SelectSnapshot(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, string? watermarkColumn);
}
```

- **What it is.** An adapter that isolates one SQL driver. It is not a fan-in contributor: the runtime's SQL bindings resolve exactly one engine per binding provider, and refuse two engines with the same name. The runtime's writer owns the delivery protocol (ledger row, unique-key check, ordered single-row upserts, one transaction), and its snapshot reader owns the read (one transaction at `SnapshotIsolation`, ordered by the key, retained as JSON Lines); an engine never commits or retries.
- **Registration:** `services.AddSingleton<IEtlSqlEngine, …>()` from an engine feature.
- **When to implement:** to deliver to or read from another engine. Supply connections, a target without secrets for the pinned handle, dialect text, the isolation level at which one statement reads one point in time without blocking writers, an optional table lock taken before the snapshot (the reader falls back to an unlocked read when the engine refuses it), whether the driver maps infinite dates to the extreme `DateTime`/`DateOnly`/`DateTimeOffset` values (so the reader refuses them), a snapshot select that, given a watermark column, keeps only rows whose watermark is greater than the `@watermark` parameter or is NULL (spec 1199: a NULL watermark must still be read, so the run rejects it rather than skipping it), and exception classification.
- **What depends on it:** `HostSqlBindings`, `EtlSqlRouteWriter` and `EtlSqlSnapshotReader` in `Elsa.Etl.Runtime`.
- **Known implementations** (intra-domain, ETL): `PostgreSqlEtlSqlEngine` (`Elsa.Etl.Sql.PostgreSql`) and `SqlServerEtlSqlEngine` (`Elsa.Etl.Sql.SqlServer`).

## RabbitMQ publisher

**`IEtlRabbitMqPublisher`** — single-implementation adapter (§2.7), layer: Core contract, no default implementation
([spec 1200](../../../../specs/1200-etl-rabbitmq-delivery/spec.md)).

```csharp
public interface IEtlRabbitMqPublisher
{
    EtlRabbitMqTarget DescribeTarget(string connectionString);
    ValueTask<EtlRabbitMqPublishOutcome> PublishAsync(string connectionString, string exchange, string routingKey,
        IReadOnlyList<EtlRabbitMqMessage> messages, CancellationToken cancellationToken);
    EtlRabbitMqErrorKind Classify(Exception exception);
}
```

- **What it is.** An adapter that isolates the RabbitMQ driver. The runtime's writer owns the protocol: one message per record with a stable id and effect headers, and what each outcome means. The publisher connects, publishes the messages in order on one confirming channel (persistent, mandatory, `application/json`), returns `Confirmed` only when the broker confirmed every message, `Unroutable` on a return and `Nacked` on a negative acknowledgement, and classifies other failures as transient or rejected.
- **Registration:** `services.AddSingleton<IEtlRabbitMqPublisher, …>()` from `EtlRabbitMqFeature` in `Elsa.Etl.RabbitMq`. Register at most one: with two, building the runtime's connector bindings throws `InvalidOperationException`, so no ETL attempt runs, as for two SQL engines with one provider name.
- **When to implement:** to publish through another client or transport with the same confirm semantics. Bound every confirm and round trip: a broker under a resource alarm blocks publishers without closing their connections, so an unbounded wait would hold the attempt and its host capacity until the alarm clears; a bounded one surfaces as a transient failure. `DescribeTarget` must exclude the password, because its result is hashed into the binding's pinned handle.
- **What depends on it:** `HostRabbitMqBindings` and `EtlRabbitMqRouteWriter` in `Elsa.Etl.Runtime`.
- **Known implementations** (intra-domain, ETL): `RabbitMqEtlPublisher` (`Elsa.Etl.RabbitMq`). `DescribeTarget` throws `ArgumentException` or `FormatException` for an invalid URI, which binding resolution reports as `etl.binding-unavailable`.

**`IEtlRabbitMqConsumer`** — single-implementation adapter (§2.7), layer: Core contract, no default implementation
([spec 1201](../../../../specs/1201-etl-rabbitmq-source/spec.md)).

```csharp
public interface IEtlRabbitMqConsumer
{
    ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>> accept, CancellationToken cancellationToken);
    EtlRabbitMqErrorKind Classify(Exception exception);
}

public interface IEtlRabbitMqReceipt : IAsyncDisposable
{
    int Count { get; }
    ValueTask AcknowledgeAsync(CancellationToken cancellationToken);
}
```

- **What it is.** An adapter that receives a finite source's messages without acknowledging them and hands each body to the runtime's `accept` callback as it arrives; the body is valid only during that call. The runtime writes the body to the run's snapshot and refuses it when it would pass the snapshot bound or the run's retained-input budget; a refused body, and every later message, must stay queued. It also stops taking messages once a drain time bound well inside the broker's `consumer_timeout` is spent, having taken at least one, so the broker never closes the channel for deliveries held unacknowledged too long; the rest stay queued. An exception `accept` throws must propagate unchanged, so the runtime can tell a snapshot write failure from a broker failure. The runtime calls `AcknowledgeAsync` only after the store has recorded the snapshot; any other outcome disposes the receipt unacknowledged, which must let the broker requeue the messages.
- **Registration:** `services.AddScoped<IEtlRabbitMqConsumer, …>()` from `EtlRabbitMqFeature`, beside the publisher, which binding resolution also uses to describe a source binding's target. More than one registration makes connector binding resolution throw.
- **When to implement:** to receive through another client with the same unacknowledged-until-told semantics and drain time bound, without holding received bodies after `accept` returns.
- **What depends on it:** `HostRabbitMqBindings`, `EtlConnectorPreparer` and `EtlConnectorExecution` in `Elsa.Etl.Runtime`.
- **Known implementations** (intra-domain, ETL): `RabbitMqEtlConsumer` (`Elsa.Etl.RabbitMq`).


## Runtime store failures

`IEtlRuntimeStore`, `IEtlConnectorRuntimeStore` and `IEtlConnectorRuntimeInspector` document `EtlStoreException` for terminal storage failures. It preserves the operation, known scope/run identity and original cause. Cancellation, established corruption/validation handling and schema-write refusals remain distinct. The EF implementation owns its transaction unit and refuses caller-owned transactions before changing tracking or storage. Server registration follows the concurrency controls; [actual both-store host receipts](../../../../specs/1206-etl-runtime-concurrency/evidence/workbench-journey.json) cover native Flowchart → JSONL ETL → Done and completed-state restart on three server engines. Native persistence remains SQLite in that proof; mid-work/old-writer recovery and Studio remain separate. Physical connector I/O is outside database retries.
