# Research: Continuous HTTP ETL runs

Each decision below comes from the current code at the branch base (`ffd7e1d7e`, CSV delivery) and from the spec review. Paths are relative to `Extensions/Etl/src`.

## R1. Where the run policy lives

**Decision.**
- Add a new public record, `EtlContinuousRunPolicy(int PollIntervalSeconds)`.
- Expose it on `EtlPipelineV2` as an init-only property, `Continuous`, whose default is `null`.
- Do not add a constructor parameter.
- `EtlArtifactV2Json.MarkSharedModelPropertiesRequired` sets `ShouldSerialize` for this property to "not null". This follows the default-budget precedent at `Core/EtlArtifactV2Json.cs:166-176`.

**Why.**
- `EtlPipelineV2` is a positional record, and its constructor is in `PublicAPI.Unshipped.txt`.
- The serializer writes nulls; `lookup` is emitted as `null`. A plain nullable member would therefore add `"continuous":null` to every artifact and change every hash.
- `null` means finite, so no mode enum is needed.
- Future continuous sources, such as RabbitMQ, can reuse the same policy.

**Rejected.**
- *A `Run { Mode, … }` object:* it adds a value, an explicit `finite`, that must never be serialized.
- *A policy on `EtlHttpJsonSource`:* continuity is a property of the run, not of HTTP.

## R2. Classifying a caught-up response

**Decision.**
- Add `EtlHttpRequestBuilder.IsCaughtUp(EtlHttpRequest current, int recordCount, string? decodedContinuation)`. It is true when `recordCount == 0` and the continuation is empty or resolves to the current request's URI. It uses the same token and next-link URI construction as `NextContinuation`.
- Only continuous pipelines call it. `EtlHttpSourceAcquirer` calls it after `EtlJsonPageReader.Read` and before `NextContinuation`, `cache.PublishAsync` and capture.
- In `EtlHttpPageCache.Decode` (the `NextContinuation` call at line 309), a continuous pipeline normalizes a caught-up page's continuation to `null`. The normalization depends only on the pinned artifact and the page bytes, so every read and replay of a retained final page decodes identically.

**Why.**
- Today an empty same-cursor page throws `etl.http.pagination-cycle` from `NextContinuation` (`EtlHttpRequestBuilder.cs:166,175`). The page is then recorded as invalid and the run fails.
- An empty page without a continuation is published and exhausts the shard.
- Both cases must be intercepted before those paths run.

## R3. Recording a caught-up response without growth

**Decision.** Add a new store transition, `RecordConnectorHttpCaughtUpAsync(lease, attempt, responseReceivedAtUtc)`. It returns `EtlConnectorHttpCaughtUpResult(EtlStoreStatus Status, bool Final)` and runs in one Serializable transaction.

1. It applies the same run, partition, authority, position and owed-batch preconditions as `MutateConnectorHttpSourceAsync`.
2. It requires the pipeline to be continuous and the journal to be non-null.
3. It validates the attempt as `EtlHttpSourceFetchTransitions.Capture` does:
   - the attempt is the latest one, under the lease fence;
   - it has no outcome;
   - its request hash matches;
   - it was received within `[StartedAt, min(ExpiresAt, Deadline))`;
   - it was received no later than the store's time.
4. If the connector run's completion-request time is at or before `attempt.StartedAt`, the transition returns `Final = true` and changes nothing (see R5).
5. Otherwise it records the idle state:
   - `request.FetchJson = null`, `FetchDigest = null`;
   - `request.MetadataBytes -= FetchMetadataBytes + SourceMetadataReserveBytes`;
   - `FetchMetadataBytes = SourceMetadataReserveBytes = InputReservationBytes = 0`;
   - the same metadata and input deltas are subtracted from the partition (`MetadataBytes`, `SourceInputReservedBytes`) and the connector (`RetainedMetadataBytes`, `SourceInputReservedBytes`);
   - `partition.NotBeforeUtcTicks` is set to the store's time plus the interval if no completion is pending. If completion was requested after the attempt started, it is set to `null` (due at once).

It adds no rows. The lease is not released here; the execution's `finally` releases it normally, because the position is unchanged (`ReleaseConnectorPartitionAsync` compares `PositionJson`).

**Why.**
- A null journal requires zero reservations (`EfEtlConnectorHistory.cs:331-334`).
- Journal validation rejects a completed 200 that is not marked invalid, so the journal cannot record the response itself. The review's B3 finding applies here.
- The resulting state is exactly a request's "never fetched" state, which `Begin` already handles. The next poll therefore needs no new code path.

## R4. The idle invariant

**Decision.** `EfEtlConnectorHistory` (lines 93-99) currently requires `row.NotBeforeUtcTicks` to equal the waiting journal's `NotBefore`, or null, whenever no batch is owed. Its new rule is:
- That requirement still applies when the journal is non-null.
- When the journal is null and no page is attached, `NotBeforeUtcTicks` may be any positive value, but only for a continuous pipeline.
- A finite pipeline keeps requiring null.

