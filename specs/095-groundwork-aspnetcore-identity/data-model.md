# Data Model: Groundwork ASP.NET Core Identity

## Model Rules

- Every ordinary record is scoped by one nonblank tenant before provider I/O.
- Groundwork envelope version is the authoritative optimistic revision for mutable owner documents.
- Public `ConcurrencyStamp` values are opaque encodings of that revision; callers never construct valid successor stamps.
- Natural uniqueness is expressed with native scoped unique indexes or deterministic scoped document IDs, never collation or preflight alone.
- Relationship records are scalar physical entities. User/role owner registries contain their child IDs so a unit of work can load and delete the complete dependent set without querying.
- Document-kind strings and field identities are stable wire identifiers; type names may evolve without renaming them.
- All text fields participating in SQL Server indexes have explicit finite lengths within the 1,700-byte compound-key budget.

## Identity User

The authoritative account used by both framework managers and Elsa IAM adapters.

| Field | Rules |
|---|---|
| `Id` | Stable nonblank string; logical identity within scope. |
| `TenantId` | Required; must equal the immutable session scope. |
| `UserName` / `NormalizedUserName` | Display and framework-normalized forms; normalized value required for sign-in-capable users and unique within tenant. |
| `Email` / `NormalizedEmail` | Optional; normalized form required when email is present. Multiple users may share it unless unique-email policy is active. |
| `EmailConfirmed` | Boolean. |
| `PasswordHash` | Optional; never indexed or logged. |
| `SecurityStamp` | Opaque security invalidation value; rotates according to Identity manager behavior. |
| `DisplayName` | Optional Elsa presentation field. |
| `PhoneNumber` / `PhoneNumberConfirmed` | Optional contact and confirmation state. |
| `TwoFactorEnabled` | Boolean. |
| `LockoutEnd` / `LockoutEnabled` / `AccessFailedCount` | Conditional lockout state; failure-count transitions are CAS-protected. |
| `Status` / `OwnerType` | Provider-neutral Elsa IAM semantics preserved rather than projected to constants. |
| `ClaimIds` | Ordered/deterministic snapshot of user-claim child IDs. |
| `LoginIds` | Snapshot of external-login child IDs. |
| `RoleLinkIds` | Snapshot of user-role link IDs. |
| `TokenIds` | Snapshot of authentication-token child IDs. |
| `TenantMembershipIds` | Snapshot of separately owned Elsa tenant-membership child IDs. |
| Envelope version | Not stored as user-authored data; mapped to/from public `ConcurrencyStamp`. |

### User Indexes

- unique `(Scope, NormalizedUserName)`;
- non-unique `(Scope, NormalizedEmail, Id)` with deterministic `Id` tie-break;
- exact physical identity `(Scope, Id)`.

## Identity Role

The authoritative tenant-local role shared by framework and Elsa role managers.

| Field | Rules |
|---|---|
| `Id` | Stable nonblank string within scope. |
| `TenantId` | Required; equals session scope. |
| `Name` / `NormalizedName` | Required display and normalized forms; normalized value unique within tenant. |
| `Description` | Optional Elsa description, preserved. |
| `Permissions` | Deterministic case-insensitive permission set used by Elsa claims projection. |
| `System` | Preserves system-role semantics. |
| `ClaimIds` | Snapshot of role-claim child IDs. |
| `UserLinkIds` | Snapshot of user-role link IDs so role deletion can remove memberships atomically. |
| Envelope version | Mapped to/from public role `ConcurrencyStamp`. |

### Role Indexes

- unique `(Scope, NormalizedName)`;
- exact physical identity `(Scope, Id)`.

## User Claim

| Field | Rules |
|---|---|
| `Id` | Generated child ID; duplicate equal claims are allowed. |
| `TenantId` | Required scope. |
| `UserId` | Required owner. |
| `Type` / `Value` | Required claim pair; finite indexed lengths. |

Indexes: `(Scope, UserId, Id)` for listing and `(Scope, Type, Value, UserId, Id)` for users-for-claim.

## Role Claim

| Field | Rules |
|---|---|
| `Id` | Generated child ID; duplicate equal claims are allowed. |
| `TenantId` | Required scope. |
| `RoleId` | Required owner. |
| `Type` / `Value` | Required claim pair. |

Index: `(Scope, RoleId, Id)`.

## External Login

| Field | Rules |
|---|---|
| `Id` | Deterministic collision-safe identity derived from scope, provider, and provider key. |
| `TenantId` | Required scope. |
| `UserId` | Required authoritative user. |
| `LoginProvider` / `ProviderKey` / `ProviderDisplayName` | Framework external-login values; provider/key cannot be blank. |
| `LinkedAt` / `LinkPolicy` | Existing Elsa external-identity semantics preserved. |

Indexes:

- `(Scope, UserId, LoginProvider, ProviderKey)`.

Subject lookup uses the deterministic primary ID derived from `(Scope, LoginProvider, ProviderKey)`.

