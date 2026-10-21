# HTTP request ledger and retained metadata (partial T005)

The connector store keeps a normalized immutable request ledger keyed by scoped run, partition and
page sequence. Unique request digests prevent revisiting an earlier request. Composite foreign keys
link each continuation request to the preceding page and link the partition to its current request.
A continuation cannot refer to a terminal page, and deleting a referenced predecessor is refused by
the database. Response bodies remain immutable object references rather than inline database payloads.

Normal attachment, empty-page advancement, renewal and release read indexed current/predecessor rows.
Their accounting sums partition counters, whose cardinality is bounded at preparation, rather than
summing or deserializing all historical pages. No process-local validation cache is needed. Fresh
ownership acquisition and successful terminal settlement audit the complete finite request/page
history, including canonical JSON, stored digests, links and byte totals. This is a linear recovery
cost, not a claim that all history is checked on every heartbeat. Current/predecessor corruption is
refused before a normal transition; older payload corruption is refused at recovery or terminal audit.
The database and these checks are not a defense against an adversary rewriting every stored invariant.

## Retention policy

`MaxRetainedInputBytes` still counts prepared source/index/lookup data and retained page bodies.
`MaxRetainedMetadataBytes` separately counts the actual UTF-8 bytes of each current partition's
`PositionJson`, every request's `RequestJson` and every page's `PageJson`, across all shards of the run.
Its authored default is 64 MiB; the independently configurable host ceiling defaults to 256 MiB.
Admission/preparation/terminal envelopes keep their separate bounds. SQL row/index/file overhead is
excluded, so this is not a maximum physical database-size guarantee.

Preparation, page attachment and continuation advancement check this aggregate budget and update
both partition and run counters transactionally. Replays do not recharge. If ownership expires after
a save, the whole transition rolls back, including request rows and counters. Limits remain independent:
a larger payload budget does not authorize more metadata.

The default metadata setting is omitted by the canonical v2 writer. Existing default-v2 bytes and
hashes therefore remain unchanged, including at the authored artifact-envelope size limit. Nondefault
values are explicit and round-trip through the same strict codec. No alternate canonical reader or
rehashed replacement artifact is required.

## Schema and compatibility boundary

See the [draft schema evolution boundary](../data-model.md#draft-schema-evolution). This refines the
unmerged connector migrations; the released v1 migration remains unchanged. Earlier private draft
connector databases have no in-place upgrade path. Mixed old/new worker binaries remain unsupported
after v2 admission is enabled, as documented in the preparation checkpoint.

## Verification

The focused gate passed 184 tests and the migration/model gate passed. Tests cover exact persisted
UTF-8 accounting, cross-shard budget enforcement, atomic quota refusal, replay, expiry rollback,
database-enforced request links, corruption on fresh ownership, 64 KiB continuations, and bounded SQL
query shapes for all four hot transitions. The SQL tests use fresh contexts and retained history;
they are structural checks, not performance benchmarks or production capacity qualification.

Two negative controls failed for the intended reasons: removing the metadata quota check accepted a
second shard's page beyond the run limit, and skipping the recovery audit allowed a new owner to claim
corrupt older history. Both production files were restored byte-for-byte before the full ETL run.

On restored final production source, the full ETL suite passed **602 tests** and the architecture
suite passed **584 tests**, both with zero failures/skips. The ETL suite includes the retained v1
migration and process-recovery cases, but these do not prove the still-unimplemented HTTP worker.
The first build caught a missing request DbSet, a subsequent test run caught missing snapshot FK
indexes, and compilation caught test setup errors; these were corrected before the passing gates.
The final map/filter and diff results are recorded in the matching PR receipt.

## Remaining work

This resolves the two resource findings from the [retained page checkpoint](http-page-store-checkpoint.md).
It does not complete T005 or enable HTTP execution. Nonempty reservations/batches, route bodies,
durable attempts/retries/acknowledgements and atomic acceptance still need implementation. Each new
retained state family must explicitly extend the accounting and terminal-quiescence rules. Full
HTTP/native/API/Studio/process recovery and retained-old-worker proofs remain owed. Tasks remain 3/22.