**Why.**
- An idle wait therefore cannot be confused with a retry wait, because a retry wait always has a `Waiting` journal.
- Completion can clear exactly the idle waits, which are the waits whose journal is null (review B2).

## R5. Completion

**Decision.**

*Column and migration.*
- Add a nullable `CompletionRequestedAtUtcTicks` column to `EtlConnectorRunRecord` / `elsa_etl_connector_runs`.
- Add a SQLite runtime migration, `20261102170000_ConnectorRunCompletion`, plus the model snapshot. The column carries `CHECK (… IS NULL OR … > 0)`.
- `SchemaVersion` stays `"1.0.0"`. The three earlier connector migrations (`20261021150000`–`170000`) added columns without changing it, so the module's rule is that additive migrations keep the version.

*Store transition.* `RequestConnectorRunCompletionAsync(scope, runId)`, in one Serializable transaction:
- it returns `NotFound`, `Terminal`, `NotContinuous` or `AlreadyRequested` without writing;
- otherwise it sets the column to the store's time and clears `NotBeforeUtcTicks` on every partition with no owed batch, no attached page and a null request journal. Retry waits keep their time.

*Drain.* When `RecordConnectorHttpCaughtUpAsync` returns `Final`, the acquirer publishes the response:
- its continuation is normalized to null (R2);
- it is captured and attached as usual;
- `EtlConnectorExecution` then calls `AdvanceEmptyHttpPageAsync`, which exhausts the shard. That call also runs `CompleteConnectorRunIfReadyAsync`, so the last draining shard settles the run inside the same transaction.

*Preconditions.*
- The capture precondition in `MutateConnectorHttpSourceAsync` refuses a continuation at `MaxPagesPerPartition`. A final page has none, so the precondition does not apply.
- The page ledger check for the next request hash is skipped because the continuation is null.

**The completion race (review S2).** The `Final` test uses `attempt.StartedAt`, which is the store-issued permit time, against the store-written request time. An attempt that started before the request may have read the feed before records were appended, so it re-arms the shard as due at once.

## R6. A zero-record page whose continuation moves

**Decision.** In a continuous pipeline, `AdvanceEmptyHttpPageAsync` sets the new partition's `NotBeforeUtcTicks` to the store's time plus the interval. The next request has a null journal and no page, so this is the idle state from R4. Completion clears it the same way. Finite pipelines are unchanged.

## R7. Validation sites

**Decision.** A shared validator, `EtlContinuousRunPolicyRules.Validate(EtlPipelineV2)` in Core, returns the first error code. It is called from:
- the design compiler (`EtlArtifactCompilerV2`);
- `EtlConnectorExecutableReader.ValidateLimits`;
- `EtlConnectorRecordCodec.ValidateBindings`, at runtime admission.

It accepts only an `EtlHttpJsonSource` with token or next-link pagination, and an interval in `1..86400`.

## R8. API and inspection

**Endpoint.**
- `POST /etl/runs/{id}/complete` is guarded by a new permission, `EtlRuntimePermissions.CompleteRuns = "etl/runs:complete"`, contributed alongside cancel.
- The resource entry is `etl-run-complete`.
- `IEtlRuntimeOperations.CompleteAsync` has a default body that returns `notFound`, following `EtlRuntimeApiModels.cs:98-102`.
- The response is an `EtlRunCompletionView(string RunId, string Result)` with status 200.

**Inspection.**
- The connector run view gains `Mode`, `finite` or `continuous`, and `CompletionRequestedAt`.
- The connector partition view gains `NextPollAt`, set only for an idle partition. The existing `NotBefore` stays as it is.
- All of these are additive members.

## R9. Studio

- **Editor.** `EtlConnectorPipelineEditor` gets a "Run continuously" switch and a poll-interval number field. They are shown only when the source is `httpJson` with token or next-link pagination. Turning the switch off removes the property.
- **Run inspection.**
  - `EtlRunInspection` shows "Continuous" and "Completion requested".
  - Each idle partition shows "Polls again at …", which is distinct from "Eligible after".
  - `EtlRunsPage` gets "Complete run" next to "Cancel run", calling `etlApi.completeRun`.

## R10. Evidence

- **Feed server.** A local cursor-feed server, written in Python with only the standard library, serves two shards. Records can be appended over HTTP, and each shard's GET with `after=<cursor>` returns the records after that cursor.
- **Journey driver.** The Workbench journey driver, derived from spec 1191's, drives the feed, the run, completion and cancellation, and reads the runtime API to count pages, requests and bytes across idle polls.
- **Worker-process journeys** reuse `Elsa.Etl.WorkerHost` with the existing `AfterReservation` / `BeforeAcceptance` gates. They add a staging pause around an idle poll's attempt, either a transport delay or a new test gate.
