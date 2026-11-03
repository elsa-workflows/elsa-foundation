# Tasks: Bounded state for continuous ETL runs

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

## Phase 1 — Compatibility capture
- [ ] T001 Migration test that seeds a continuous run with spec 1192 schema rows (several accepted pages, batches and requests). Written before the migration and failing without it.

## Phase 2 — Poll generation (US2)
- [ ] T002 `EtlConnectorHttpPosition.PollGeneration` and its serialization: omitted at 0, read as 0 when absent. Codec rule: non-negative, and above 0 only in a continuous run. PublicAPI.
- [ ] T003 `ConnectorHttpPageId` with the generation. Test: bytes at generation 0 are identical.
- [ ] T004 The caught-up record increments the generation; the final path does not. Tests.
- [ ] T005 Page cache and acquirer: generation-aware keys, the per-page body identity for continuous runs, legacy validation. Test: the superseded-late-page interleaving no longer fails the run.

## Phase 3 — Compaction (US1, US3)
- [ ] T006 Partition summary columns and the relaxed request check (entity, context, migration with rebuild, snapshot). T001 passes.
- [ ] T007 `CompactConnectorHttpHistoryAsync`, called on both advances in continuous runs.
- [ ] T008 Base-relative HTTP history audit and batch history audit. Inspector effect counts.
- [ ] T009 Skip the page-sequence bound in continuous runs, at every site.
- [ ] T010 Tests: window, accounting, lifetime counts, constant audit reads, same-fence replay after compaction, finite runs unchanged, a pre-slice run compacted at its first advance.

## Phase 4 — Files (US1)
- [ ] T011 `EtlHttpPageCache.DeleteAsync` and `DeleteRetainedRouteBodiesAsync`; execution calls both after commits in continuous runs. Test: cache files stay bounded over a long run.

## Phase 5 — Evidence and docs
- [ ] T012 Long continuous journey test (2 shards, 40 or more pages each, `MaxPagesPerPartition` 3) and real-process kills.
- [ ] T013 READMEs, `EXTENSION_POINTS.md`, and spec 1192's limits pointed here.
- [ ] T014 Bite-proofs, Workbench journey, evidence README, the whole ETL and architecture suites.
