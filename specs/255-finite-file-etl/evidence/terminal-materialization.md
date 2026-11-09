# Terminal accepted-output materialization

T016 adds a runtime-owned operation to materialize complete JSONL routes from a terminal run's accepted manifest. This is an internal runtime checkpoint in draft PR #3111, not the full author/publish/invoke/inspect product gate.

## Operation and boundaries

`EtlAcceptedOutputMaterializer` reads only the scoped run and its ordered, paginated manifest. It resolves the exact retained output revisions, streams accepted chunks through a 4 KiB buffer, verifies their deterministic batch keys, lengths and digests, and publishes each nonempty route through the existing staged/create-only `FileChunkStore`. It never enumerates physical files to decide acceptance. The versioned materialization identity reuses the lossless, length-framed ETL identity routine; timestamps and physical completion order do not affect output bytes.

Matching replay returns the same immutable route references. A changed accepted chunk or conflicting materialized file is refused; replay never overwrites the conflict. Empty routes have no file reference. An unadmitted cancellation tombstone has no fabricated binding/output. Cancelled and faulted output is labelled partial; BusinessError retains its accepted main and error routes. The operation neither settles the run nor changes its manifest, result or delivery outbox.

Pagination adapts its preferred 64-entry page to a lower host `take` cap through bounded reductions; no EF-specific dependency is added. State consists of scalar accounting, a bounded page and the copy buffer. Checked observed byte totals bound the candidate; this introduces no total-run output ceiling and makes no heap/RSS claim. Each route is independently atomic. A failed/interrupted multi-route call may leave a complete unreferenced candidate, recoverable by deterministic replay; no atomic file pair or process/power-loss qualification is claimed here. See the [contract](../contracts/journey.md#terminal-jsonl-materialization).

No public Core response, persisted record, schema version or migration changes. API/Studio exposure remains on the existing checklist. Runtime-only composition uses no design database/services for this operation.

## Executed acceptance and review

Twelve added real-runtime cases cover both routes/multiple batches, deliberately reversed partition execution, one-entry host pages, matching replay, exclusion of a correctly named but unaccepted physical orphan, missing chunks, same-length altered content, empty input, refusal of active/missing/other-scope runs, removed pinned revisions, real cancelled/faulted partial output, cancellation before admission, and a changed final file that remains untouched after conflict. Partial outcomes come from actual store/execution transitions, not fabricated terminal records.

The initial red compile confirmed the absent production materializer/types, after correcting a missing test import. Review then corrected source-versus-destination I/O classification, required all counters to be zero without admitted bindings, reused the existing identity helper rather than widening internal visibility, and shared manual partition preparation/execution setup. Scoped worker input was reviewed and integrated by root; final integration/QA remains root-owned.

The full ETL suite passes **285**, with zero failures/skips. The focused materialization filter passes **13** (including an existing matching test). The [digest regression receipt](materialization-digest-regression.json) records temporarily omitting source digest comparison: the same-length corruption case must fail while the missing-source case still rejects; byte-exact restoration and the complete focused filter pass. The architecture suite passed **579**, zero failures/skips. Root self-review used four bounded passes, ending clean for this internal operation. Solution filters/diff passed. An initial simultaneous maps/filter build collided on the generator reference assembly; this was a local build-output sharing error, not a map-data finding. The serial map check passed. Fresh exact-head CI is recorded with the pushed checkpoint receipt on #3111. Studio CI on the previous T022 head failed the already-filed #3081 readiness race (seed11); it is not green and is separate from this operation's local validation.

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter FullyQualifiedName~materializ
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

T016 does not complete live-worker permit/replacement proof, two-process interruption/recovery, public API/Studio, full persisted workflow publication/picker or HTTP/browser acceptance. Physical effects remain at least once. Earlier native runtime/REST results retain their own checkpoint provenance; they are not newly executed by this operation's tests. Historical receipt/HTTP timeout causes remain unproven. Optional actor-provider work remains deferred.
