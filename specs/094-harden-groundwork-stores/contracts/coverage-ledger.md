# Contract: Persistence Coverage Ledger

The executable ledger is [`../coverage-ledger.json`](../coverage-ledger.json). This document defines its governance and provides the review view. The original 32-row denominator is frozen to the merge-base of branch `codex/645-groundwork-store-hardening`; implementation work may advance or split rows but may not silently remove them. On 2026-07-25 the program owner ratified two additive diagnostics rows, bringing the current denominator to 34 without weakening the original 32-row floor.

## Completion rule

A row is `ready` only when all of the following are true:

1. Exactly one durable outcome and authority are recorded.
2. The storage unit has an approved scoped/global classification and a separate ordinary/privileged operation access policy.
3. Every scale-bearing query has a finite bound, deterministic order, compiled active route, and four-provider native-plan evidence.
4. Every concurrency and failure-window scenario passes with independent clients and durable reopen/restart.
5. SQLite, SQL Server, PostgreSQL, and MongoDB produce equivalent public outcomes on real storage.
6. Existing behavioral objectives frozen at the baseline remain covered.
7. The mapped #646 workload has a reproducible passing verdict, or a reviewed `not-hot-path` rationale applies.
8. Core dependency and EF-surface ratchets pass.

Each `behavioralBaseline` path means every discovered test case in that file at `baselineRef`, not merely the continued existence of the file. The validator compares exact test-case identities between the immutable baseline commit and the candidate tree; a removed/renamed case requires a test-removal approval entry.

Missing mandatory-provider evidence keeps the row incomplete. A memory-backed fixture is never provider or restart evidence.

Provider evidence is a set of structured scenario records, not a list of free-form links. Every
record identifies its ledger row, provider and provider version, topology, manifest fingerprint,
execution path, independent-client count, result hash, outcome, and durable evidence location. A
record that exercises a query shape also carries provider-native plan evidence; concurrency,
failure-window, and restart records name the exact obligation they cover.

The ledger's `groundworkVersion` must equal every pinned Groundwork package and `Groundwork.Tool`
version. Provider identity, substrate/topology, scenario identity, and provider-driver execution path
use the closed provider-conformance catalog. SQLite evidence is file-backed with distinct connections;
SQL Server and PostgreSQL use real server containers; MongoDB uses a transaction-capable replica set
or sharded cluster. `in-memory`, an arbitrary topology label, or an unrecognized execution route is
invalid.

Each scenario references a deterministic checked-in artifact path under `evidence/` plus its SHA-256
digest. The artifact payload must exactly reproduce the structured ledger record, excluding only the
artifact digest fields. Query scenarios additionally reference a digest-verified provider-native plan
artifact. Missing, relocated, modified, or payload-mismatched artifacts fail the gate. Scenario IDs
are limited to `ordinary-round-trip` and the query, concurrency, failure, and restart obligations
declared by that row.

For an `evidence-complete`, `performance-complete`, or `ready` row, all four providers must carry
the same scenario identifiers, every declared query/concurrency/failure/restart obligation must be
covered, every record must pass, and each scenario's public-result hash must agree across providers.
Concurrency, failure-window, and restart evidence requires at least two independent clients. An
arbitrary non-empty string, a memory-backed execution, or a provider record filed under a different
provider cannot satisfy the gate.

## PR-time evidence contract

The `Groundwork fast gates` job in `.github/workflows/ci.yml` is the container-free pull-request authority for this ledger. It checks out the candidate merge commit, restores `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`, and runs the entire architecture test project in Release mode. Running the whole project intentionally avoids a test-name filter that could silently stop selecting a renamed or newly added ratchet.

The job fails when any of these evidence classes regress:

- JSON Schema conformance, the exact current 34-row denominator, status transitions, or evidence completeness;
- immutable `baselineRef` test-case continuity or its exact architect-approval ledger;
- discovered contract, Groundwork registration, manifest/storage-unit, or #644/#660 authority reconciliation;
- provider-neutral core dependency boundaries or the reviewed shrink-only EF surface.

