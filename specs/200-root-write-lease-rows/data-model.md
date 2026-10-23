# Data Model: Root-Write Lease Coordination Without a Hot Row

## WorkflowExecutableRootWriteLeaseEntity (new)

Table: `elsa_runtime_workflow_executable_root_write_lease` (Runtime context, `RuntimeArtifact` schema family).

| Field | Type | Rules |
|---|---|---|
| `Id` | string (hash) | Primary key. `Hash("{len}:{scope}{len}:{artifactId}{len}:{leaseId}")`, built with the same hashing helper as the coordination row id. |
| `ScopeKey`, `ScopeKeyHash` | encoded string, hash | Same encoding as the executable and coordination rows. |
| `ArtifactId`, `ArtifactIdHash` | encoded string, hash | Same encoding. |
| `LeaseId` | encoded string | Ordinal and case-sensitive. Keeps the existing `Lease_dictionary_keys_remain_case_sensitive` semantics. |
| `Token` | string | A fresh random value at each grant. Unique across live leases and guards, as the existing token validation requires. |
| `ExpiresAtUtcTicks` | long | The lease is live while `ExpiresAtUtcTicks > nowTicks`. |
| `IncarnationId` | string | The incarnation of the executable and coordination pair at grant time. |
| `Revision` | long | Concurrency token. Used only to overwrite an expired row or a row from another incarnation. |
| `SchemaVersion` | string | `RuntimeArtifactEfModule` stamp. |

**Indexes:** primary key on `Id`, plus a non-unique index on (`ScopeKeyHash`, `ArtifactIdHash`, `ArtifactId`, `ExpiresAtUtcTicks`). The second index serves the live-lease count on the guard path and artifact-wide deletes.

**State:**
- *Absent* → **Live**, when acquired.
- **Live** → **Live**, when renewed with a later expiry.
- **Live** → *Absent*, when released.
- **Live** → **Expired**, when `now` reaches `ExpiresAtUtcTicks`.
- **Expired** → **Live**, when acquired again with the same id. The row is overwritten.
- **Expired** → *Absent*, when purged or when the artifact is deleted.

## WorkflowExecutableCoordinationEntity (existing, narrowed role)

- No columns change.
- `ContentJson` keeps the `CoordinationState(Leases, Guard)` shape, so there is no content-schema change.
- After the upgrade the code never writes `Leases`.
- Existing unexpired entries in `Leases` are *legacy leases*. They block guard grant and guarded delete until they expire.
- The guard path drops expired legacy entries whenever it rewrites the row.
- The row becomes guard-only: only garbage-collection operations write it.

## Invariants

| ID | Invariant | Enforced by |
|---|---|---|
| I1 | A live deletion guard and a live lease (new or legacy) never coexist after both operations return success. | Write-then-check on both sides, plus guarded-delete re-verification (research R2). |
| I2 | A lease row is honoured only for its own incarnation. | Every check compares the incarnation. Pair deletion removes the artifact's lease rows. |
| I3 | A holder's acquire, renew and release read or write only that holder's own lease row, plus reads of the pair. | The R3 operation shapes. |
| I4 | Lease operations never save or discard changes staged on the caller's `RuntimeDbContext`. | An isolated context per operation (R4). |
| I5 | A successfully committed write is never reported as failed because its lease could not be released. | Manager release isolation (R7). |
| I6 | Each checkpoint commit attempt holds a distinct lease id. | `checkpoint:{CommitId}:{nonce}` (R8). |

## In-memory parity

`InMemoryWorkflowExecutableStore` already keeps separate lease and guard dictionaries under one lock, so I1–I3 hold there trivially. Its semantics stay as they are. The checkpoint commit stores (EF and in-memory) take the per-attempt lease id.
