# Connector owed reservations (partial T005)

Connector runs can now reserve nonempty measured input under their current partition ownership.
The range is explicitly either a CSV ordinal interval or an HTTP retained-page/item interval;
HTTP item offsets are never represented as file ordinals. The store validates the exact source
position, retained request and page content hash, positive measured bytes, input digest and stable
batch identity before recording responsibility.

The versioned identity binds the immutable invocation, selected binding kinds/revisions, shard seeds,
prepared inputs, partition, typed range and measured canonical input. Its fields are explicit rather
than the serialization of snapshot DTOs, so unrelated future snapshot fields cannot silently change
mechanical identity. Worker identity, fence and attempt are excluded. The worker must still verify
and measure actual input; a descriptor is not proof that a physical input object has been read.

## Durable accounting and recovery

One indexed owed-batch pointer per partition refers to a canonical retained descriptor. Reservation
updates that pointer, the shared run's outstanding record/byte charges and retained metadata charges
in one serializable transaction. A replay requires the same descriptor and current unexpired authority;
it does not charge again. If the lease expires after the save but before commit, the transaction rolls
back. Invalid ranges and quota refusals leave no reservation or accounting changes.

Release and expiry retain responsibility. Acquisition prioritizes owed work, audits its history and
returns the original descriptor to the replacement worker. Normal transitions compare run accounting
to a bounded partition-to-owed-batch join rather than scanning accepted history. Full recovery audits
reject corrupted descriptors, mismatched metadata, unsupported settlement and unlinked responsibility.
A successful terminal proposal refuses outstanding connector batches. Cancellation retains owed
records and charges as evidence while preventing further reservation; it does not reverse an external
effect or prove that an old physical attempt stopped.

The [metadata budget](../data-model.md#connector-storage-upgrade-boundary) now also includes exact UTF-8
bytes of retained batch descriptors. It remains distinct from outstanding input bytes, retained source
bodies, physical attempt allowances and database row/index overhead.

## Schema and validation

The additive `20261021120000_ConnectorBatchReservations` migration introduces the batch table and an
optional foreign key from each partition to its owed batch. SQLite rebuilds the partition table while
preserving existing request/page pointers. The structural upgrade test starts at the preceding draft
migration, retains linked rows, applies the new migration, checks all foreign keys, confirms enforcement
is restored on the same connection, and proves that a missing owed-batch reference is refused. This is
schema preservation evidence, not the still-owed old-worker process resume proof in T003. Released v1
migration files remain unchanged; the earlier draft-schema compatibility boundary remains applicable.

Focused transition gates passed 43 and 27 tests, including HTTP/CSV recovery, exact replay, invalid typed
ranges, cross-shard record/byte limits, metadata refusal, cancellation, expiry rollback, corrupted-history
refusal and migration preservation. The final full-suite and architecture counts are recorded below and
in the PR receipt. An earlier test compilation caught raw-string interpolation errors in the migration
fixture; these were corrected before the passing gate.

Two negative controls failed for the intended reasons: removing post-save expiry validation committed
an expired reservation, and removing the reservation-history audit allowed unlinked owed work to be
claimed. Production files were restored byte-for-byte before the full affected suite. A first full run
passed 629 tests; the subsequent identity refinement and additional identity test are covered by the
final run, rather than inferred from that earlier pass.

On the final reviewed source, the full ETL suite passed **630 tests** and the architecture suite
passed **584 tests**, both with zero failures/skips. These include retained v1 cases but do not prove
the still-unimplemented HTTP worker. Existing build warnings remain. Map/filter/diff results and
41 resolved relative documentation links are recorded in the PR receipt.

## Remaining acceptance

This checkpoint neither advances a nonempty source position nor acknowledges output. T005 remains
open and the task count remains **3/22**. Next are retained per-route bodies/effect identities, durable
attempt/deadline/retry eligibility, acknowledgements and atomic batch acceptance. Acceptance must
replace the current refusal of settled batch history with verified accepted ranges and effects, extend
retention accounting explicitly, and settle final counts/terminal/outbox in the same transaction.

HTTP runtime remains explicitly unavailable until the execution path is integrated. Full connector
combinations, native/Studio journeys, interruption/cancellation/retry matrices and old-worker resume
remain required. No live HTTP, browser, throughput or physical capacity qualification is claimed by
these storage tests. Historical CI timeouts and main-red #2293 are not explained by these results.