The job is container-free and does not create provider or restart evidence. SQLite, SQL Server, PostgreSQL, MongoDB, failure/restart, native-plan, temporary-oracle, and readiness claims remain owned by the fail-closed integration lanes; a row cannot advance to `evidence-complete` or `ready` merely because this fast gate passes.

## Allowed outcomes and states

**Durable outcomes**: `ordinary-document`, `operational-store`, `specialized-primitive`, `external-authority-adapter`, `explicit-exclusion`.

**States**: `missing`, `planned`, `implemented`, `evidence-complete`, `performance-complete`, `ready`, `externally-blocked`, `excluded`.

- `external-authority-adapter` and `externally-blocked` require a linked issue.
- `explicit-exclusion` and `excluded` require an architect-approved linked decision.
- `ready` cannot be set manually when the validator finds missing evidence.
- A regression moves a row out of `ready`.

## Baseline review view

| Row | Contract / state | Target outcome | Target scope | Authority | Initial gap | Performance mapping |
|---|---|---|---|---|---|---|
| `runtime-activity-execution-inspection` | `IActivityExecutionInspectionStore`, `IActivityExecutionInspectionWriter` | Ordinary document | Scoped | #645 | Provider/restart/OCC evidence | Not hot path |
| `runtime-activity-execution-state` | `IActivityExecutionStateStore` | Ordinary document | Scoped | #645 | Provider/restart/OCC evidence | Checkpoint bundle representative |
| `runtime-bookmark-state` | `IBookmarkStateStore` | Ordinary document + bounded route | Scoped | #645 | Client filtering / matrix | `bookmark-lookup` |
| `runtime-durable-timer` | `IDurableTimerStore` | Operational store | Scoped | #645 | Conditional due/advance path | `due-timer-selection` |
| `runtime-durable-value-state` | `IDurableValueStateStore` | Ordinary document | Scoped | #645 | Provider/restart/OCC evidence | Checkpoint bundle representative |
| `runtime-execution-liveness` | `IExecutionLivenessStateStore`, `IRuntimeExecutionOwnershipService`, `IRuntimeRecoveryScanner` | Operational store | Scoped | #645 | Atomic fence/recovery query | `recovery-scan` |
| `runtime-incident-state` | `IIncidentStateStore` | Operational store | Scoped | #645 | Create-once/OCC evidence | Recovery representative |
| `runtime-recurring-trigger-schedule` | `IRecurringTriggerScheduleStore` | Operational store | Scoped | #645 | Client filtering / schedule race | `recurring-schedule-selection` |
| `runtime-checkpoint-commit` | `IRuntimeCheckpointCommitStore` | Specialized primitive | Scoped | #645 | Fence TOCTOU / process-local locks | `checkpoint-commit` |
| `runtime-diagnostics-settings` | `IRuntimeDiagnosticsSettingsStore` | External authority adapter | Classified by #660 | #660 | Draft PR not landed | Diagnostics workload owned by #660/#646 |
| `runtime-post-commit-outbox` | `IRuntimePostCommitOutboxStore`, `IPostCommitOutboxLookupStore` | Operational store | Scoped | #645 | No atomic claim/stale-ack token | `outbox-drain` |
| `runtime-scheduler-state` | `ISchedulerStateStore` | Operational store | Scoped | #645 | Provider/restart/OCC evidence | Recovery representative |
| `runtime-executable-source-reference` | `IWorkflowExecutableSourceReferenceStore` | Ordinary document + bounded route | Scoped | #645 | Client filtering / matrix | Trigger/bookmark representative |
| `runtime-workflow-executable` | `IWorkflowExecutableStore` | Ordinary document | Scoped | #645 | Provider/restart/OCC evidence | Checkpoint representative |
| `runtime-workflow-execution-state` | `IWorkflowExecutionStateStore` | Ordinary document + bounded routes | Scoped | #645 | Partial relational-only paging | `recovery-scan` |
| `runtime-workflow-hold-state` | `IWorkflowHoldStateStore` | Operational store | Scoped | #645 | Conditional transition evidence | Recovery representative |
| `runtime-scheduler-poison` | `IWorkflowSchedulerPoisonStore` | Operational store | Scoped | #645 | Groundwork implementation missing | `queue-drain` representative |
| `runtime-scheduler-work-queue` | `IWorkflowSchedulerWorkQueue` | Operational store | Scoped | #645 | Claim/ack races and client query | `queue-drain` |
| `runtime-trigger-binding` | `IWorkflowTriggerBindingStore` | Ordinary document + bounded routes | Scoped | #645 | Client filtering / projection race | `trigger-binding-stimulus-lookup` |
| `runtime-publication-projection-state` | Trigger/schedule internal projection state | Operational store | Scoped | #645 | Shared state authority / conditional activation | Trigger and recurring representatives |
| `iam-user` | `IUserStore` | External authority adapter | Scoped | #644 | Must retire duplicate authority | `iam-normalized-lookup-update` |
| `iam-role` | `IRoleStore` | External authority adapter | Scoped | #644 | Must retire duplicate authority | `iam-normalized-lookup-update` |
| `iam-application` | `IApplicationStore` | Ordinary document | Scoped | #645 | Groundwork implementation missing | IAM representative |
| `iam-credential` | `ICredentialStore` | Ordinary document | Scoped | #645 | Groundwork implementation missing | IAM representative |
| `iam-external-identity` | `IExternalIdentityStore` | External authority adapter | Scoped | #644 | Must retire duplicate authority | IAM representative |
| `iam-claim-mapping` | `IClaimMappingStore` | Ordinary document + bounded route | Scoped | #645 | Groundwork implementation missing | IAM representative |
| `iam-provider-configuration-tenant` | Tenant path of `IProviderConfigurationStore` | Ordinary document | Scoped | #645 | Groundwork implementation missing | IAM representative |
| `iam-provider-configuration-global` | Global path of `IProviderConfigurationStore` | Ordinary document | Explicit global | #645 | Implementation missing; writes require privileged access | Not hot path |
| `iam-tenant-membership` | `ITenantMembershipStore` | Ordinary document | Scoped | #645 (depends on #644 seam) | Last-write-wins / matrix | IAM representative |
| `secrets-repository` | `ISecretRepository` | Ordinary document + bounded route | Scoped | #645 | Read/save `TryAdd`, unbounded list, no revision | `secret-create-read-list` |
| `distributed-execution-placement` | `IExecutionPlacementStore` | Specialized primitive | Scoped | #645 | Unbounded list / capability overclaim | `placement-takeover` |
| `distributed-command-transport` | `IExecutionCommandTransport` | Specialized primitive | Scoped | #645 | Scan-max sequence / lease CAS gaps | `command-send-lease-ack` |

