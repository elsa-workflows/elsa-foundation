# Runtime EF implementation

## Private history projection model

[Spec1211](../../../../../../specs/1211-run-history-timeline/plan.md),
[history inspection contract](../../../../../../specs/1211-run-history-timeline/contracts/history-inspection.md).

`RuntimeHistoryEntityConfiguration` and the five internal history entity types define the private
head, event receipt/content, bookmark, bookmark-count and known-parent projections. They are not
an extension API, are not registered in `RuntimeDbContext` and do not advertise history support.
`RuntimeHistoryProjectionSchema` carries only table names; it declares no module/schema family.

Indexes use bounded scope/run/resource hashes and committed positions; complete lossless identities
and content must be checked by the following provider mapping. Head revisions are concurrency tokens.
Event receipts can survive content retirement; full run deletion and retention mutations remain with
their owning transactions. A known-parent projection does not establish frozen occurrence membership.

`EfRuntimeHistoryProjectionMapping` is a private helper for the same projections. The eventual provider
owner supplies its actual `EfSchemaChain`: every read settles compatibility before interpreting stamped
identity/content and upcasts the complete content column through that chain. The helper declares no
alternative family, registers no reader and does not qualify an atomic provider. Complete scoped identities,
filter projections, committed positions and receipt/purge acknowledgement are checked beside hashes.
Unknown event vocabulary remains recorded; protected payloads and unsafe metadata are refused. Purged
creation links do not upgrade unknown creation evidence. Counts distinguish run and exact activity owners.

`EfRuntimeHistoryCheckpoint` is the private staging seam under construction. It retains the checked native
admission choice, validates ordered logical receipts and cleanup sources, reserves the history head by revision
and stages event/bookmark/count/ancestry rows inside the caller's existing transaction. A first-head reservation
may save inside that same transaction; the caller still owns rollback/tracker restoration, final participant save
and marker-last commit. It returns only newly positioned events for the later derived occurrence participant.
Its head reservation is shared internally with forthcoming observation append; it owns no separate commit.
Missing Required contributions remain owed; missing Optional contributions produce a retained gap. The eventual
native writer must invoke that check for missing contributions too. This seam is not yet called by the production
writer and does not qualify any selected reader, deletion family or capture profile.

The checkpoint seam calls `EfRuntimeHistoryOccurrenceProjection.StageAsync` with only its newly positioned
events, in the same context/transaction. The three internal occurrence receipt/membership/counter shapes and
`RuntimeHistoryOccurrenceEntityConfiguration` preserve first-observation membership, bounded scope ordinals,
unknown-baseline/semantics evidence and receipts through prefix content retention. They copy no journal content.
`StageDeleteAsync` joins full run retirement; the actual native deletion adapter still needs that binding.
All append owners, including independent observations, must stage their new events so the projection's covered
prefix remains coherent. A lagging or unsupported projection must not be advertised as an empty playable run.
The normal Runtime model still registers none of these history/occurrence tables.

`EfRuntimeHistoryObservation` is a private independent-observation staging helper. The eventual owner
authorizes the source, owns its transaction/final save and acknowledgement reconciliation, and normalizes
provider failures. The staged result is not a durable acknowledgement. The helper requires a retained
captured head and matching native policy, uses the same head reservation and occurrence prefix, and preserves
the last covered native checkpoint/time. Receipt replay is immutable; acknowledged purge can return a receipt
without recreating content. Its purpose-separated semantic fingerprint remains64 hex characters for every
provider. No bookmark lifecycle or occurrence is inferred from a delivery observation, and no provider port is
registered by this helper.

The isolated schema/mapping/checkpoint tests cover private projections and relational staging without changing
normal Runtime migration targets. Actual native entry-point/marker integration and family binding, scoped readers,
observation append, occurrence projection, retention/deletion, generated migrations and production qualification
remain open.

`EfRuntimeHistoryRetention` stages explicit prefix content retirement and full run retirement in the
caller-owned transaction. Prefix purge locks/checks the native owner, preserves admission/checkpoint
coverage and all semantic/occurrence receipts, and reserves the monotonic head boundary before a
set-based content update. Full deletion uses eight completely scoped set-based statements rather than
loading a long journal. It is called only after the native owner admits/locks full deletion, never by
inspection or prefix purge. Both reject pending history tracker changes and recheck immutable actual
access before returning; neither commits, acknowledges, authorizes a retention policy or registers a port.
The normal native deletion and independent retention owner still need their production binding.

Prefix retention requires ReadCommitted or Serializable isolation: an earlier InnoDB repeatable-read
view may outlive the current native write lock, and a no-op request would otherwise return an old
boundary without a CAS. The independent owner must choose an admitted isolation explicitly. Full
set-based deletion retains its caller's existing native deletion transaction; this read-view restriction
belongs to prefix boundary reporting. The actual MySQL regression establishes the older read view,
commits retirement from another context, and requires explicit refusal rather than a stale result.
