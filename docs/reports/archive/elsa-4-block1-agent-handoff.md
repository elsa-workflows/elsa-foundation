# Elsa 4 — Block 1: Runtime Execution Engine
## Agent Handoff Document

**Target repo:** `elsa-workflows/elsa-foundation`  
**Target agent:** Claude Code with `addyosmani/agent-skills`  
**Scope:** All 9 slices of the Runtime Execution Seam  
**Status of decisions:** Locked — do not re-litigate

---

## ⚠️ STATUS: This handoff has already been executed — do NOT start from Slice 1

> **Verified against `main` (code-as-truth pass).** The 9-slice plan below has been built out — and extended past the original 9 slices, through `specs/080`. Treat this document as a historical brief and a record of the locked decisions (§4), **not** as an open work order. Re-running `/spec`→`/build` from Slice 1 would redo finished work and risk regressing it.
>
> What actually exists on `main`:
> - **Slices 1–9 are implemented**, each backed by committed specs and source under `src/Elsa/Workflows/Runtime/Core`:
>   - S1 Executable artifact → `WorkflowExecutable.cs` (single-root-activity shape), `WorkflowExecutableIdentity.cs`, `ExecutableNode.cs` — specs `007`, `070`, `071`
>   - S2 Execution/Activity state → `WorkflowExecutionState.cs`, `ActivityExecutionState.cs`, `SchedulerState.cs` — specs `007`, `027`, `028`
>   - S3 Checkpoint → `RuntimeCheckpoint.cs`, `RuntimeCheckpointCommit.cs`, `IRuntimeCheckpointPersistencePolicy.cs` — specs `008`, `034`, `080`
>   - S4 Pipeline slots → `RuntimePipelinePlan.cs`, `I*RuntimeMiddleware.cs` — spec `009`
>   - S5 Bookmark resume → `BookmarkState.cs`, `WorkflowExecutableResumeTarget.cs`, `IBookmarkResumeResolver.cs` — specs `010`, `055`–`058`
>   - S6 Input/value capture → `RuntimeInputBinding.cs`, `DurableValueState.cs`, `IDurableValueStateStore.cs` — specs `011`, `060`, `061`
>   - S7 Diagnostics/incidents → `RuntimeHistoryEvent.cs`, `IncidentState.cs`, `ActivityExecutionInspection*.cs` — specs `012`, `043`, `079`
>   - S8 Operational/outbox → `RuntimePostCommitOutbox.cs`, `RuntimeRecovery.cs`, `RuntimeWaitRegistration.cs`, `IRuntimeRecoveryScanner.cs` — specs `013`, `046`–`051`
>   - S9 Elsa 3 migration → `elsa3/` projects + mapping — spec `014`
> - The **addendum decisions D11–D16** (volatile waits, completion propagation, generators, pause/control-plane, wait-intents, in-process execution-agent) are also implemented — `ControlPlaneState.cs`, `GeneratorModels.cs`, `RuntimeWaitRegistration.cs`, `WorkflowExecutionAgentModels.cs`, `InProcessWorkflowExecutionAgentProvider.cs` — specs `015`–`080`.
> - `WorkflowExecutionContext` is a **full 264-line implementation** with no stubbed members (spec `064`). The "entirely a stub" claim in earlier reports is stale; see `docs/reports/elsa-4-gap-analysis.md` §1.
>
> **The one genuine remaining gap from this block:** sub-workflow execution — `WorkflowDefinitionActivity.Execute` still throws `NotSupportedException` (`src/Elsa/Activities/Composition/Runtime/Activities/WorkflowDefinitionActivity.cs:24`; specs `005`, `068`). If you are picking up runtime work, **that** is the live item — not Slice 1.
>
> §5's "❓ Verify existence" table below is also stale: every type it lists as unverified now exists. Keep §4 (locked decisions) as the authoritative design record.

---

## 1. Setup Before Starting

Install the agent-skills plugin if not already installed:

```bash
/plugin marketplace add addyosmani/agent-skills
/plugin install agent-skills@addy-agent-skills
```

Then read the repo guide:

```
AGENTS.md                                   ← repo entrypoint, operating model
.specify/memory/constitution.md             ← hard gates (read §E2.x sections in full)
.specify/memory/constitution-framework.md   ← framework gates (read §2.6, §2.7, §2.21, §2.23)
docs/reports/elsa-4-runtime-execution-brainstorm-decisions.md   ← locked decisions
docs/reports/elsa-4-runtime-execution-addendum-topics.md        ← locked addendum decisions
docs/reports/runtime-execution-pre-spec-handoff.md              ← known experiments/shortcuts
docs/glossary/elsa.md                       ← canonical Elsa 4 terms
EXTENSION_POINTS.md                         ← sanctioned extension points index
```

