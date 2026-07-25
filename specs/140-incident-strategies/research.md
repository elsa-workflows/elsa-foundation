# Research: Extensible Incident Strategies

## Sources

- GitHub issue [#1015](https://github.com/elsa-workflows/elsa-foundation/issues/1015)
- Current Foundation Design, Publishing, Runtime, API, persistence, and e2e code
- Elsa 3.7.1 incident-strategy source and discovery behavior
- `.specify/memory/constitution-framework.md` and `.specify/memory/constitution.md`
- `docs/glossary/elsa.md`

## Decisions

### Elsa 3 parity is deliberately narrow

**Decision**: Ship only `Fault/1` and `ContinueWithIncidents/1` as built-ins. Preserve automatic
post-fault handling, authored → host → Fault default precedence, and read-only discovery. Do not add
Retry, Suspend, or a mutation endpoint.

**Rationale**: Elsa 3 has only Fault and Continue-with-incidents strategies. Retry/Suspend would be
new policy, not parity, and would prematurely mix automatic resolution with operator recovery.

### Strategy identity is exact and versioned

**Decision**: Use `{ alias, version }`. Alias lookup is ordinal case-insensitive with canonical
descriptor spelling; version is a nonblank opaque ordinal token. Built-in bare aliases are reserved;
third-party aliases must be dotted/namespaced.

**Rationale**: Stable wire identity must not depend on CLR names or unrequested SemVer ordering.

### Authored inheritance is resolved at publication

**Decision**: Authored null remains null in Design. Publishing selects authored reference, configured
host default, then `Fault/1`; validates it; pins it into the executable; and hashes it as behavior.
Runtime uses only that pin.

**Rationale**: Re-publication can intentionally adopt a changed host default while an existing
executable remains behaviorally immutable and Design/Runtime layering stays intact.

### Decisions are executable objects

**Decision**: `IIncidentStrategy.ResolveAsync(context, cancellationToken)` returns one
`IIncidentResolutionAction`. The runtime invokes `ExecuteAsync` directly. `Kind` is durable
classification only, never a dispatch key or persisted CLR discriminator.

**Rationale**: This mirrors MVC action results and gives third parties behavior extensibility without
growing a framework enum.

### Public action capabilities are narrow

**Decision**: Public actions may stage only target-incident Blocking/Open/Resolved transitions,
workflow Faulted, safe metadata, and explicitly registered strategy-safe post-commit intents. They
cannot change activity state, complete/cancel/suspend/retry workflow, mutate other incidents, access
checkpoint services, absorb, suppress, or enqueue core scheduler/dispatch/retry work.

**Rationale**: The runtime can enforce capabilities and replay behavior. In-process third-party code
still has a documented obligation not to perform external side effects independently.

### Absorption and suppression remain system-only

**Decision**: Absorption resolves an incident because a parent meaningfully handled its child fault;
suppression terminally marks it irrelevant because cancellation/reclamation removed its scope. Their
action classes and staging operations remain internal.

**Rationale**: These are structural runtime facts, not general-purpose decisions a custom policy may
claim.

### The batch boundary is the outer drain

**Decision**: Evaluate once after `WorkflowDrainOrchestrator` reaches causal quiescence for one
envelope, after scheduler work and in-drain outbox hops. Include all durable ordinary blocking
activity-fault incidents with null outcome, including residue from an aborted prior pass. Evaluate
ordinal IncidentId order and commit one checkpoint.

**Rationale**: Parent propagation and absorption travel through scheduler/outbox hops. Per-fault or
per-inner-pass evaluation races those structural semantics and cannot provide a meaningful atomic
multi-incident boundary.

### Failures use a fresh built-in Fault action

**Decision**: Null returns, strategy throws, and action throws discard only the incident-local stage
and execute a fresh runtime-owned Fault action with safe `IncidentStrategyFailure` provenance.
Cancellation from the supplied cancelled token aborts; unrelated `OperationCanceledException` is a
normal strategy failure. Fallback/checkpoint failure leaves durable Blocking + null outcome.

**Rationale**: Failure handling cannot recursively invoke extensible code or manufacture evidence for
an outcome that never committed.

### Registration is atomic and discovery is descriptor-only

**Decision**: `AddIncidentStrategy<T>(descriptor)` and `AddIncidentStrategy<T>()` contribute the
descriptor and scoped implementation together. The reflection overload requires one non-inherited
attribute with alias/version and may humanize only display text. Startup validation rejects duplicate
identities and invalid defaults. Discovery reads immutable descriptors and constructs no strategies.

**Rationale**: Registry drift between discovery and execution is prevented while API reads remain
side-effect free.

## Existing Mechanisms Reused

- `RuntimePostCommitIntent` and `IRuntimePostCommitIntentHandler` provide durable idempotent external
  work after checkpoint commit.
- `RuntimeCheckpointCommitter` atomically persists runtime state and outbox items under ownership
  fencing.
- `IWorkflowSchedulerDrainObserver` provides the existing once-per-outer-drain integration point.
- `WorkflowExecutableHasher` already owns behavioral identity construction.
- Workflow Publishing API already has the read permission, mediator endpoint pattern, and capability
  declaration used by value-conversion profile discovery.

## Rejected Alternatives

- Keep or expand `IncidentResolutionAction` enum: blocks third-party behavior extensibility.
- Persist action CLR types or dispatch by `Kind`: couples durable state to implementation types and
  turns classification into a fragile command bus.
- Resolve runtime policy from authored state/current default: breaks pinned executable behavior and
  Design/Runtime isolation.
- Allow arbitrary registered post-commit intents: permits custom actions to smuggle retry/scheduler
  control back into a scope that explicitly excludes it.
- Evaluate immediately when a fault is recorded: races parent absorption and cancellation.
- Return null for no progress: makes omission indistinguishable from extension failure.
