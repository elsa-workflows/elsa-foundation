# Proposed manifest revision — the five missing OpenIddict declarations

Status: **proposal, NOT applied.** Drafted 2026-08-04 so the pending decision is a concrete diff rather
than a description. Nothing in this document is in the tree.

## Why this exists

All four Groundwork OpenIddict stores are implemented, and **16 members are rejected with a capability
exception because no declared route can serve them** — not because of store code. Those 16 trace to five
missing declarations. This drafts them so a reviewer can approve or reject something specific.

**This is not on the EF-removal critical path.** Groundwork OpenIddict issues tokens, rotates refresh
tokens, invalidates logout and authenticates bearers today. What these declarations unblock is
administrative listing, the T041 application→authorization→token cascade, and OpenIddict's background
pruning.

## The five declarations

All use helpers that already exist in `OpenIddictGroundworkStorageManifest.cs`
(`KeywordIndex`, `DateTimeIndex`, `PointQuery`, and the `ExpiredReceiptQuery` shape added for the
mutation receipt, which is the closest precedent for a date-ranged route).

### 1–2. Count-all / list-all, on all four units

Needs a constant-valued partition field, mirroring the runtime manifest's established pattern
(`ElsaRuntimeStorageManifest.CollectionField = "collection"` + `ByCollectionIndex`, which
`ListAllQuery` filters on).

- add a `collection` field to all four records in `Models/OpenIddictGroundworkRecords.cs`, set to a
  per-kind constant on create;
- `KeywordIndex("openiddict-<kind>-by-collection", "collection")` per unit;
- `PointQuery(ListAll<Kind>Query, index)` per unit.

Unblocks `CountAsync(plain)` and `ListAsync(plain)` on scope, application, authorization and token —
**8 members**.

*This is the only part that changes a persisted record shape.* Admissible under the pre-release
no-back-compat agreement. **Must not bump `SchemaVersion` or `ManifestVersion`.**

### 3. `applicationId` on the authorization unit

- `KeywordIndex("openiddict-authorization-by-application-id", "applicationId")`
- `PointQuery(FindAuthorizationByApplicationIdQuery, index)`

Unblocks `FindByApplicationIdAsync`, `RevokeByApplicationIdAsync` — **2 members**, and the first half of
T041's cascade.

### 4. `applicationId` and `authorizationId` on the token unit

- `KeywordIndex("openiddict-token-by-application-id", "applicationId")` + point query
- `KeywordIndex("openiddict-token-by-authorization-id", "authorizationId")` + point query

Unblocks `FindByApplicationIdAsync`, `RevokeByApplicationIdAsync`, `FindByAuthorizationIdAsync`,
`RevokeByAuthorizationIdAsync` — **4 members**, and the second half of the cascade.

Both columns already exist on the physical table; only the index and route are missing.

### 5. A standalone date route on the authorization and token units

- `DateTimeIndex("openiddict-authorization-by-creation-date", "creationDate")` + a date-ranged query
- `DateTimeIndex("openiddict-token-by-expiration-date", "expirationDate")` + a date-ranged query

Unblocks `PruneAsync` on both — **2 members** — and OpenIddict's background pruning.

Note `TokenSubjectIndex` already carries `expirationDate`, but only as the third column of a
subject-led compound, which cannot serve a prune scan that has no subject to lead with.

## Constraints on whoever applies this

1. **Run the four-provider capability probe.** `tests/Elsa/Persistence/Groundwork/Conformance/Tests`
   → `OpenIddictGroundworkCapabilityProbeTests`, ~3 minutes, needs Docker. It is the **only** check that
   compiles real provider query plans. A declaration change passed 146 store tests, the full 355-test
   architecture suite and the maps gate on this branch and was still illegal (`GW-QUERY-008`).
2. **Point routes, not collection routes.** Every route above is a point or date-range lookup. Do not
   reach for `CollectionQuery` — collection-membership routes cannot use cursor paging, which is the
   constraint that produced the withdrawn regression.
3. **Stay inside SQL Server's 1,700-byte key limit.** The four-field authorization index was dropped for
   exceeding it; that is why the compound `FindAsync` is conditional. Single-column keyword indexes are
   well inside it.
4. **Do not bump `SchemaVersion`.** It is a frozen legacy stamp here, not a migration knob.
5. Update the affected store members from rejection to implementation, and convert their
   rejection tests into behaviour tests — the tests carry comments anticipating this.

## If it is declined

Nothing breaks. The 16 members keep failing closed with a capability exception naming the missing route,
which is honest and observable. T041 and background pruning stay unstartable, and that should then be
recorded as a deliberate scope decision on #643 rather than left looking like unfinished work.