---

## 2. How to Work

Use the `addyosmani/agent-skills` commands at each slice boundary:

| Phase | Command | What happens |
|---|---|---|
| Define the slice | `/spec` | Produces a spec document under `specs/` for that slice |
| Break it down | `/plan` | Decompose spec into atomic tasks with acceptance criteria |
| Implement | `/build` | One thin vertical slice at a time; commit after each passing test |
| Prove it works | `/test` | TDD — write tests first, prove they pass before moving on |
| Before merging | `/review` | Five-axis code review; confirm constitution compliance |

**Rule:** Never move to the next slice without `/review` passing on the current one.  
**Rule:** Always `/spec` before any code. Even a two-line spec is fine for trivial changes.  
**Rule:** The spec document for each slice goes to `specs/NNN-runtime-<slice-name>/spec.md` and stays committed.

---

## 3. Non-Negotiable Constraints (Constitution Gates)

These are hard stops. If implementing a slice would violate any of these, stop and raise it — do not work around them.

### §E2.2 — Runtime/Design separation
`Elsa.Workflows.Runtime.*` must NOT directly depend on `Elsa.Workflows.Design.*`.  
No `ProjectReference` from any `Elsa.Workflows.Runtime.*` project to any `Elsa.Workflows.Design.*` project.

### §E2.6 — Runtime executes without design data
Runtime executes runnable artifacts without loading Design-side data at execution time.  
A workflow execution must never load `WorkflowDefinitionState` or authored document JSON to decide what to do next.

### §E2.9 — Scope separation
`WorkflowDefinitionState` (authoring), read projections (reading), and `WorkflowExecutable`/`WorkflowExecutionState` (executing) are separate scopes with separate contracts. They do not share mutable state.

### §2.6.4 (framework) — Split contracts
Design-time and runtime contract consumers require split contracts. If a type is needed by both, it belongs in a shared `.Core` contracts package — not in either Design or Runtime.

### §2.7 (framework) — Bridges/adapters
Bridges and adapters isolate dependencies. If Runtime and Design must communicate, a bridge package that depends on both is the correct pattern. The bridge is never in the core domain packages.

### §2.21 (framework) — Preserve tests
Refactors preserve existing passing tests unless test removal is explicitly approved. Do not delete or skip tests to make slices pass.

### §2.23 (framework) — Unit tests required
Every new feature class and logic-bearing implementation gets focused unit tests. Integration tests do not substitute for unit tests on logic-bearing code.

---

## 4. Locked Decisions (Authoritative — Do Not Re-Open)

These were settled in the brainstorm and addendum review. Implement them as stated.

### D1. Executable Artifact Boundary
Runtime executes a runtime-owned `WorkflowExecutable` artifact pinned per workflow execution.  
- Authored documents (`WorkflowDefinitionState`) are compile/publish inputs, not runtime execution inputs.  
- Running instances pin to the artifact they started with. Moving to a newer artifact requires explicit migration.  
- Descriptor resolution, expression compilation, data-link compilation, and validation happen at compile/publish time — not at execution time.

### D2. Split Runtime State Model
Do not use a monolithic `WorkflowState`. The runtime state is split into:

| Contract | Owns |
|---|---|
| `WorkflowExecutionState` | Identity, artifact reference, status, timestamps, correlation, parent, tenant |
| `SchedulerState` | Pending/suspended work, queue position, branch/iteration scheduling metadata |
| `ActivityExecutionState` | Active or resumable activity executions |
| `BookmarkState` | Durable resume handles, lookup fields |
| `DurableValueState` | Declared durable values: variables, inputs/outputs, captured activity outputs |
| `IncidentState` | Unresolved execution-affecting incidents |
| `OperationalState` | Host coordination: outbox item IDs, drain markers, leases, heartbeats |

History and audit records are observability projections — not runtime continuation state. Runtime must never read history to continue execution.

### D3. ActivityExecution Contract
`ActivityExecution` is the durable identity for one concrete execution of one executable activity node.

Core identity:
```csharp
ActivityExecutionId          // unique ID for this run
WorkflowExecutionId
ExecutableNodeId             // compiled runtime node
AuthoredActivityId           // links back to the authored document
ActivityType
ActivityTypeVersion
```

Lifecycle:
```csharp
Status                       // Scheduled | Running | Completed | Suspended | Faulted | Cancelled
SubStatus?
ScheduledAt
StartedAt
CompletedAt?
```

