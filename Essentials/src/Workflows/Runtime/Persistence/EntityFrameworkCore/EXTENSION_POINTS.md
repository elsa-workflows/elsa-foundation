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

The isolated `RuntimeHistoryProjectionSchemaTests` exercise the relational model without changing the
normal Runtime migration targets. Native participants, checked schema-chain mapping, scoped readers,
observation append, occurrence projection, generated migrations and production qualification remain open.
