# Tasks: Bounded state for continuous ETL runs

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

## Phase 1 — Compatibility capture
- [x] T001 Prior-writer fixture captured from the spec 1192 writer (`tests/Shared/Fixtures/Continuous1192`), with a migration test that resumes and compacts it.

## Phase 2 — Poll generation (US2)
- [x] T002 Partition `PollGeneration` column with a check and an audit rule (above 0 only in a continuous run), copied onto the lease's init-only `PollGeneration` at each claim. PublicAPI. (Revised from a position member after the design review, research R4.)
- [x] T003 `ConnectorHttpPageId` with the generation. Test: bytes at generation 0 are identical.
- [x] T004 The caught-up record increments the generation; the final path does not. Tests.
- [x] T005 Page cache and acquirer: generation-aware keys, the per-page body identity for continuous runs, legacy validation. Test: the superseded-late-page interleaving no longer fails the run.

## Phase 3 — Compaction (US1, US3)
- [x] T006 Partition summary columns and the relaxed request check (entity, context, migration with rebuild, snapshot). T001 passes.
- [x] T007 `CompactConnectorHttpHistoryAsync`, called by every claim in a continuous run, for every shard, after the claimed shard's audit (research R8).
- [x] T008 Base-relative HTTP history audit and batch history audit. Inspector effect counts.
- [x] T009 No page-sequence bound in continuous runs: the store's pipeline decoder and the page cache.
- [x] T010 Tests: window, accounting, lifetime counts, finite runs unchanged, a pre-slice run compacted at its first claim. What is not separately tested is listed in the evidence README.

## Phase 4 — Files (US1)
- [x] T011 `EtlHttpPageCache.DeleteAsync` and `DeleteRetainedRouteBodiesAsync`; execution calls both after commits in continuous runs. Test: cache files stay bounded over a long run.

## Phase 5 — Evidence and docs
- [x] T012 Long continuous journey test (2 shards, 40 or more pages each, `MaxPagesPerPartition` 3) and real-process kills.
- [x] T013 READMEs, `EXTENSION_POINTS.md`, and spec 1192's limits pointed here.
- [x] T014 Bite-proofs, Workbench journey, evidence README, the whole ETL and architecture suites.