Relationship:
```csharp
SchedulingActivityExecutionId?   // what caused this to run
ParentActivityExecutionId?       // which scope owns this execution
BranchId?
IterationId?
CallStackDepth?
```

Associated:
```csharp
BookmarkIds
IncidentIds
FaultCount
AggregateFaultCount
Metadata
```

**Evaluated inputs and raw outputs are NOT durable ActivityExecution state by default.**

### D4. Named Checkpoint Contract
Checkpoints are named runtime persistence boundaries.

Canonical names:
```
WorkflowStarted | ActivityScheduled | ActivityStarted | ActivityCompleted
ActivitySuspended | BookmarkCreated | BookmarkConsumed | DurableValueCaptured
IncidentRecorded | WorkflowSuspended | WorkflowCompleted | WorkflowFaulted
WorkflowCancelled | PostCommitIntentRecorded
```

Checkpoint semantics ≠ persistence policy:
- Semantics = what changed and why (named fact)
- Policy = when/how to flush (immediate, batched, or skipped when safe)
- Post-commit side effects are recorded before commit and delivered only after commit succeeds.

### D5. Bookmark Resume Contract
Bookmarks store stable `ResumeTargetId`s pointing into the pinned executable artifact — NOT C# method names.

Durable bookmark shape:
```csharp
BookmarkId
WorkflowExecutionId
ActivityExecutionId
ExecutableNodeId
ResumeTargetId               // stable ID declared by the activity author
StimulusType
StimulusHash
Payload?                     // follows runtime value rules
Metadata                     // query/audit data, not runtime state
CreatedAt
ExpiresAt?
```

Resolution: `Bookmark.ResumeTargetId → pinned artifact resume table → activity runtime handler → C# method`.

Activity authors declare resume targets:
```csharp
[ResumeTarget("wait-for-delivery")]
public ValueTask OnDeliveryStatusReceived(ActivityResumeContext context)
```

### D6. Two Named Pipelines With Stable Slots
Keep separate `WorkflowExecutionPipeline` and `ActivityExecutionPipeline`.

Workflow pipeline slots (in order):
```
Ingress | LoadExecutionState | AcquireExecutionLease | BeforeRun
Schedule | Checkpoint | PostCommit | CompleteRun | ReleaseExecutionLease
```

Activity pipeline slots (in order):
```
BeforeActivity | EvaluateInputs | BeforeInvoke | Invoke
AfterInvoke | CaptureOutputs | HandleActivityResult | Checkpoint | AfterActivity
```

Middleware targets stable slots:
```csharp
builder.ActivityPipeline.Add<MyMiddleware>(slot: ActivityPipelineSlot.BeforeInvoke, order: 50);
```

The resolved pipeline plan must be inspectable for diagnostics.

### D7. Output and Data-Link Semantics
```
Activity output    = scoped to the active execution (ephemeral)
Data link          = compiles to an input binding; reads activity output only within same active scope
Durable value      = explicitly declared; survives suspension and resume
```

Cross-suspension or ambiguous-scope bindings require explicit capture semantics:
```
last(A.Output) | all(A.Output) | iteration(A.Output, current) | capture A.Output into Customers[]
```

Ambiguous output references without explicit semantics are compile-time errors.

### D8. Diagnostics and History Separation
```
Runtime state        = only what the engine needs to continue
Execution history    = observability projection (never read for continuation)
Audit payloads       = policy-controlled (sensitive values excluded by default)
Incident state       = minimal first-class state + richer history/audit projection
```

History event categories: `WorkflowLifecycle | ActivityLifecycle | BookmarkLifecycle | ValueLifecycle | IncidentLifecycle | SchedulerLifecycle | OperationalLifecycle`

### D9. Operational Recovery and Outbox
Distinct concepts — do not conflate:
```
Execution lease     = host coordination lock
Heartbeat           = liveness signal
Graceful drain      = finish-and-stop-new-work mode
Interrupted exec    = execution whose host died
Recovery scanner    = finds interrupted executions and requeues from last checkpoint
Post-commit outbox  = record intent → checkpoint commit → deliver intent → mark delivered
Domain retry        = workflow/activity policy decision (NOT operational recovery)
```

### D10. Elsa 3 Compatibility Boundary
Supported:
- Import Elsa 3 workflow definition JSON/documents
- Compile imported definitions into Elsa 4 executable artifacts
- Migration diagnostics and fixups

NOT supported by default:
- Binary compatibility with Elsa 3 `WorkflowState`
- Transparent resume of Elsa 3 persisted activity execution contexts
- Automatic mapping of Elsa 3 callback-method bookmarks to `ResumeTargetId`

