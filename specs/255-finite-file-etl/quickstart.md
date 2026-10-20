# Finite ETL validation guide

**State:** acceptance target, not a claim of implemented commands or passing ETL tests. Follow [tasks.md](tasks.md) as implementation lands. Domain admission is recorded in [ADR 0087](../../docs/adr/0087-etl-owns-its-execution-domain.md).

## Existing runtime seam

From repository root, run the focused bookmark tests:

```bash
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore --filter FullyQualifiedName~RuntimeCreateBookmarkSchedulerWorkHandlerTests
```

They must show that the typed private state survives in the same bookmark-created commit as the matching bookmark, legacy Running-to-Suspended behavior remains supported, and delayed creation after cancellation/stale registration commits nothing. These tests verify existing infrastructure only; they do not prove ETL admission or recovery.

## Implemented-journey acceptance to execute later

1. Rebuild Workbench with the optional ETL design/runtime/EF/API/native features and the Studio companion. Use a fresh local validation database and a fixture-owned shared filesystem, following `e2e-tests/README.md` and ETL's eventual module README. Configure named immutable CSV/lookup revisions and separate accepted/error JSONL bindings. Never point the fixture at production storage.
2. In Studio create a pipeline with required `id`/`country` fields, a country lookup, an output mapping and an equality filter. Validate/publish. Confirm raw paths and unauthorized binding aliases are rejected. Save the exact artifact/hash/version.
3. Invoke the version from a native Sequence followed by a visible marker, then from a Flowchart with its ordinary completion route. Use enough fixture records for two nonempty partitions and two workers. Verify each row is emitted, filtered or rejected, no per-record/batch workflow exists, output JSON types/order match the contract and the parent accepts one continuation.
4. Repeat with row validation/missing lookup errors, a destination conflict, and explicit cancellation. Verify distinct business-error/fault/cancellation semantics and no duplicate parent continuation. Editing/publishing a draft must not affect the older artifact or run.
5. Use deterministic fixture barriers to interrupt workers before effect, after durable chunk effect but before cursor acceptance, and during terminal delivery. Stop/restart one process or recover on the survivor. Inspect persisted fences/cursors/reservations/outboxes, not elapsed time. Identical replay converges; conflicting content and late ownership refuse.
6. Deliver cancellation before admission and delayed bookmark work after cancellation. Confirm the tombstone prevents later run work and a cancelled parent is not resumed. Retry terminal delivery across a restart; queued transport is not mistaken for consumption.
7. Fill record and byte admission independently, exceed per-record/output/lookup bounds, and pause an old attempt after lease replacement. Confirm the live attempt retains local permits until unwind and the successor requires its own capacity. Resume from corrupted/missing pinned snapshots and unknown format versions; visible refusal must not advance the cursor.
8. Inspect all runs/partitions in Studio, including recovery and partial output. Exercise keyboard navigation, field diagnostics, loading/empty/forbidden/missing-feature states and narrow-screen layout. Inspect a runtime-only host with no design database.

## Delivery evidence

Run focused ETL/native tests, the real two-process recovery fixture, relevant rebuilt-server e2e and browser checks. Add a meaningful safeguard removal/restoration for cursor/effect acceptance or cancellation-before-admission. Use the existing architecture guard and migration/provider checks for the admitted scope. Refresh lock files before generated maps after dependency changes; run the maps check and stage all actual map changes including the manifest.

Record exact commits, commands, fixtures, pass/fail/skip counts and limits in the PR receipt. Independently review exact-head changes, verify CI and resulting-main gates, then update issue/project status. Passing one slice does not complete parent #2908 or Program #2892.
