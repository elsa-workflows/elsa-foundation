# Contract: Groundwork Provider Conformance

One black-box suite executes the same public Elsa scenarios against SQLite, SQL Server, PostgreSQL, and MongoDB. Provider drivers supply mechanics only; they may not change expected domain outcomes.

## Provider driver contract

Each driver must provide:

- exact provider identity/version and topology description;
- production-shaped host construction from the selected storage composition;
- schema `validate`, `plan`, `status`, and authorized `apply` support;
- independent store clients that do not share adapter locks or in-memory state;
- deterministic database reset for test isolation;
- disposal/reopen against the same durable database;
- process restart where the scenario claims process durability;
- named failure injection before, during, and after durable decisions;
- cancellation and transaction/resource cleanup verification;
- provider-native route/plan/command evidence;
- sanitized diagnostics with no connection values or tenant IDs in metric labels.

Required substrates:

| Provider | Mandatory substrate |
|---|---|
| SQLite | File-backed database; distinct connections; process reopen of the same file. |
| SQL Server | Real container/server using the production provider. |
| PostgreSQL | Real container/server using the production provider. |
| MongoDB | Replica set or sharded transaction-capable deployment for multi-document scenarios. |

Memory-backed stores may run fast unit tests but cannot satisfy any row in this matrix.

## Temporary EF oracle

Where an EF implementation exists at the baseline, extract the observable scenario once and run it against both the EF oracle and the Groundwork adapter until the owning zero-EF exit authorizes deletion. Compare public results, conflict classification, ordering/count/null semantics, and final durable state—not provider mechanism or physical schema. The oracle lane may use only its already-supported provider; it does not justify new EF providers, migrations, packages, or behavior. Retain the result digest/evidence after the EF implementation is deleted.

## Shared scenario result

Every scenario returns provider-independent observations:

- public return values and domain failure classification;
- final durable records/state transitions after reopen;
- winner/loser counts for concurrency races;
- deterministic ordered result identities and continuation boundaries;
- idempotency/replay outcome;
- emitted capability/readiness outcome;
- bounded-execution evidence classification;
- stable result digest.

Raw provider exceptions must be translated before this boundary. Expected outcomes never mention SQL error numbers, MongoDB exception types, provider collation, or native row shapes.

## Mandatory scenario families

### Composition and schema

| Scenario | Required outcome |
|---|---|
| All selected families | Runtime, IAM, secrets, and distributed stores initialize, execute a public round trip, dispose, and reopen together. |
| Missing manifest source | Startup fails before serving work and names the selected feature. |
| Duplicate storage unit | Startup fails and names both contributors. |
| Missing active route/capability | Startup fails rather than enabling a fallback. |
| Schema drift | Live validation reports the drift without mutating targets. |
| CLI lifecycle | Offline/live validate, plan/status, and safe/authorized apply use the same target fingerprint as runtime. |

### Scope and sessions

| Scenario | Required outcome |
|---|---|
| Equal IDs in two scopes | Load/query/update/delete affects only the current scope. |
| Wrong-scope point read | Returns the same non-disclosing outcome as missing-in-current-scope. |
| Wrong-scope mutation | Changes no data and does not disclose another scope's record. |
| Ordinary global/cross-scope attempt | Rejected before provider data is returned or changed. |
| Privileged operation | Requires named capability/purpose and records acquisition/outcome without tenant metric labels. |
| Cancel/fail/dispose/reuse | No scope or transaction state leaks to the next client. |
| Mixed-scope UoW | Rejected before partial writes. |

### Ordinary document concurrency

| Scenario | Required outcome |
|---|---|
| Concurrent create-only | Exactly one winner; equivalent retry is idempotent only where the public contract says so. |
| Stale update | Stable domain conflict; current record unchanged. |
| Stale delete | Stable domain conflict; current record remains. |
| Reopen | Identity, revision, uniqueness, and scope survive adapter/process recreation. |

### Ownership and checkpoint