### D11. Volatile Wait vs Durable Suspension (Addendum)
```
Durable suspension  = commit state, unload from memory, resume later by bookmark/event
Volatile wait       = keep in memory, await task/timer/event, continue in same host context
```

Volatile wait rules:
- Scoped to an `ActivityExecution` and branch — not the whole workflow
- Multiple concurrent volatile waits may exist in one workflow execution
- Workflow state mutation remains single-threaded through the scheduler
- True parallel activity execution is explicitly deferred

### D12. Activity Completion Propagation (Addendum)
Model completion as deterministic scheduler work, not immediate recursive bubbling:
```
ActivityCompleted → ParentCompletionEvaluation → ContinuationScheduling → Checkpoint
```
- Completion work is queued internally and drained deterministically before unrelated work
- Joins evaluate only after all required branch completions are recorded

### D13. Generator Activities (Addendum)
Distinguish triggers from generators:
```
Trigger    = external source that starts or resumes a workflow
Generator  = in-workflow activity that emits execution events over time
```
A generator owns a long-lived `ActivityExecution`. Each emission creates scheduler work. Generator lifetime is tied to its owning execution scope.

### D14. Pause/Unpause Contract (Addendum)
Pause is runtime control-plane policy with explicit scopes. Pause ≠ durable suspension.

Pause scopes: `Ingress | WorkflowExecution | Activity/Generator | Worker/Dispatcher | HostDrain`

Pause is cooperative — the scheduler stops before starting new `ActivityExecution`s, not mid-execution. Pause state belongs to `ControlPlaneState`, not ordinary `WorkflowExecutionState`.

### D15. Wait-Dependent Post-Commit Intents (Addendum)
If Elsa causes the side effect that may produce the signal, register the wait BEFORE delivering the side effect:
```
write state change + write wait registration + write post-commit intent
→ checkpoint commit
→ deliver intent
→ wait becomes Active
→ reply resumes workflow
```

States: `WaitRegistration.State ∈ { Reserved | Active | Satisfied | Cancelled | Expired | Faulted }`

### D16. Actor-Style Execution Semantics (Addendum)
`WorkflowExecutionId → one active execution agent/mailbox` (single-writer property).

The execution agent processes commands sequentially:
```
Start | ScheduleActivity | CompleteActivity | ContinueVolatileWait | DeliverSignal
CreateBookmark | Pause | Unpause | Cancel | Checkpoint
```

Provider model: `WorkflowExecutionAgent` + `WorkflowExecutionAgentProvider`. Default implementation: in-process mailbox. Elsa durable state contracts remain the source of truth — they are NOT equal to the actor framework's persistence model.

---

## 5. Current State (What Already Exists)

> **Stale.** This table predates implementation. Every type marked ❓ below now exists on `main` — see the status banner at the top of this document. Retained for historical context only.

Do NOT assume the reports in `docs/reports/` reflect the current code state. Read the actual files before starting each slice. The situation as of the last review:

| File / Type | Status |
|---|---|
| `WorkflowExecutionState` record | ✅ Exists, properly structured (PinnedExecutable, status, timestamps, correlation, parent, tenant, metadata) |
| `WorkflowExecutionStatus` enum | ✅ Exists (Pending/Running/Suspended/Completed/Faulted/Cancelled) |
| `WorkflowExecutionContext` class | ✅ Substantially implemented (inputs, variables, outputs, memory register) |
| `WorkflowExecutableIdentity` | ✅ Referenced by `WorkflowExecutionState` — verify it exists |
| `ActivityExecutionState` | ❓ Verify existence and completeness |
| `SchedulerState` | ❓ Verify existence and completeness |
| `BookmarkState` | ❓ Verify existence and completeness |
| `DurableValueState` | ❓ Verify existence and completeness |
| `IncidentState` | ❓ Verify existence and completeness |
| `OperationalState` | ❓ Verify existence and completeness |
| `WorkflowExecutionPipeline` builder | ❓ Verify slot names match D6 |
| `ActivityExecutionPipeline` builder | ❓ Verify slot names match D6 |
| `BookmarkState` / `ResumeTargetId` | ❓ Verify D5 compliance |
| Checkpoint contracts | ❓ Verify named boundaries match D4 |
| `WorkflowDefinitionActivity.Execute` | ⛔ Throws `NotSupportedException` — sub-workflow execution is deferred |

Run this before starting to map the actual state:
```bash
grep -r "NotImplementedException\|NotSupportedException\|throw new" \
  src/Elsa/Workflows/Runtime/ \
  src/Elsa/Activities/Runtime/ \
  --include="*.cs" -l
```

---

## 6. Work Sequence — 9 Slices

