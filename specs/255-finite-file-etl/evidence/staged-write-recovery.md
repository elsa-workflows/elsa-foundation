# Staged-write process recovery (T024 partial)

## Execution and publication boundary

The production executor now receives a scoped preparer and batch processor through DI. Their internal format publisher factory supplies the existing CSV/index/JSONL `FileChunkStore` implementations; the accepted-output materializer uses the same factory. The fixed formats retain their bounds, borrowed-stream ownership, hash verification and create-only publication. This is an internal composition seam, not a new authored setting, public connector contract or alternate persistence authority. No schema or package pin changed.

The fixture host decorates that factory and splits one real write into two. It flushes a positive prefix to the real publisher-owned staging stream, atomically records the actual process ID/output root/effect identity/format/write lengths, and waits outside the database transaction. It does not fabricate a chunk or accepted manifest. Per-role immutable configuration lets only the selected predecessor pause during staging; the replacement uses ordinary publishers.

`Actual_worker_interrupted_mid_staged_write_recovers_without_accepting_partial_content` runs twice: once during actual source-snapshot copying, once during the actual JSONL output write. Its 800-row single-partition fixture ensures the source has logical input left to copy and the output write contains the complete expected batch. The test observes the explicit flushed-prefix marker before enumerating staging. While the child is paused, there is no final file or accepted manifest. Preparation has no persisted prepared state or partitions; output execution retains the real owed batch and record/byte reservation.

The parent stops and joins only its owned predecessor, reads the exact partial bytes, and then starts the replacement. This ordering avoids racing a successor that can publish the final file. The replacement obtains a larger preparation or partition fence, rebuilds the full source/chunk, and pauses before actual acceptance. The output replay preserves the owed descriptor, cursor and record/byte quota; the source replay retains the complete snapshot's expected key/hash. Before release, the complete deterministic file exists but the manifest remains empty. Release allows the real fenced transaction to accept one matching effect and settle success with 800 records and zero outstanding quota. The abandoned predecessor staging remains unchanged: lease expiry is not authority to delete another attempt's files.

These two cases prove sequential replacement after process interruption. The earlier [T023 case](two-process-recovery.md) separately proves two overlapping live worker processes. This is process-loss qualification, not power-loss durability, OS cache behavior, callback-exception cleanup or all-format midpoint coverage.

## Delivery fencing

`Expired_delivery_claim_is_fenced_until_consumer_proof_settles_once` uses the actual SQLite store and its existing controllable clock. Cancellation leaves a reserved batch's quota owed. A replacement delivery claim keeps the exact delivery ID/result/address/hash with a larger fence. The old claim cannot acknowledge while delivery is owed, or change the new owner/fence/expiry, run result or quota. The replacement's typed store-boundary proof settles once; identical proof replays even from a now-stale claim after settlement, and a conflicting disposition refuses. The proof is supplied as this store test's input; native consumption has its own real bookmark/receipt tests. No native parent or consumption is invented here.

## Causal checks and review

The focused publication composition suite passed 57 tests. The existing actual-process journey plus new delivery fence passed 2 tests after per-role protocol changes. The corrected midpoint fixture passed both cases. The first midpoint run exposed a fixture error: it expected one 800-record batch while retaining the pipeline's default two partitions. Explicit single-partition configuration fixed that expectation; it was not a production defect.

Temporarily bypassing the composed preparer and processor in actual `EtlExecution` makes both child-process midpoint cases fail explicitly because acceptance is reached before the required staged-write barrier. No timeout is the regression oracle. [Source hashes and restoration receipt](publication-handoff-regression.json) record byte-exact restoration. Final affected-suite/check results are recorded below and in the public exact-head receipt.

Root review corrected the partition assumption and a post-exit observation race. A separate read-only worker review found no actionable issue in DI routing, stream ownership, prefix visibility, process ordering or reservation assertions. The final root pass covers the integrated code, test protocols and evidence; no independent approval is required under the owner's standing authorization.

## Final local validation

After byte-exact restoration, **291 ETL and 579 architecture tests passed**, zero failures/skips. Locked restore, regenerated maps/freshness, solution-filter generation/check and diff checks passed. No tracked generated-map delta was produced. Ignored map findings were reviewed: all 72 catalogs indexed, no missing root catalogs and zero runtime-to-design direct reference signals. Three root review passes ended clean after the two fixture corrections described above; no unresolved actionable finding remains for this checkpoint. CI results belong to their exact public head receipt and are not inferred from these local checks.

## Remaining acceptance

T024 remains unchecked. Read-to-reservation, post-acceptance/pre-observation and result-delivery process interruptions, conflicting output recovery and the complete duplicate/late/stale result matrix must still be reconciled. Persisted native publication/picker, API, Studio, rebuilt-server/browser, remaining schema/provider and resulting-main gates remain owed. PR #3111 stays draft; Program #2892 is not complete. Physical external effects remain at least once; historical receipt/HTTP timeout causes remain unexplained.
