# Contract: Identity Storage Manifest And Routes

## Manifest Identity

- Stable owner: `elsa.identity`.
- The new manifest version replaces the greenfield legacy four-document shape; no compatibility migration is supplied.
- Every unit is `TenancyPolicy.Scoped`, optimistic where mutable, and JSON serialized. The seven query-bearing kinds use dedicated entity tables; primary-ID-only user tokens, tenant memberships, and name/email reservations use dedicated document tables.
- Host name transformation runs before provider rendering/normalization. Runtime and CLI consume the identical resolved snapshot/fingerprint.

## Physical Units

| Stable unit identity | Record | Identity / uniqueness | Required routes |
|---|---|---|---|
| `identityUser` | Identity User | exact `(scope,id)`; unique `(scope,normalizedUserName)` | by ID; by normalized name; by normalized email take 2 |
| `identityRole` | Identity Role | exact `(scope,id)`; unique `(scope,normalizedName)` | by ID; by normalized name |
| `identityUserClaim` | User Claim | exact generated ID | list by user; users by claim type/value |
| `identityRoleClaim` | Role Claim | exact generated ID | list by role |
| `identityExternalLogin` | External Login | deterministic ID from `(scope,provider,key)` | exact by ID; list by user |
| `identityUserRole` | User Role Link | deterministic ID; unique `(scope,userId,roleId)` | list/check by user; users by role |
| `identityUserToken` | Authentication Token | deterministic ID from `(scope,userId,provider,name)` | exact by ID; owner registry supplies delete evidence |
| `identityTenantMembership` | Tenant Membership | deterministic ID from `(scope,userId)` | exact by ID; owner registry supplies delete evidence |
| `identityUserNameReservation` | User-name Reservation | deterministic ID from `(scope,normalizedUserName)` | exact by ID |
| `identityEmailReservation` | Email Reservation | deterministic ID; unique `(scope,normalizedEmail)` | exact reservation |
| `identityRoleNameReservation` | Role-name Reservation | deterministic ID from `(scope,normalizedRoleName)` | exact by ID |
| `identityMutationReceipt` | Mutation Receipt | deterministic ID from operation/request fingerprint | exact by ID; oldest expired page |

## Route Rules

- Every route has a stable identity, finite maximum, explicit result operation, and deterministic final ID tie-break.
  On the current `preview.103` family, every non-unique Identity route uses cursor paging and ends its
  physical index with the envelope `id_lookup_key`. The fixed-width lookup key is provider-applied
  ordering evidence, not a caller predicate, and keeps the widest SQL Server compound index below
  1,700 bytes; offset paging's `id_comparison_key` would exceed that provider limit for the declared
  400-code-unit lookup projections.
- Runtime predicates include scope even though physical storage also partitions identity; defense in depth must not become post-filtering.
- Declared username, role, claim, role-link, and login-by-user routes use exact equality only.
- Email route has maximum 2 so ambiguity is detected without loading the tenant catalog.
- Claim and role membership routes use bounded deterministic pages internally. A framework API returning a list may iterate a finite continuation protocol but may not issue an unbounded kind scan.
- All route fields are physical columns/fields. No provider extracts predicates from opaque JSON during request execution.
- SQL Server string/binary lengths and compound index byte budgets are checked at composition time.

### Issue #1106 Cursor Amendment

- The twelve scale-bearing list/relationship routes use cursor paging and terminate their physical
  indexes with the provider identity lookup-key column.
- An exhaustive framework/Elsa list result is assembled only by following the opaque continuation
  returned by that same admitted route. The traversal has explicit page-count and forward-progress
  guards and preserves cancellation between and during pages.
- Repeated, malformed, cross-route, cross-scope, or non-advancing continuations fail closed.
- Exact/deterministic-ID loads, normalized user-name/email/role first-page lookups, and the ordered
  64-record expired mutation-receipt cleanup do not become cursor traversals.
- The amendment changes the manifest/schema/composition identity and invalidates active use of
  every earlier provider-evidence generation. Earlier generations remain immutable historical
  records.

## Required Capabilities

- physical entity tables;
- scoped exact identity;
- typed compound unique indexes;
- bounded query documents/first/any/count;
- create-only and expected-version save/delete;
- atomic cross-unit unit of work;
- transaction-capable provider topology;
- schema plan/validate/status/apply through the selected manifest source.

Readiness rejects a selected path missing any required capability. MongoDB additionally requires an explicitly named replica set, writable primary, and successful transaction probe.

## Atomic Boundaries

| Transition | Unit-of-work participants |
|---|---|
| Create/update user | user + old/new optional email reservation |
| Add/remove/replace user claim | user + affected user-claim records |
| Add/remove login | user + external-login record |
| Add/remove role | user + role + user-role record |
| Set/remove token | user + user-token record |
| Add/update/delete tenant membership | user + membership record when coupled to user authority |
| Delete user | user + reservation + every registered user claim/login/role link/token/membership + linked role registry updates |
| Add/remove role claim | role + affected role-claim records |
| Delete role | role + every registered role claim/user link + linked user registry updates |

Every participant is loaded by exact ID inside the unit of work. Pre-transaction route results are treated only as candidates and are revalidated after exact transactional load.

## Native Evidence

The #1106 native-plan denominator is exactly twelve physical route/index definitions: normalized
user name, normalized email, normalized role name, tenant role listing, claim mappings by provider,
claims by user, users by claim, claims by role, roles by user, users by role, external logins by
user, and expired mutation receipts. Eight are exhaustive continuation identities. The three
normalized lookups and the ordered 64-record receipt cleanup remain one-request consumers but still
require cursor-declared physical indexes so their provider order uses the lookup-key tail.
External-login subject lookup, token lookup, and membership lookup use deterministic primary IDs and
therefore do not declare secondary bounded routes.

For each provider, evidence records:

- package/provider/topology identity;
- manifest and resolved-name fingerprint;
- physical table/index identities;
- native plan or winning-plan evidence for all twelve cursor-declared physical routes;
- candidate/materialized counts proving scope/predicate/order/limit execute before materialization;
- unique-race winner/conflict digest;
- dispose/reopen and process-restart digest;
- sanitized diagnostics with no connection secret or credential value.

Partial provider evidence cannot advance #1106/#646/spec 094 authority rows to ready.