Work the slices in order. Each slice depends on the contracts from the previous ones.

---

### Slice 1 — Executable Artifact Contract

**What:** Define what Runtime executes (the `WorkflowExecutable` artifact boundary).

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-executable-artifact/spec.md`

**Spec must cover:**
- Objective: define the runtime-owned artifact type and its identity fields
- Artifact identity: `ArtifactId`, `DefinitionId`, `DefinitionVersionId`, `ArtifactVersion`, `ArtifactHash`, `PublishedAt`, `CompatibilityMetadata`
- `ExecutableNode` identity inside an artifact: `ExecutableNodeId`, `AuthoredActivityId`, `ActivityType`, `ActivityTypeVersion`
- Artifact-level resume target table: `ResumeTargetId → handler descriptor` (placeholder is fine for now)
- Artifact-level activity descriptor reference shape
- Boundaries: Runtime code must not reference `WorkflowDefinitionState` or any `Elsa.Workflows.Design.*` type at execution time (§E2.2, §E2.6)

**Implementation:**
- Primary project: `Elsa.Workflows.Runtime.Core`
- New types belong in `src/Elsa/Workflows/Runtime/Core/Models/` or `src/Elsa/Workflows/Runtime/Core/Contracts/`
- If `WorkflowExecutableIdentity` already exists, verify it matches D1 and extend if needed

**Acceptance tests (write these first):**
- [ ] A runtime service can load a minimal `WorkflowExecutable` artifact without loading `WorkflowDefinitionState`
- [ ] A `WorkflowExecutionState` can reference a pinned artifact by `ArtifactId` and `ArtifactVersion`
- [ ] A structural test (project reference scan) verifies `Elsa.Workflows.Runtime.Core` has no `ProjectReference` to `Elsa.Workflows.Design.*`
- [ ] Missing runtime activity support produces a `RuntimeArtifactCompatibilityException`, not a design deserialization error

**Do NOT do:**
- Do not make the artifact shape identical to `WorkflowDefinitionState`
- Do not embed authored document JSON in the artifact
- Do not couple artifact loading to the Studio/API endpoint models

---

### Slice 2 — WorkflowExecution and ActivityExecution State

**What:** Durable identity and state contracts for workflow and activity executions.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-execution-state/spec.md`

**Spec must cover:**
- `WorkflowExecutionState` (verify existing, extend if missing fields from D2)
- `ActivityExecution` identity model (D3)
- `ActivityExecutionState` lifecycle model (D3)
- Relationship fields: scheduling parent, parent scope, branch, iteration, call-stack depth
- Minimal `SchedulerState`: references to pending/active `ActivityExecution`s or executable nodes
- State persistence interfaces or repository stubs if needed downstream

**Implementation:**
- Primary project: `Elsa.Workflows.Runtime.Core`
- Verify `WorkflowExecutionState` completeness against D2 — it exists but may be missing fields
- Create `ActivityExecution` record and `ActivityExecutionState` if they don't exist
- Minimal `SchedulerState` — just enough for slices 3 and 4 to build on

**Acceptance tests:**
- [ ] A workflow instance creates a `WorkflowExecutionState` pinned to an artifact
- [ ] Scheduling an executable node creates or references an `ActivityExecution` with correct identity fields
- [ ] A loop scenario can be represented: same `ExecutableNodeId`, different `ActivityExecutionId`, incrementing `IterationId`
- [ ] A parallel branch scenario can be represented: same parent, different `BranchId`
- [ ] `ActivityExecution` state does not contain evaluated inputs or raw activity outputs as durable fields
- [ ] State contracts are round-trip serializable (add a JSON serialization test for each new record type)

**Do NOT do:**
- Do not add `ActivityExecutionContext` behavior (live DI scope, services, scheduling calls) to the durable state models
- Do not store raw C# output objects in `ActivityExecutionState`
- Do not combine history/audit data into the state records

---

### Slice 3 — Checkpoint Contract and Persistence Policy

