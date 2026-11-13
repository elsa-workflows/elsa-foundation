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

The isolated schema/mapping/checkpoint tests cover private projections and relational staging without changing
normal Runtime migration targets. Actual native entry-point/marker integration and family binding, scoped readers,
observation append, occurrence projection, retention/deletion, generated migrations and production qualification
remain open.
