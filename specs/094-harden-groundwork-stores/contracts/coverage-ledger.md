# Contract: Persistence Coverage Ledger

The executable ledger is [`../coverage-ledger.json`](../coverage-ledger.json). This document defines its governance and provides the review view. The initial denominator is frozen to the merge-base of branch `codex/645-groundwork-store-hardening`; implementation work may advance or split rows but may not silently remove them.

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
| `runtime-post-commit-outbox` | `IRuntimePostCommitOutboxStore` | Operational store | Scoped | #645 | No atomic claim/stale-ack token | `outbox-drain` |
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
- a scale-bearing query lacks a finite bound or native evidence;
- an explicitly global row lacks its scope reason, or a privileged access policy lacks its authorization reason and audit scenario;
- a capability has no active-path scenario;
- a #644/#660 authority row points to a local parallel document;
- the EF surface grows relative to the baseline commit;
- a baseline test objective disappears without a recorded architect approval.
- a logic-bearing persistence registration has an undocumented non-scoped lifetime or a lifetime test observes scope/mutable-state leakage between request scopes.
