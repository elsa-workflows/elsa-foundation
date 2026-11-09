# Native ETL component checkpoint

This is component evidence for #3109 / draft #3111, on top of integrated parent `8a74c3c0e2b75b706dbddafc8eb0a43bf866700e`. It does not complete spec 255 or establish actual native composite, two-process, API/Studio, browser or post-merge acceptance. The approved execution checklist remains authoritative; 14/36 tasks remain checked until the corresponding complete gates pass.

## Included behavior

- The design bridge resolves an exact scoped published ETL definition/version/content hash and embeds the bounded artifact. A canonical literal selection participates in the existing parent executable hash; no generic hash format or workflow dependency-row change is introduced. The native activity verifies the retained artifact against that hashed literal. Publication does not require starting the ETL worker feature.
- `RunEtl` uses ordinary typed private state and one typed suspension. The normal `BookmarkCreated` checkpoint is the admission boundary. It captures the actual persisted registration/bookmark, not an internal attempt-ID formula. Admission handlers accept durable responsibility without doing file work.
- Cancellation before bookmark creation carries the complete actual registration address and can create a pre-admission ETL tombstone. After the engine consumes the wait and clears registrations, the durable consumed receipt retains the real registration, including after payload retirement. Whole-parent and subtree cancellation are covered at this component boundary.
- Admission acknowledgement replay converges from the exact immutable ETL record even after native history/private-state cleanup. New admission still validates the durable parent, executable, activity and bookmark in the trusted partition.
- The ETL worker optionally delivers terminal results through a domain contract. It does not claim native delivery without an adapter. The adapter creates an explicitly scoped native operation, sends the exact bookmark/alias/schema/provider/idempotency identity, and reads durable state before acknowledging. Mailbox dispatch status, missing parent alone and missing bookmark with a nonterminal activity are insufficient proof.
- Shared internal ETL JSON preserves opaque UTF-16 code units and the existing EF v1 bytes. The native delivery adapter and EF store share the complete frozen invocation/result envelope hash. No saved format migration is introduced.

## Review and corrections

The bounded root self-review/fix loop closed three correctness groups and ended with a clean pass: consumed-registration cancellation, exact literal/address plus lost-admission-ack replay, and the result-envelope hash contract. A delegated read-only review of the repaired runtime found no remaining actionable finding in this component surface. It is review input, not an independent approval requirement.

The first delivery fixture exposed ten deterministic failures before dispatch: the adapter compared a terminal-only hash with a persisted invocation/result-envelope hash. Both sides now use the existing complete-envelope algorithm from the shared codec; the focused adapter/activity filter passed 21/21 afterward. Initial compile errors and a concurrent unfinished composite fixture were corrected or isolated; no failed attempt is counted as passing.

## Executed validation

- Focused native checkpoint/codec/publication/host-worker filter: 39 passed, zero failures/skips.
- Native adapter and actual activity-contract filter over a real SQLite ETL store: 21 passed, zero failures/skips. Native stores in these focused tests are in-memory; this is not an EF native scheduler/restart qualification.
- Full Release ETL project: 235 passed, zero failures/skips.

The final integrated architecture project passed 579/579 cases with zero failures/skips. Full CI-shaped locked Release and isolated Debug restores passed. Generated dependency-map freshness, solution-filter validation and the diff check passed. An initial architecture pass detected missing architecture-project references for the two new libraries, untracked new lock/readme files, and missing isolated Debug restore assets. These are integration prerequisites, not passing evidence; they are fixed and rechecked before delivery.

## Behavioral counterexamples

[Source hashes and exact filters](native-regression-proofs.json) identify the temporary changes. No counterexample is committed.

1. Omit actual-bookmark address comparison: six address-mismatch cases fail and the intentionally unrelated activity case still passes. Exact restoration passes all seven.
2. Require a live registration after typed consumption: both retained-payload and retired-payload cancellation cases fail. Exact restoration passes both.

Physical effects remain at least once. The historical receipt/HTTP timeout causes remain unproven; these results do not explain those historical failures. Remaining native Sequence/Flowchart/CompensationScope, final materialization, two-process recovery, API/Studio/browser and resulting-main gates are still owed.
