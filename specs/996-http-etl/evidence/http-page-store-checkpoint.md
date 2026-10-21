# Retained HTTP pages and empty-page progress (partial T005)

The connector store now attaches a verified page reference to its expected HTTP request under the
current partition lease. Attachment records immutable metadata and a unique per-partition request
digest, advances no input records, and charges the referenced bytes exactly once. A replacement
worker receives the same retained page. Replayed attachment and empty-page progress return the
current position only while ownership is still valid; late owners cannot mutate state.

Empty pages advance to their retained continuation or explicit exhaustion without creating a
reservation, output effect or accepted-record charge. Nonempty pages cannot use this transition.
Continuations that repeat a visited request, exceed finite limits or contradict single-page mode
are refused. The existing shared terminal operation permits success only after the exact partition
set is complete; until batch acceptance exists, nonempty pages cannot establish completion.

The additive SQLite table stores `EtlRetainedHttpPage` metadata, not inline response payloads. The
body remains a referenced immutable object. Canonical metadata has its own 512 KiB limit; continuations are capped at 64 KiB so two lossless cursor strings fit that metadata envelope. Referenced
response size is subject to the authored page and total retained-input limits. Accounting includes
all retained page history, including consumed empty pages, plus prepared file/index/lookup objects.
Run inspection reads the aggregate in the same SQL snapshot as the run and terminal state. Store
operations stream and verify the bounded request chain before progression and roll back if ownership expires
during persistence. Admission and worker-upgrade restrictions from the
[preparation checkpoint](connector-preparation-checkpoint.md) still apply.

## Remaining acceptance

This is persistence support, not HTTP execution acceptance. The caller must actually publish and
verify the referenced immutable response before attachment. Transport binding validation, resolved
next-link policy and request normalization remain part of the HTTP worker work. Durable attempts,
retry eligibility, nonempty batch reservations, route bodies/acknowledgements and atomic batch
acceptance remain required before enabling connector runtime execution. Completing those operations
must extend terminal quiescence and position-history validation with accepted-batch evidence. Full
HTTP/native/API/Studio/process-recovery proof and the retained old-producer worker proof remain owed.

T005 remains unchecked; total completed tasks remain 3/22.

## Review and resource findings at this checkpoint

Three bounded root/worker review passes covered schema, replay/history and integrated limits. Review
found and corrected incomplete older-history validation and continuation policies larger than the
persisted metadata format supports. The focused compiler/reader/store run passed 171 tests after
correcting one test's admission-versus-preparation expectation; no production behavior was weakened.

The subsequent [request-ledger and metadata checkpoint](http-metadata-ledger-checkpoint.md) addresses the following resource findings while preserving T005 as incomplete.

At this checkpoint, the full chain check streams metadata without EF tracking, but repeating it on every progression
still accumulates quadratic work across a long shard. The page-body retention budget also excludes
metadata overhead, which is bounded separately per page and by finite page count. Before runtime
qualification, T005 must choose a bounded aggregate metadata budget and a validation/index strategy
that avoids repeatedly replaying the complete history while preserving the demonstrated corruption
and loop refusal. This checkpoint is not evidence of acceptable resource usage at maximum limits.
Do not enable the connector runtime or close T005 without reconciling these findings.

The first narrow history mutation left the test green because the shared position validator independently checked the same request hash. Root removed the redundant inline comparison and retained that shared validation before repeating the control.

## Integrated verification

- `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore`: **575 passed**, zero failures/skips, after restoring production source from the two final negative controls. This includes migration/model-snapshot, retained v1 and real process recovery cases; it does not prove the still-unimplemented HTTP worker.
- Both final request-history controls failed their targeted regression when the relevant check was removed. Restored page-store SHA-256: `92e6f4a61efe66c4cccf50ee24ddb319036afc002c27aa291762ffa1c481503c`.
- Final architecture/map/filter results are recorded in the matching PR checkpoint receipt. No backend REST/browser HTTP acceptance is claimed by this storage-only checkpoint.