## Authority boundaries

- #644 is the only authoritative document owner for framework users, roles, and external logins. #645 adapters may not persist parallel copies.
- #660 owns runtime diagnostic-settings persistence. #645 carries the ledger row so it cannot disappear from the zero-EF denominator.
- #646 owns benchmark execution and verdicts. #645 owns workload inputs, correctness baselines, and verdict consumption.
- Publication projection state is an internal state machine jointly exercised by trigger-binding and recurring-schedule public contracts; it remains an explicit row because a public-contract-only scan would miss it.

## Validator obligations

The implementation-phase validator must fail when:

- a baseline row is absent or duplicated;
- a discovered durable contract or registered memory-only store has no row;
- a Groundwork implementation registration has no manifest/storage unit;
- a row claims `ready` without every mandatory provider and restart scenario;
- provider evidence is unstructured, incomplete, misfiled, non-passing, single-client where
  independent clients are required, or produces non-equivalent public-result hashes;
- a scale-bearing query lacks a finite bound or native evidence;
- an explicitly global row lacks its scope reason, or a privileged access policy lacks its authorization reason and audit scenario;
- a capability has no active-path scenario;
- a #644/#660 authority row points to a local parallel document;
- a #644/#660 composition link changes its reviewed relationship (`#644` adapter-only, `#660` linked-source-evidence);
- the EF surface grows relative to the baseline commit;
- a baseline test objective disappears without a recorded architect approval.
- a logic-bearing persistence registration has an undocumented non-scoped lifetime or a lifetime test observes scope/mutable-state leakage between request scopes.