**What:** Named persistence boundaries and policy hooks.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-checkpoint/spec.md`

**Spec must cover:**
- `Checkpoint` model with all canonical names from D4
- `ICheckpointWriter` / `ICheckpointDispatcher` abstraction (or equivalent names)
- Default persistence policy interface: `ICheckpointPersistencePolicy`
- Atomic state-change envelope: includes workflow state, activity state, bookmark refs, durable value refs, incident refs, operational markers
- `PostCommitIntent` placeholder contract (kind, payload reference, status, retry policy)
- Volatile wait checkpoint semantics (D11): volatile waits do not themselves produce a durable checkpoint

**Implementation:**
- Primary project: `Elsa.Workflows.Runtime.Core`
- New contracts/interfaces go in `Contracts/` subfolder
- Default policy implementation goes in a separate class (not hardcoded in the dispatcher)

**Acceptance tests:**
- [ ] Runtime produces named checkpoint records for: `WorkflowStarted`, `ActivityScheduled`, `ActivityStarted`, `ActivityCompleted`, `WorkflowSuspended`, `WorkflowCompleted`, `IncidentRecorded`
- [ ] A test shows persistence policy can flush immediately or defer without changing checkpoint semantics
- [ ] `PostCommitIntent` records exist before commit; delivery is confirmed after successful commit in a test
- [ ] Volatile wait does NOT produce a `WorkflowSuspended` checkpoint

**Do NOT do:**
- Do not bake a specific persistence implementation (EF Core, etc.) into the Core project
- Do not deliver post-commit intents synchronously in the same transaction as the checkpoint

---

### Slice 4 — Pipeline Slots and Inspectable Plans

**What:** Named extension points before any behavior-heavy middleware is written.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-pipelines/spec.md`

**Spec must cover:**
- `WorkflowExecutionPipelineBuilder` with the 9 slot names from D6
- `ActivityExecutionPipelineBuilder` with the 9 slot names from D6
- Middleware registration contract: `Add<T>(slot, order)` signature
- `IPipelinePlan` / pipeline introspection API showing resolved middleware order per slot
- Placeholder built-in middleware stubs for: load state, scheduling, input evaluation, invoke, capture outputs, checkpoint, post-commit
- Slot naming enums/constants committed to a contracts package (so extension modules can reference them without taking a runtime implementation dependency)

**Implementation:**
- Pipeline builder and slot contracts: `Elsa.Workflows.Runtime.Core`
- Extension modules should target slots via a contracts-only reference, not by depending on the full pipeline implementation

**Acceptance tests:**
- [ ] A module can register middleware into a stable slot without depending on concrete neighboring middleware
- [ ] The resolved pipeline plan is inspectable and lists middleware in slot + order sequence
- [ ] Workflow and activity context types remain distinct (cannot pass `ActivityExecutionContext` to `WorkflowExecutionPipeline`)
- [ ] A test verifies that slot-targeted registration produces deterministic ordering regardless of registration order

**Do NOT do:**
- Do not encode behavior-critical ordering in implicit linked-middleware chains (no `UseMiddlewareAfterExact<T>()` patterns)
- Do not make the workflow and activity pipelines share context models

---

### Slice 5 — Bookmark Resume Contract

**What:** Durable resume handles using `ResumeTargetId`, not method names.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-bookmarks/spec.md`

**Spec must cover:**
- `BookmarkState` model (D5 fields)
- `[ResumeTarget("id")]` attribute for activity authors
- Resume target table inside the artifact: maps `ResumeTargetId → handler descriptor`
- `IBookmarkStore` interface: create, find by stimulus type/hash, find by workflow execution, consume
- `IResumeTargetResolver`: `ResumeTargetId → C# method/delegate` via the pinned artifact
- Lookup fields: `WorkflowExecutionId`, `ActivityExecutionId`, `ExecutableNodeId`, `StimulusType`, `StimulusHash`

**Implementation:**
- Primary: `Elsa.Workflows.Runtime.Core`
- The `[ResumeTarget]` attribute and `IResumeTargetResolver` are the activity-author-facing surface — keep them simple

**Acceptance tests:**
- [ ] A bookmark can be created for an activity execution and executable node
- [ ] Resume resolves through `ResumeTargetId` in the artifact resume table — not through the bookmark's stored method name
- [ ] Missing resume target (artifact updated, old target removed) produces a clear `ArtifactResumeTargetNotFoundException`, not a null reference exception
- [ ] Two bookmarks for the same stimulus type on the same workflow execution can be distinguished by `ActivityExecutionId`
- [ ] A test verifies a bookmark survives a round-trip serialization/deserialization

**Do NOT do:**
- Do not store C# method names as durable bookmark fields
- Do not resolve bookmarks by looking up authored activity IDs in the authored document

---

### Slice 6 — Input Bindings, Outputs, and Durable Value Capture