| Scenario | Required outcome |
|---|---|
| Concurrent ownership acquisition | One winner and unique strictly increasing fencing tokens. |
| Conditional heartbeat/release | A superseded owner cannot extend or release the current owner. |
| Stale checkpoint fence | Entire checkpoint bundle is rejected atomically. |
| Concurrent same idempotency key, same input | One durable outcome and equivalent replay result. |
| Same idempotency key, conflicting input | Stable conflict and no changed prior outcome. |
| Failure before/during/after commit | No partial bundle; retry/reopen converges to one allowed outcome. |

### Queue, outbox, timer, recurring, incident, and poison transitions

| Scenario | Required outcome |
|---|---|
| Concurrent bounded claim | One current owner per logical item. |
| Visibility/claim expiry | A successor may reclaim after expiry. |
| Stale acknowledgement | Cannot complete or delete the successor's item. |
| Retry/attempt advancement | Expected revision and attempt state advance once. |
| Due selection | Finite deterministic order; predicates/limit execute at storage boundary. |
| Create-once incident/timer/poison | One winner and deterministic existing-winner outcome. |
| Restart | Pending/retry/poison relationships survive and recover without loss. |

### IAM and secrets

| Scenario | Required outcome |
|---|---|
| Tenant-local normalized uniqueness | Same normalized value may exist in different tenants but not twice in one tenant. |
| #644 authority adaptation | User/role/external-login operations update/read the authoritative documents only. |
| Missing IAM store families | Application, credential, claim mapping, provider configuration, and membership scenarios have durable outcomes. |
| Secret concurrent `TryAdd` | Exactly one add succeeds; winner is not overwritten. |
| IAM/secret stale mutation | Stable revision conflict; current value unchanged. |
| Bounded IAM/secret list/lookup | Stable semantics and native bounded evidence. |
| Reopen | Uniqueness, revision, scope, and secret versions remain intact. |

### Distributed placement and command transport

| Scenario | Required outcome |
|---|---|
| Placement claim/renew/takeover | Exactly one current lease and monotonic version. |
| Stale placement release | Does not remove successor lease. |
| Concurrent command send | Deterministic identities and unique declared per-execution sequence. |
| Bounded lease | Returns finite items in declared order and atomically owns them. |
| Visibility expiry and re-lease | Successor receives required unacknowledged work. |
| Stale command acknowledgement | Cannot delete successor-owned command. |
| Failure and restart | Commands are redelivered at least once with only one successful acknowledgement. |
| Placement versus fencing | A placement winner with a stale execution fence cannot commit. |

### Bounded routes

Every scale-bearing query runs with a dataset larger than its requested window and proves:

1. scope is part of the provider-bound execution;
2. predicate/null/missing semantics match the shared oracle;
3. ordering includes a stable tie-breaker;
4. continuation/count/distinct behavior is deterministic;
5. the provider returns/materializes no more than the declared bound for page operations;
6. native evidence identifies the compiled route and contains no fallback collection scan attributable to application evaluation.

## Capability rule

A provider capability is available only when the selected composition resolves an active route/transition and all its mandatory scenarios pass on that provider/topology. Options, package presence, or a declared capability flag cannot make it available. An unavailable required capability is a startup error with a stable diagnostic identifying feature, storage unit, capability, and provider.

## CI lanes

- Fast lane: unit tests, ledger/schema validator, architecture ratchets, SQLite contract smoke.
- Provider lane: full SQL Server, PostgreSQL, MongoDB, and file-backed SQLite black-box matrix.
- Failure/restart lane: deterministic races, failure windows, process restart, and topology rejection.
- Plan lane: schema CLI and provider-native bounded-route evidence.
- Readiness lane: consumes provider evidence plus #646 verdicts and is the only lane allowed to mark ledger rows `ready`.
- Temporary-oracle lane: runs shared observable scenarios against EF and Groundwork wherever both baseline implementations exist; the EF-surface ratchet prevents expansion.
