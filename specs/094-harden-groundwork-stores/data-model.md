# Data Model: Groundwork Store Hardening Evidence

This work unit does not introduce a provider-owned domain model into Elsa core. It defines the durable declarations and evidence records required to prove each existing provider-neutral contract across four Groundwork providers.

## 1. Persistence Coverage Entry

The coverage ledger has one row per durable public contract or inseparable internal durable state machine.

| Field | Meaning |
|---|---|
| `EntryId` | Stable kebab-case identity; never derived from a CLR namespace. |
| `Contract` | Provider-neutral public contract, or named internal state when no public contract owns it. |
| `StoreFamily` | Runtime, IAM, Secrets, Distributed Runtime, or External Owner. |
| `DurableOutcome` | Ordinary Document, Operational Store, Specialized Primitive, External Authority Adapter, or Explicit Exclusion. |
| `Authority` | Exactly one accountable workstream/issue and implementation owner. |
| `ScopeClassification` | Tenant Scoped or Explicitly Global; external-owner rows defer classification to their authority. |
| `ScopeReason` | Required for explicitly global rows; absent for ordinary scoped rows. |
| `AccessPolicy` | Ordinary, Privileged, or ordinary-read/privileged-write. This is separate from storage-unit scope. |
| `AccessReason` | Required whenever privileged access is allowed. |
| `QueryShapes` | Stable query identities, predicates, sort, continuation, finite maximum, and result kind. |
| `ConcurrencySemantics` | Create-only, expected revision, fencing, claim/lease, idempotency, or none with justification. |
| `AtomicBoundary` | Documents/transitions that commit together. |
| `FailureWindows` | Named interruption points and allowed outcomes. |
| `ProviderEvidence` | Required SQLite, SQL Server, PostgreSQL, and MongoDB scenario references. |
| `PerformanceWorkload` | #646 workload identity, representative workload, or `not-hot-path` rationale. |
| `BehavioralBaseline` | Existing test objectives frozen at the baseline commit. |
| `Status` | Missing, Planned, Implemented, Evidence Complete, Performance Complete, Ready, Externally Blocked, or Excluded. |

The ledger root also records one `GroundworkVersion`; it must match every pinned Groundwork package
and `Groundwork.Tool` generation consumed by the work unit.

### Validation

- Every baseline entry has exactly one durable outcome and one authority.
- An Explicit Exclusion names its accepting issue and reason; silence is never exclusion.
- Explicitly global entries require a scope reason; privileged access policies separately require an authorization/audit reason.
- A scale-bearing query requires a finite maximum and deterministic ordering.
- `Ready` requires complete provider, restart, failure, bounded-execution, behavioral-baseline, and performance evidence.
- User, role, and external-login authority can point only to #644; diagnostic settings can point only to #660.

`BehavioralBaseline` stores source paths; combined with the immutable `baselineRef`, each path denotes every test-case identity discovered in that file at that commit. Validation compares test identities, not only file existence.

## 2. Durable Outcome

| Kind | Use |
|---|---|
| `OrdinaryDocument` | Point-oriented CRUD and bounded queries whose concurrency is satisfied by create-only/expected-version document operations. |
| `OperationalStore` | Queue, outbox, timer, schedule, incident, poison, or similar lifecycle with public claim/retry/completion behavior. |
| `SpecializedPrimitive` | Fencing, checkpoint admission, placement takeover, command visibility/acknowledgement, or another transition that must be one provider-atomic decision. |
| `ExternalAuthorityAdapter` | Adapter to a document authority owned by #644 or a sibling workstream; no second document is created. |
| `ExplicitExclusion` | Durable behavior intentionally owned elsewhere and accepted through a linked decision. |

`DurableOutcome` is a ledger classification, not a new Elsa core enum.

## 3. Storage Scope Classification

| Kind | Session rule |
|---|---|
| `TenantScoped` | The adapter must acquire `DocumentStoreAccess.Scoped(StorageScope)` and the manifest declares scoped tenancy. |
| `ExplicitlyGlobal` | The manifest declares global tenancy and the caller must acquire `DocumentStoreAccess.Global`; tenant-looking data cannot silently use this class. |

Privilege is an operation access policy, not a third storage scope. A privileged operation presents an Elsa authorization capability and named purpose; the adapter creates `PrivilegedScoped`, `PrivilegedGlobal`, or `PrivilegedAcrossScopes` access matching the storage unit and records acquisition/outcome without tenant identifiers in metric labels. A global unit may allow ordinary reads but require privileged writes, as with host-wide provider configuration.

Scope is immutable for the lifetime of a store session/unit of work. Mixed global/scoped units of work are invalid. Disposal, cancellation, rollback, and pooled provider reuse must not carry scope into another request.

The provider-neutral context has a nonblank scope identity. Single-tenant hosts use the configured default (`default` unless overridden); absence never grants global access.

## 4. Storage Composition Snapshot

One immutable snapshot describes the host-selected target before the host serves work.

| Field | Meaning |
|---|---|
| `CompositionIdentity` | Stable Elsa application storage identity. |
| `CompositionVersion` | Version of the selected union contract. |
| `SelectedFeatures` | Stable feature identities chosen by the app host. |
| `ManifestSources` | One manifest source per selected Groundwork implementation family. |
| `StorageUnits` | Union of selected units after collision validation. |
| `RequiredCapabilities` | Capabilities derived from active routes/transitions, never configuration flags alone. |
| `Provider` | Exactly one selected provider and version. |
| `TopologyRequirements` | Transaction/replica-set or equivalent prerequisites. |
| `TargetFingerprint` | Deterministic fingerprint consumed by schema planning and evidence. |

### Validation