**What:** Connect expression/input evaluation to the value-persistence decisions.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-values/spec.md`

**Spec must cover:**
- `RuntimeInputBinding` model: binding kind (ActivityOutput | DurableValue | Expression | Literal | ExternalReference)
- Active execution output register: keyed by `ActivityExecutionId`, cleared when execution scope ends
- `IDurableValueStore` interface: declare, read, write, capture-from-output
- Binding resolver rules (D7):
  - Same-scope output: ok without explicit capture
  - Cross-suspension boundary: must declare durable capture
  - Loop/parallel ambiguity: compile-time error unless explicit semantics declared (`last()`, `all()`, `iteration()`)
- Compile-time diagnostic types for ambiguous binding references
- Durable value capture integration with checkpoint (Slice 3): capture checkpoint emits `DurableValueCaptured` named checkpoint

**Implementation:**
- Primary: `Elsa.Workflows.Runtime.Core`
- Expression integration via existing `Elsa.Expressions.Core` contracts (do not add a new expression system)

**Acceptance tests:**
- [ ] Same-scope activity output binding resolves without explicit capture
- [ ] A binding that references an output across a suspension boundary requires `DurableValueCaptured` checkpoint
- [ ] Ambiguous loop output reference (activity in a loop, no explicit semantics) produces a compile-time `AmbiguousOutputReferenceException`
- [ ] History output snapshots cannot be used as input binding sources (structural test)
- [ ] `DurableValueState` round-trips through serialization

**Do NOT do:**
- Do not allow history records to be used as runtime input sources
- Do not make all activity outputs durable by default (they are ephemeral scope values)

---

### Slice 7 — Diagnostics, History, and Incidents

**What:** Observability without making history a runtime dependency.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-diagnostics/spec.md`

**Spec must cover:**
- Execution history event model for all 7 categories from D8
- `WorkflowLifecycleEvent`, `ActivityLifecycleEvent` minimum shapes
- `IncidentState` model (minimal runtime state: id, kind, severity, message, activity execution ref)
- `IncidentHistoryProjection` shape (richer: payload, stack trace, timestamps, resolution info)
- Sensitive value exclusion policy interface: `IHistoryPayloadPolicy`
- Payload capture policy: policy determines whether input/output/variable snapshots are included
- `IExecutionHistoryStore` write interface (read is explicitly deferred — history is never read for continuation)

**Implementation:**
- Primary: `Elsa.Workflows.Runtime.Core` for contracts
- Structured log integration via existing `Elsa.Diagnostics.StructuredLogs` if it exists; OpenTelemetry via `Elsa.Diagnostics.OpenTelemetry`

**Acceptance tests:**
- [ ] Runtime continues execution after emitting history events without reading them back
- [ ] Blocking incident is queryable from `IncidentState` without replaying history
- [ ] Input/output snapshots are absent by default unless policy explicitly enables capture
- [ ] Sensitive fields are excluded from history payloads (a test with a known sensitive-field marker)
- [ ] A structural test confirms `IExecutionHistoryStore` has no read-for-continuation methods

**Do NOT do:**
- Do not add a "read history to resume" path anywhere
- Do not embed full input/output values in `IncidentState` (they belong in the history projection)

---

### Slice 8 — Operational Recovery and Post-Commit Outbox

