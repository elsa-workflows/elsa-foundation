# Data model

All rows belong to the authoritative Workflow Design physical tenant scope. User-controlled comparisons use application-normalized/hash-backed keys; raw string uniqueness alone is insufficient on SQL Server.

- **Folder:** opaque ID, parent, display/normalized sibling identity, revision and timestamps; same-scope parent, depth <=16, no cycles. No child or retained workflow placement on delete.
- **Definition organization:** one row per folded definition, nullable folder and revision; outside authored state, cascading with permanent definition deletion.
- **Tag/value:** stable ID/key, display metadata, marker/controlled mode, single/multiple cardinality, retirement and revision; controlled values have stable identities and retirement. Assigned semantics cannot be repurposed.
- **Assignment:** definition plus tag and optional controlled value; unique memberships and single-cardinality enforcement in the committing transaction.
- **View:** ID, server-derived owner, private/shared flag, name, bounded schema-versioned query/columns and revision.
- **Favorite:** tenant/user/definition unique personal reference.
- **Change record:** actor, target, timestamp, operation/idempotency identity and semantic before/after committed atomically.
- **Read scope:** transient server-derived visibility restriction and policy revision, separate from saved queries and client inputs; bounded allowed/denied definition identities filter counts and paging and participate in cursor identity.

Hierarchy admission and create/placement use one tenant fence. Manual organization survives reconciliation and soft delete/restore; deleted targets reject edits until restored.
