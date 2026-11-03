# Spec 1196 evidence: bounded state for continuous ETL runs

Source: the branch `agent/claude/2908-etl-compaction`. Each section names the revision it was recorded at.

## Prior writer

- **Fixture** (recorded at `6226b78af`). [`Continuous1192/runtime.db`](../../../Extensions/Etl/tests/Shared/Fixtures/Continuous1192/README.md) was written by the spec 1192 writer at `6226b78af`, before any persisted format changed. It holds a two-shard continuous run with 5 accepted pages.
- **The test** (`A_spec_1192_continuous_run_migrates_keeps_its_rows_and_compacts_its_history`) does this, in order:
  1. migrates a copy;
  2. compares every settled page, batch and historical request row with the prior writer's, byte for byte, and finds `PRAGMA foreign_key_check` empty;
  3. resumes the run on the same loopback port, and checks that each shard then holds only its window, with a base request that has no predecessor;
  4. delivers two appended records exactly once, and completes the run `Succeeded` with 8 accepted;
  5. finds the foreign keys still consistent.

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-005, FR-007, FR-010, SC-001: a window bounded per shard, exact accounting, lifetime counts, file deletion | `A_continuous_run_accepts_far_more_pages_than_its_page_limit_with_a_bounded_window_per_shard`: two shards, 40 pages each, page limit 3. Sampled peak per shard: at most 2 pages and 3 requests. 80 run-level main effects. The snapshot root ends empty. 80 distinct deliveries. The test checks the run's retained input bytes directly. Every other retained byte counter is checked by each claim's audit, which would have failed the run. |
| FR-001, FR-002 (increment), FR-003 (keys), SC-003: poll generation | `A_stalled_attempts_late_page_never_reaches_a_later_poll_of_the_same_cursor`: the exact interleaving. The stalled capture is `LeaseLost`. The next poll runs in generation 1 and attaches its own page. Both manifests coexist. |
| FR-003: identities | `Generation_zero_keeps_the_finite_page_identity_and_every_later_generation_is_distinct` |
| FR-006, FR-007: a null predecessor only at the base | `A_null_predecessor_outside_a_compacted_base_is_refused_by_the_claim_audit` |
| FR-011, SC-007: finite runs unchanged | `A_finite_http_shard_keeps_its_whole_history_and_no_compacted_summary_across_claims`, plus the whole suite |
| SC-006: real processes | `Continuous_worker_processes_killed_repeatedly_keep_a_bounded_window_and_every_record_once`: 4 workers killed before acceptance, page limit 3, 24 records, each exactly once |

**Changed existing tests**, each for a stated reason:
- `Database_request_chain_constraints_reject_incomplete_history` lost its `null-predecessor` case. The database now admits that case (FR-006), and the new audit test above covers its refusal.
- `Connector_source_fetch_migration_preserves_legacy_history_and_claims_its_continuation` reads a single historical column at its old migration target, because spec 1196 adds partition columns.

## Bite-proofs

Recorded in [`bite-proofs.json`](bite-proofs.json). Each mutation makes its focused tests fail, and the restore makes them pass.

**Tested without a bite-proof, or not tested:**
- **Untested requirements.** The final caught-up path leaving the generation unchanged (FR-002). Reading a legacy shared body (FR-003, US3 scenario 3; the fixture has no cached page). The cycle refusal limited to the window (FR-009). The per-partition inspection effect counts (FR-008; only the run-level total is asserted). Each follows from code that the README names.
- **The audit's reads are bounded (SC-002).** The audits stream only retained rows, and SC-001's test bounds those rows. No test counts the rows the audit reads.
- **Same-fence acceptance replay after compaction.** Compaction runs only in a claim, which changes the fence, so a same-fence retry cannot span it. No test exercises it.
- **Compaction precedes budget checks** (a spec 1192 run at its retention ceiling). This follows from compacting in the claim transaction, but no test drives a run to its ceiling.
- **Orphaned files** from a crash between a commit and its file deletion, or from a stalled attempt's late page, are left in place. No test asserts their count.

## Actual Workbench journey

[`compaction_workbench_journey.py`](compaction_workbench_journey.py) runs against an owned Workbench on an ephemeral port:
- a two-shard feed answers one record per page, against a page limit of 3;
- a native Flowchart starts the run.

Its summary is in `workbench-journey-summary.json`. The owner's Workbench on port 5095 is never touched.

## Limits

These are stated in the ETL README:
- The cycle refusal covers the retained window only.
- Budgets must fit one peak window per shard.
- A continuous shard's lifetime page sequence is bounded at 2³¹ − 1.
- File deletion is best effort.
