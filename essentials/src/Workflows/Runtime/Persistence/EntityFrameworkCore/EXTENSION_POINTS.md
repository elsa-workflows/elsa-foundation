# Runtime EF implementation

[Spec1211](../../../../../../specs/1211-run-history-timeline/plan.md),
[history inspection contract](../../../../../../specs/1211-run-history-timeline/contracts/history-inspection.md).

## Owned history model and schema

The five internal journal/bookmark/count/known-parent shapes and three derived occurrence shapes join
`RuntimeDbContext`, its existing Runtime migration history and one declared `RuntimeHistory`1.0.0
family with eight complete ContentJson columns. `RuntimeHistoryEntityConfiguration` and
`RuntimeHistoryOccurrenceEntityConfiguration` own their keys, indexes and concurrency tokens.
All four provider models retain bounded indexed hashes/keys while complete identities/content use
large text. The initial RunHistoryTimeline migration, Designer and snapshot exist for every provider.
Frozen legacy upgrades retain original table digests/history IDs and create empty history storage;
schema installation fabricates no capture or occurrence evidence.

`RuntimeHistoryEfModule` owns the actual declared chain. Its metadata-only pairing check requires the
complete owned CLR/member/write mappings, faithful effective member and change-comparer access, declared and actual store conversions,
concurrency and required indexes. Additional unique constraints must be redundant with an owned
unique key. These checks read no journal rows and activate no execution. Model/schema presence alone
enrolls no capture policy or payload authority.

## Native checkpoint and derived staging

`EfRuntimeHistoryCheckpoint` validates immutable native admission and every ordered logical receipt,
including missing Required contributions, then reserves the head by revision and stages journal,
bookmark/count/ancestry and derived occurrence changes in the caller's existing transaction. Native
checkpoint binding invokes it after the fence boundary, before incoming workflow state staging.
Initial-head saves remain inside that same transaction; final participant save, marker-last commit,
rollback/tracker restoration and ambiguous acknowledgement reconciliation belong to the native writer.
Allocation-only contention leaves as cause-preserving history unavailability; corruption, cancellation,
access/schema failure and stale ownership retain their own precedence and causes.

Confirmed uncaptured/no-contribution native checkpoints return before touching history-only storage.
Captured Required/Optional obligations remain authoritative regardless of current module switches.
Required absence refuses; Optional absence retains a gap. `EfRuntimeHistoryProjectionMapping` checks
complete scoped identities, content, positions and replay/purge acknowledgement beside bounded hashes,
settles compatibility before interpretation and upcasts through the same declared chain. Unknown event
vocabulary is retained; unsafe metadata and protected payloads are refused.

`EfRuntimeHistoryOccurrenceProjection` stages only newly positioned activity events in that same
transaction. Its receipt/membership/counter shapes preserve first-observation membership and scoped
ordinals, unknown baseline/ancestry semantics and replay through prefix retention. They copy no journal
content. Every append owner stages its new events; lagging/unsupported projection is never presented as
an empty playable run. Full deletion retires the occurrence family with the other history shapes.

## Selected reader and independent mutation owner

[EfRuntimeHistoryStore](Stores/EfRuntimeHistoryStore.cs) implements bounded details/history/bookmark
and occurrence facets over one ordinary scoped context/accessor/chain. Its deferred SQLite boundary
and [EfRuntimeHistoryOccurrenceReader](Stores/EfRuntimeHistoryOccurrenceReader.cs) retain admitted
caller transactions, authenticated frozen windows and indexed ordinal/anchor reads. Immutable
value-equivalent access snapshots are valid; actual access retirement and cancellation are checked
before disclosure, including failed completion after an owned transaction is disposed.

The existing public native checkpoint aggregate selects that scoped reader when no explicit history
reader exists, replacing only its owned volatile default. It selects one scoped
`EfRuntimeHistoryMutationStore` for both observation and retention beside that owned reader. Stable
factory identity permits either registration order without invoking factories; explicit foreign
readers and independent ports remain untouched. Native and independent pairing qualify the actual
same reader/context/accessor/chain and model, without reading diagnostic content.

`EfRuntimeHistoryObservation` stages independent observations against retained native policy with
the shared head reservation and occurrence prefix, preserving native checkpoint/time coverage.
Semantic fingerprints remain purpose-separated; replay after acknowledged purge preserves the receipt
without resurrecting content. Delivery observations infer no bookmark lifecycle or occurrence.
The independent owner uses one owned Serializable transaction, refuses pending caller work and
caller/ambient/enlisted transactions, and returns only after commit or exact persisted acknowledgement
reconciliation. Failed write/reconciliation causes remain available. A staging result is not an ACK.

## Retention and run retirement

`EfRuntimeHistoryRetention` stages an explicitly requested monotonic prefix purge while preserving
admission/coverage and semantic/occurrence receipts. Prefix boundary reporting admits ReadCommitted or
Serializable: an old InnoDB repeatable-read view cannot report a stale no-op boundary. The real MySQL
control establishes that old view and requires refusal. Full retirement uses eight scoped set-based
deletes inside its caller's admitted native deletion transaction, without loading a long journal.

Both native deletion entry points retire captured history before removing its retention root;
canonical terminal cleanup also removes remaining history rows. Confirmed uncaptured direct root
deletion remains independent of history storage. Hard/access/schema failures clear staged tracker
state and preserve rollback. Inspection and prefix purge cannot authorize full deletion. These
helpers choose no retention policy; prefix receipts survive until full run retirement.

## Capture composition boundary

This native binding selects existing persistence ports and qualifies their pairing. It adds no
capture options, semantic source profile, separate schema owner/connection or payload authority.
Registration-order controls use the existing internal `RuntimeHistoryRegistration.AddServices`.
The public history opt-in and default host feature are supplied with the actual alteration/source
owner integration; admission must still qualify actual source/value/cleanup/delivery inputs.
Native component qualification is separate from authored workflow, restart and composed Studio acceptance.