**What:** Host reliability and outbox delivery, preserving Elsa 3 guarantees with clean contracts.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-operational/spec.md`

**Spec must cover:**
- `IExecutionLease` contract: acquire, renew, release, is-expired
- `IHeartbeatSender` contract: send, configure interval
- Interrupted execution marker in `OperationalState`
- `IRecoveryScanner` contract: find expired/interrupted executions, requeue from last checkpoint
- `PostCommitIntent` full contract (extending Slice 3 placeholder): kind enum, payload, delivery state, retry policy
- `IPostCommitOutbox` contract: record intent, mark-delivered, find-pending
- `WaitRegistration` contract with states and `dependsOnIntentId` (D15): `Reserved | Active | Satisfied | Cancelled | Expired | Faulted`
- Correlated wait registration pattern (D15): write wait + intent atomically before commit
- Domain retry policy boundary: `IWorkflowRetryPolicy` is activity/workflow policy, not operational recovery

**Implementation:**
- Primary: `Elsa.Workflows.Runtime.Core` contracts; thin default implementations in the same project or a companion `Elsa.Workflows.Runtime.Infrastructure` project
- Pause/unpause control plane (D14): `ControlPlaneState` belongs in `OperationalState` or a co-located type

**Acceptance tests:**
- [ ] A lost lease requeues from the last checkpoint without marking as a domain retry
- [ ] Post-commit intents follow record → commit → deliver → mark-delivered ordering (test with a simulated delivery failure before commit and after)
- [ ] Drain/quiescence stops new `ActivityExecution` scheduling without corrupting active executions
- [ ] `WaitRegistration` transitions correctly: `Reserved → Active` after intent delivery, `Reserved → Satisfied` when signal arrives before delivery
- [ ] Domain retry is rejected if no `IWorkflowRetryPolicy` is in the chain (structural test)

**Do NOT do:**
- Do not implement distributed consensus or multi-node placement in this slice (actor provider is a future extension — D16 is a design decision, not a Slice 8 deliverable)
- Do not conflate operational recovery with workflow fault handling

---

### Slice 9 — Elsa 3 Definition Migration Boundary

**What:** Bounded, explicit compatibility with Elsa 3 authored definitions.

**Spec trigger:** `/spec` → produce `specs/NNN-runtime-elsa3-migration/spec.md`

**Spec must cover:**
- Which Elsa 3 authored definition JSON shapes are accepted (check `Elsa3.Models` and `Elsa3.Mapping` projects — they already exist)
- `MigrationDiagnostic` model: severity, code, message, activity path, suggested fix
- Import pipeline: Elsa 3 definition JSON → validation → mapping → Elsa 4 authored document
- Compile path: Elsa 4 authored document → `WorkflowExecutable` artifact
- Explicit unsupported-instance-resume documentation (a code-level guard or exception with a clear message)
- Optional compatibility-host backlog item (mark as `docs/program-goals/` entry, not implementation)

**Implementation:**
- Primary: `Elsa3.Mapping` (already in solution) — extend it; do not create a competing mapping project
- Import API endpoint: `Elsa.Workflows.Runtime.Api` — add an import endpoint only if Runtime.Api already has a router for definition operations
- Do NOT put Elsa 3 parsing code in `Elsa.Workflows.Runtime.Core`

**Acceptance tests:**
- [ ] A known valid Elsa 3 definition JSON imports successfully and produces a compilable Elsa 4 authored document
- [ ] A known invalid Elsa 3 definition JSON produces actionable `MigrationDiagnostic` records, not an unhandled exception
- [ ] Persisted Elsa 3 `WorkflowState` JSON is NOT accepted as an Elsa 4 `WorkflowExecutionState` (a guard test)
- [ ] A live-instance-resume attempt produces a clear `Elsa3LiveResumeNotSupportedException` with cutover guidance

**Do NOT do:**
- Do not add binary `WorkflowState` deserialization to the Elsa 4 runtime
- Do not attempt automatic mapping of Elsa 3 callback-method bookmark names to `ResumeTargetId`

---

## 7. Cross-Cutting Rules (Apply to Every Slice)

### Tests
- Write tests **before** implementation (`/test` discipline applies even for contracts)
- Test categories required per slice: structural dependency tests, model serialization tests, behavioral unit tests for any logic-bearing class
- Test project naming convention: `Elsa.Workflows.Runtime.Core.Tests`, etc. — check existing test projects first
- Do NOT use `[Fact]` methods that exceed ~30 lines; extract helpers

### Extension Points
- After each slice, update `EXTENSION_POINTS.md` to register any new sanctioned extension points
- New extension point types: interfaces with `Source`, `Contributor`, `PreProcessor`, `PostProcessor`, `Handler`, `Store`, `Policy`, or `Resolver` suffix
- Run `tools/maps/generate-extension-point-map.sh` (or `.ps1`) after updating `EXTENSION_POINTS.md`

### Git workflow
- Feature branch per slice: `feature/runtime-slice-N-<short-name>`
- Commit after each green test run — small, atomic commits
- Commit the spec document (`specs/NNN-runtime-*/spec.md`) in the first commit of each slice branch
- Use `/review` before any merge to main

### Documentation
- Update `docs/glossary/elsa.md` when you introduce or refine a canonical term (D15 addendum established the formal glossary of runtime terms)
- Do NOT write new concept explanations in code comments that duplicate glossary entries — link to the glossary

### What to do if you hit a blocker
- Constitution gate conflict → stop; raise in the task comments, do not work around it
- Locked decision seems wrong for the implementation → raise it; do not re-open the decision unilaterally
- Existing test fails after your change → fix your implementation first; do not delete or skip the test (§2.21)

---

## 8. Risks to Guard Against

| Risk | Guard |
|---|---|
| Starting scheduler behavior before artifact/state contracts are pinned | Slices are ordered — do not skip ahead |
| Reusing `WorkflowDefinitionState` shape for the runtime artifact | The artifact shape must own its own identity fields |
| Using history records to continue execution | History store interface has write-only methods for continuation purposes |
| Storing callback method names as durable bookmark data | `ResumeTargetId` is the only durable resume identifier |
| Attempting Elsa 3 live instance resume | Guard exception + clear message is the required implementation |
| Pipeline ordering encoded in implicit middleware chains | Only slot + order targeting is permitted |
| Delivering post-commit intents before checkpoint commit succeeds | Outbox pattern is mandatory — see D15 and Slice 8 |
