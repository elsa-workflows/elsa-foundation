# Connector publication and atomic acceptance checkpoint

Scope: partial T005 of [spec 996](../spec.md), issue #3151, draft PR #3156. T005 remains
open and task completion remains 3/22. This is storage evidence, not full connector execution acceptance.

## Implemented behavior

- JSONL publication evidence validates the actual publication's deterministic effect key, hash and
  length against the retained route body. Publication is distinct from acceptance. The caller verifies
  the physical bytes; a real file-publisher test checks the derived key against its published file.
- Acceptance requires every present JSONL/HTTP route's publication/acknowledgement. It settles one
  retained batch, releases outstanding input, updates partition and shared counters, advances the
  typed file or HTTP page/item position, and retains historical descriptors/output/evidence.
- A fully consumed HTTP page creates the next immutable request or becomes exhausted. A nonempty
  fully filtered batch needs no route effect. The last batch or empty page commits terminal result
  and delivery outbox together with progress, including the business-error result for rejected rows.
- Current ownership is checked after progress and terminal saves. Expiry rolls back both. A current
  owner may replay accepted work without recounting it; the returned lease includes any newer owed
  batch. A terminal run returns `Terminal` on duplicate acceptance without another result/outbox.
- New reservations charge the exact positive metadata growth needed for acceptance before dispatch.
  Acceptance releases that reserve and accounts for the actual next position/request. Migrated
  checkpoints retain zero reserve; output-retention replay preflights/top-ups their capacity before
  any recovered output dispatch, and HTTP permits independently enforce the top-up. Incoming HTTP
  outcomes may still be retained without acquiring new dispatch capacity.
- Recovery and terminal settlement stream complete histories and validate accepted prefixes, typed
  ranges, per-page accepted items, admission/effect identities, route evidence, counts and budgets.
  Ordinary transitions and accepted replay use bounded indexed reads.

## Review and verification

Final ETL verification passed 729 tests with zero failures or skips. The last two review passes
strengthened the budget regression and found no remaining actionable checkpoint issue. Earlier review
corrected stale replay responses, nonempty exhausted-page validation, acceptance metadata headroom,
and historical migration fixtures querying future columns.

Three final regression controls independently removed route evidence, post-save expiry rollback and
the acceptance reserve. Each failed the intended tests; production sources were restored byte-for-byte
before the final full suite. The first reserve control exposed a weak test: measuring the reservation
itself also reduced the test quota when the reserve was removed. That initial control incorrectly
passed and is retained in the diagnostic record. The final test calculates required bytes from the
expected next durable state without reserving a batch and verifies both exact fit and one byte short.

Architecture passed 584 tests with zero failures or skips. Generated maps, solution filters,
56 relative links and diff checks passed. Fresh-head CI remains separate pending evidence. Main-CI report #2293 is open; these local passes do not qualify main.
The migration is additive and preserves prior bodies/journals/foreign keys. No released v1 migration
was changed. The optional task-runner follow-up remains outside this implementation.

## Remaining acceptance

Source-fetch attempts, immutable request-addressed page recovery, actual HTTP transport composition,
old-worker resume, complete native/API/Studio journeys and two-process interruption proofs remain
owed. Runtime connector execution remains explicitly unavailable until these integrations are proven.
Existing v1 behavior must remain covered. Physical external effects remain at least once. This
checkpoint does not explain any historical timeout or authorize merging the partial draft.