## User Role Link

| Field | Rules |
|---|---|
| `Id` | Deterministic identity derived from scope, user ID, and role ID. |
| `TenantId` | Required scope. |
| `UserId` / `RoleId` | Required owners in the same scope. |

Indexes:

- unique `(Scope, UserId, RoleId)`;
- `(Scope, RoleId, UserId)` for users-in-role.

The link ID is registered on both owners in the same unit of work.

## User Authentication Token

| Field | Rules |
|---|---|
| `Id` | Deterministic identity derived from scope, user ID, login provider, and token name. |
| `TenantId` | Required scope. |
| `UserId` | Required owner. |
| `LoginProvider` / `Name` | Required token identity. |
| `Value` | Optional secret value; never indexed or logged. |

Token lookup uses the deterministic primary ID derived from `(Scope, UserId, LoginProvider, Name)`; no
secondary token index is declared.

Authenticator keys and recovery-code payloads use the framework's conventional provider/name tokens and therefore share this unit.

## Tenant Membership

An Elsa-owned projection linked to the authoritative user without duplicating credentials or identity authority.

| Field | Rules |
|---|---|
| `Id` | Deterministic identity derived from scope and user ID. |
| `TenantId` / `UserId` | Unique pair and same-scope authority link. |
| `RoleIds` | Deterministic set of authoritative role IDs. |
| `DirectPermissions` | Deterministic case-insensitive set. |
| Envelope version | Revision-aware Elsa mutation evidence. |

Membership lookup uses the deterministic primary ID derived from `(Scope, UserId)`; no secondary membership
index is declared. Role lookup stays on User Role Link.

## Email Reservation

A conditional uniqueness record used only when the host enables unique email.

| Field | Rules |
|---|---|
| `Id` | Deterministic identity derived from scope and normalized email. |
| `TenantId` / `NormalizedEmail` | Required unique key. |
| `UserId` | Current owner. |

The reservation is created/deleted in the same unit of work as the user create/email change/delete. When unique email is disabled, no reservation is created and email lookup fails closed if more than one match exists.

## Revision Carrier

The framework-facing user/role object carries:

- public `ConcurrencyStamp`: opaque encoded envelope version;
- provider-private loaded identity/scope evidence needed to reject a stamp replayed for another object;
- pending relationship intent only for the duration of one scoped store interaction if required by a manager call sequence.

No Groundwork type appears on the public user or role model.

## State Transitions

### User / Role Create

1. Validate immutable session scope and normalized fields.
2. Preflight the bounded unique route for friendly diagnostics.
3. Stage create-only owner document and optional email reservation.
4. Commit once.
5. On uncertain acknowledgement, reload exact identity and compare the canonical request fingerprint.
6. Return success with version-backed concurrency stamp, a precise duplicate result, or a bounded uncertain outcome.

### User / Role Update

1. Decode and bind the caller's concurrency stamp to expected owner version.
2. Validate scope and uniqueness changes.
3. Stage conditional owner update plus old/new reservation changes.
4. Commit; on success rotate the public stamp.
5. Map a stale version to the framework concurrency failure.

### Relationship Add / Remove / Replace

1. Resolve bounded external identities (for example normalized role name) before the transaction.
2. Begin one unit of work and reload every owner by exact ID.
3. Verify scope, normalized identity evidence, and expected owner versions.
4. Stage create/delete/update of scalar link documents and both owner registries.
5. Commit and refresh affected in-memory owner stamps.
6. Reconcile lost acknowledgement by deterministic IDs and registry membership.

### User / Role Delete

1. Begin one unit of work with expected owner version.
2. Load the owner and every child referenced by its registry.
3. For cross-owner links, load and conditionally update the other owner registry.
4. Delete dependents, reservation, and owner atomically.
5. A concurrent relationship mutation can win only by advancing an owner version; the loser retries through manager policy or returns concurrency failure, never an orphan.

### Lockout Failure / Reset

- Increment/reset `AccessFailedCount` and set/clear `LockoutEnd` with expected version.
- A CAS conflict reloads the current user and performs a bounded retry only while the same logical request remains valid.
- The operation updates the caller's concurrency stamp after each committed transition.

### Concurrent Seeder

1. Acquire explicit privileged access for the configured tenant and purpose.
2. Create-only the normalized admin role; reload the exact winner on conflict.
3. Create the user through `UserManager`; reload the exact winner on duplicate.
4. Add wildcard/catalog permissions, role link, and membership idempotently through conditional transitions.
5. Validate the final canonical state before reporting success.

## Invariants

- A user/role/external login exists in one authority only.
- A link is present in every required owner registry or absent everywhere after a committed decision.
- No ordinary operation can load, infer, or mutate another scope.
- No stale stamp overwrites a successor.
- No provider-specific collation determines logical normalization.
- No query materializes more than its declared maximum before predicate/order/limit execution.
- Runtime startup never creates or changes the identity schema.
