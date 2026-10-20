# Owned two-process recovery fixture (T023)

## Scope

A small console test host composes the production ETL runtime and ETL runtime EF feature over fixture-owned SQLite, source/lookup files, snapshots and output roots. The parent test migrates/seeds/prepares once with its worker stopped. Both child hosts validate that existing schema, select the repository-pinned SQLite provider explicitly and execute real `EtlRuntimeWorker` work. No Workbench, HTTP, actor provider or Orleans composition is added. The child does not configure design services; this is not a claim that the persistence package has no design assembly dependency.

The test project has a build-order-only reference to the console host. Tests launch the host's own configuration/framework output, never load that executable into the parent. Internal protocol and forwarding-store source is shared by linked compile items; the existing T021 store barrier now uses the same forwarding base instead of duplicating the interface. All interception remains test-only.

## Causal recovery journey

`Two_real_worker_processes_recover_a_published_unaccepted_effect_after_predecessor_exit`:

1. Admit and prepare a real single-partition run over CSV fixture files. Launch the first child and require its exact readiness line with its actual PID.
2. Pause before the real store's `AcceptBatchAsync`, after the processor has atomically published its complete physical chunk. The outside-transaction marker records the real lease, batch/effects and PID. Require unchanged owed quota and no accepted manifest.
3. Launch a distinct second child against the same files/database; require both live PIDs and the predecessor's currently persisted owner/fence. The predecessor continues heartbeat renewal while its acceptance call is paused.
4. Stop only that owned predecessor process tree and await its exit. The survivor's real store claim must obtain a larger fence and different owner. Require the same cursor, descriptor, counts and exact physical chunk reference, unchanged owed quota and still-empty manifest before accepting.
5. Release the survivor's gate. Require one accepted manifest effect, expected JSONL, one accepted record, zero outstanding quota and the matching durable terminal result/address. No native parent exists in this fixture, so terminal delivery remains owed rather than being falsely acknowledged.

The sampled persisted expiry while the predecessor was live is only a lower bound: a later heartbeat could renew it before exit. The test checks that this sample has passed and relies on the actual store's fenced/expiry-guarded takeover for authority. It does not re-read and assert the old fence after exit while the survivor can race that inspection. There is no fixed sleep to infer takeover, mocked lease, synthetic accepted manifest or assumed process termination.

The process-wide acceptance gate publishes one complete marker and shares its completion with concurrent scoped decorators. Publication uses a private temporary file followed by a same-directory move. Every acceptance waits on the role's release file before calling the real store; the fixture does not hold a database transaction while paused.

## Ownership, diagnostics and checks

The fixture records a child before awaiting readiness, captures both output streams, bounds startup/exit/task waits and stops only owned processes. It disposes every child before deleting shared files; startup/cleanup failures retain ownership, and cleanup failure preserves the shared root/logs. Output-pump failure fails readiness directly and is observed at teardown. Role names and process arguments use typed APIs, not shell command construction. These are fixture control files, not an external product API.

The first focused run passed **3/3**: actual two-process recovery plus both existing real T021 lifetime cases. Final affected validation passed **288 ETL / 579 architecture**, zero failures/skips. Locked restore, regenerated dependency-map freshness, generated solution-filter check and diff checks passed. The dependency map adds only the test-host edges/project; generated documentation maps/findings remain ignored. Root reviewed their findings (no direct package version clusters or runtime-to-design signals) and completed three bounded self-review passes, ending clean for this checkpoint. Review made the acceptance gate process-wide, removed the post-exit inspection race and propagated output-pump failure to readiness. A release file's content is intentionally irrelevant: its existence is the control signal, never product output.

An initial compiler failure exposed that the provider-neutral persistence module intentionally supplies no SQLite engine; the fixture host now references the same centrally pinned `Microsoft.EntityFrameworkCore.Sqlite` package already used by the tests. A comparison of overlapping lock entries found no existing package version difference. No schema/public API or production runtime source changed from T021. Earlier Studio/native/REST evidence retains its own checkpoint provenance; fresh exact-head CI is reported separately in the public receipt.

```sh
dotnet restore Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --locked-mode
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter 'FullyQualifiedName~Two_real_worker_processes|FullyQualifiedName~Real_replaced_attempt|FullyQualifiedName~Real_preparation_retains'
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

## Remaining acceptance

T023 establishes the owned process fixture and one published-effect-before-cursor survivor recovery case. **T024 remains incomplete**: the full read/admission/staged-write/accepted-cursor/result interruption matrix and restart variants are not implied. The later [staged-write checkpoint](staged-write-recovery.md) adds the reviewed internal publisher seam and actual source/JSONL midpoint process evidence; it does not complete T024. Full persisted authoring/picker/publication, API/Studio/HTTP/browser, schema-provider acceptance and resulting-main verification remain owed. Physical effects remain at least once. Historical receipt/HTTP timeout causes remain unexplained; PR #3111 and the program remain incomplete.