- Missing, duplicate, or incompatible feature declarations fail before materialization.
- Storage-unit identity collisions identify both source features.
- Every public store registration has a selected durable requirement.
- Every required capability maps to a tested active provider route/transition.
- The schema CLI and runtime composition consume the same snapshot/manifest source.

## 5. Query Route Requirement

| Field | Meaning |
|---|---|
| `QueryIdentity` | Stable versioned route identity. |
| `Owner` | Coverage entry that requires the route. |
| `Scope` | Storage-bound scope applied before all other predicates. |
| `Predicates` | Closed portable predicate shape. |
| `Ordering` | Complete deterministic order including tie-breaker. |
| `Continuation` | Offset/keyset contract and validation rules. |
| `MaximumResultCount` | Finite hard maximum accepted by the adapter. |
| `ResultKind` | Page, count, any, first, projection, mutation, or delete. |
| `PhysicalRoute` | Compiled Groundwork route and required physical fields/indexes. |
| `EvidenceKind` | Provider-native plan/command evidence proving boundary execution. |

An unsupported route has no client-evaluated state. It blocks the selected composition.

## 6. Operational Transition

| Field | Meaning |
|---|---|
| `TransitionIdentity` | Stable name such as `checkpoint-commit`, `queue-claim`, or `command-acknowledge`. |
| `Inputs` | Identities, revisions, fence/lease tokens, idempotency key, timestamps, and finite batch limits. |
| `Preconditions` | Provider-atomic compare conditions. |
| `Writes` | Complete durable state changed by one decision. |
| `SuccessOutcome` | Public domain result. |
| `ConflictOutcomes` | Stable existing-winner, stale-revision, stale-owner, expired-lease, or replay results. |
| `IdempotencyRule` | Equivalent replay and conflicting replay behavior. |
| `FailureWindows` | Before decision, during execution, acknowledgement loss, and after durable decision. |
| `RecoveryRule` | What a new client/process observes and how it converges. |

Execution ownership allocation and checkpoint admission share one durable fencing authority. Placement routing is not a substitute for execution fencing.

## 7. Failure Window

| Field | Meaning |
|---|---|
| `WindowId` | Stable scenario identity. |
| `InjectionPoint` | Before provider call, within provider transaction, after durable decision/before acknowledgement, or during recovery. |
| `AllowedDurableOutcomes` | Exact state set that may exist after interruption. |
| `ForbiddenOutcomes` | Partial bundles, double winners, stale acknowledgements, or lost required work. |
| `RecoveryAction` | Retry, reopen, restart, reclaim, or reconcile action. |
| `FinalInvariant` | State that must hold after convergence. |

## 8. Provider Evidence Record

| Field | Meaning |
|---|---|
| `ScenarioId` | Shared black-box scenario identity. |
| `CoverageEntryId` | Ledger row proved by the scenario. |
| `ProviderIdentity` / `ProviderVersion` | Exact package/provider under test. |
| `Topology` | Database/server configuration relevant to guarantees. |
| `ManifestFingerprint` | Exact storage target. |
| `ExecutionPath` | Active route/handler identity. |
| `Clients` | Independent client/process count. |
| `FailureWindow` | Optional injected interruption. |
| `ResultHash` | Provider-independent observable outcome hash. |
| `NativeEvidence` | Sanitized plan/command proof for bounded execution. |
| `Outcome` | Pass or a classified domain/readiness failure. |
| `Evidence` / `EvidenceSha256` | Catalog-bound durable scenario artifact and verified digest. |
| `NativeEvidenceSha256` | Verified digest for the provider-native plan artifact when present. |

Provider identity, version, topology, execution path, and artifact path are closed catalog values,
not descriptive strings. The artifact payload is checked against the ledger record and every artifact
digest is verified. Memory-backed stores can support unit tests but cannot create a
`ProviderEvidenceRecord`.

## 9. Capability Claim

A capability claim is derived from provider evidence.

| Field | Meaning |
|---|---|
| `CapabilityIdentity` | Stable behavior/capability name. |
| `Provider` | Provider for which it is claimed. |
| `ActivePath` | Route/transition actually selected by composition. |
| `PassingScenarios` | Required evidence records. |
| `Prerequisites` | Topology/configuration checked at startup. |
| `Status` | Available or Unavailable with a diagnostic reason. |

Configuration may request a capability but cannot create one.

## 10. Performance Handoff And Verdict

| Field | Meaning |
|---|---|
| `WorkloadId` | Stable FR-030 workload identity. |
| `CoverageEntries` | Ledger rows represented by the workload. |
| `DatasetDefinition` | Fixed seed, scale, payload distribution, and provider setup. |
| `CorrectnessBaseline` | Required result hash/invariants before timing. |
| `OracleAvailability` | Same-provider EF oracle or documented no-oracle case. |
| `EvidenceLink` | #646 raw/report artifact. |
| `Verdict` | Pass, Redesign, or Blocked. |
| `AcceptedShape` | Shared/linked, dedicated-document, physical-entity, or specialized path selected by evidence. |

The #645 lane consumes this record; #646 owns measurement code, statistics, and final thresholds.

## State Transitions

```text
Missing
  -> Planned
  -> Implemented
  -> Evidence Complete
  -> Performance Complete
  -> Ready
```

- `Externally Blocked` can be entered only with a named owning issue and returns to the previous incomplete state when the dependency lands.
- `Redesign` from #646 returns the row from `Performance Complete` to `Planned` with a new accepted-shape decision pending.
- `Excluded` is terminal only for a linked, explicitly accepted exclusion.
- A regression in behavioral, provider, scope, capability, or performance evidence moves a row out of `Ready` immediately.
