# W21 / MD-6 — `Elsa.Workflows.Runtime.Core` charter audit + split disposition

Status: report (audit + disposition). Design/disposition only — **no split executed**. The decision is
recorded as a proposed ADR ([0033](../adr/0033-runtime-core-splits-contracts-from-engine.md)); ratification
sits with the [Runtime Execution Seam](../program-goals/runtime-execution-seam.md) owner. Produced by W21
of the Elsa 4 remediation fleet.

**Branch point:** `1d5bb6bb` (W18 merge tip). **LoC method:** physical `.cs` line count.
**Snapshot caveat:** the runtime shape moved under W12/W13/W14 and specs/082–084 and will keep moving
as the slot-decomposition continuation (`specs/084`) proceeds; the sizing below is a snapshot at this SHA.

## The finding (MD-6)

`Elsa.Workflows.Runtime.Core` behaves like a runtime engine, not a thin contracts-only `.Core`. The
2026-07 review measured it at 15,042 LoC. **At `1d5bb6bb` it is 19,029 LoC** — it *grew*, because W12
folded the ADR-0029 Move-2 phase logic into slot-bound services here and specs/082–083 added the
pipeline execution spine. It is by far the largest project in the repo (next-largest `Elsa.Server`
is 6,868).

## Audit against the §2.1 `.Core` charter

Framework §2.1 defines Layer 1 (`*.Core`) as containing *"interfaces, abstract classes, models, thin
utility implementations, helper extensions"* with the heavy work living in Layer 3 implementation
projects. §2.17 defines **thin**: *"mechanical rather than domain-decisive — delegation, wrapping,
simple default behaviour, guards, option binding, trivial transformation… must not contain business
policy, persistence strategy, infrastructure-specific logic, or branching that encodes meaningful
domain decisions."*

Top-level folder breakdown at `1d5bb6bb`:

| LoC | Files | Folder | Charter verdict |
|---:|---:|---|---|
| 10,092 | 94 | `Services/` | **Breach** — logic-bearing engine, not thin utility |
| 6,246 | 74 | `Models/` | Charter-legitimate (models are explicitly allowed) |
| 1,361 | 74 | `Contracts/` | Charter-legitimate (interfaces) |
| 280 | 8 | `Constants/` | Charter-legitimate |
| 258 | 3 | `Builders/` | Charter-legitimate (pipeline builders — thin) |
| 256 | 11 | `Middleware/` | Charter-legitimate (pipeline contract + attribute) |
| 190 | 1 | `Extensions/` | Borderline — the composition root (see below) |
| 165 | 8 | `Exceptions/` | Charter-legitimate |
| 144 | 2 | `Resolvers/` | Breach (small) — logic-bearing services |
| 37 | 1 | `Validators/` | Charter-legitimate |

**The breach is concentrated and unambiguous: `Services/` (10,092 LoC, 94 files) is a full runtime
engine reference implementation living inside a `.Core`.** Representative contents (each domain-decisive,
not mechanical): `InMemoryRuntimeCheckpointCommitStore` (576), `RuntimeContainerScopeService` (440),
`SimpleActivityExecutionContext` (428), `Coalescing/RuntimeCoalescingSession` (392),
`RuntimeActivityInputMaterializer` (339), the scheduler work handlers
(`WorkflowScheduleActivitySchedulerWorkHandler` 318, `WorkflowCheckpointSchedulerWorkHandler` 318,
`WorkflowCompleteActivitySchedulerWorkHandler` 271, …), `WorkflowSchedulerDrainer` (309),
`WorkflowDrainOrchestrator` (215), `BookmarkResumeDispatcher` (193). These are the ADR-0029 slot bodies
and the durable-drain machinery — the execution engine itself.

**The composition root confirms it in its own words.** `Services/`… is registered by
`Extensions/RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntimeCore()` (190 LoC), whose doc
comment describes itself as *"Host-agnostic composition root for the workflow runtime… Registers the
runtime execution spine — stores, scheduler, drainer, coordinator, command processor, the
workflow/activity execution pipelines, and every scheduler work handler"* and repeatedly refers to
*"the reference implementation."* A `.Core` is not supposed to *have* a reference implementation of an
engine; it is supposed to *declare the contracts* an engine implements.

### Why the mechanical guards miss this

`Elsa.Workflows.Runtime.Core.csproj` references only other `.Core` projects
(`Activities.Runtime.Core`, `Expressions.Core`, `Serialization.Core`) plus
`Microsoft.Extensions.Logging.Abstractions` — so it **passes** `ArchitectureGuardTests`'
dependency-envelope and heavy-package checks. The breach is **semantic, not mechanical**: the
dependency *envelope* is clean, but the *content* is an engine. No current guard test asserts "a
`.Core` contains only contracts/models/thin-utility," so this drifted in silently. This is the same
false-affordance failure class ADR 0029 calls out for the pipeline — types that look like one thing
(contracts layer) but are another (engine).

## Blast radius

13 source projects reference `Elsa.Workflows.Runtime.Core` (22 references incl. tests). A large share
consume **only contracts + models** — the activity projects (`ControlFlow`, `Flowchart`, `Primitives`,
`Sequence`, `Scheduling`), `Expressions.JavaScript.Jint`, and the persistence project — and would no
longer need to transitively drag the engine once the split lands. The engine-consuming projects
(`Runtime.Api`, `Runtime.Resumption`, `Runtime.Scheduling`, `Runtime.JavaScript`, `Activities.Runtime`)
would reference the new implementation package. This is a meaningful, cross-cutting change — which is
exactly why it is **disposition-only** here and routes to the runtime architect.

## Disposition — proposed contracts-vs-engine split

Split `Elsa.Workflows.Runtime.Core` into two projects along the charter line, preserving the `.Core`
NuGet identity for the contracts half (§2.16 refactor-cost — most consumers keep their reference):

- **`Elsa.Workflows.Runtime.Core` (Layer 1, retained identity)** — keeps `Contracts/`, `Models/`,
  `Constants/`, `Exceptions/`, `Middleware/` (the pipeline *contract* + `[RuntimeMiddleware]`
  attribute), `Builders/`, `Validators/`, and genuinely-thin `Extensions/`.
- **`Elsa.Workflows.Runtime` (new Layer 3 implementation, no `.Core` suffix)** — takes `Services/` and
  `Resolvers/` (the engine) and the `AddWorkflowRuntimeCore` composition root (renamed
  `AddWorkflowRuntime`). Contract-only consumers drop to the `.Core` reference; engine hosts reference
  this package.

This mirrors how every other domain in the repo is split (`.Core` contracts + sibling implementation)
and directly aligns with the ADR-0029 slot-decomposition direction and `specs/084`: the slot-bound
handlers are engine implementation and belong in the implementation package, while the slot contracts
and `RuntimePipelinePlan` stay in `.Core`.

### Open sub-question for the runtime architect

`Models/` at 6,246 LoC is charter-*permitted* (models are allowed in `.Core`) but is large; a
secondary pass should confirm none of those types carry domain-decisive branching that would make them
engine state rather than data. This audit does not block on that; the primary, unambiguous breach is
`Services/`.

## Recommendation

1. **Accept the contracts-vs-engine split as the direction** (proposed ADR 0033), executed as a
   separate, runtime-architect-approved change sequenced with `specs/084`, not in W21.
2. **Add a semantic guard** when the split lands: a test asserting `Elsa.Workflows.Runtime.Core`
   contains no `*Service` / `*Handler` / `*Dispatcher` engine types (or the inverse — that the engine
   types live only in the implementation assembly), so the breach cannot silently recur.
3. Keep the decision recorded as **proposed** until the runtime architect ratifies, per the
   [Runtime Execution Seam](../program-goals/runtime-execution-seam.md) bucket's ownership.

## Links

- Finding source: [`review-modularity.md` §MD-6 + Open Question 2](elsa-4-architecture-review-2026-07/review-modularity.md)
- Proposed decision: [ADR 0033](../adr/0033-runtime-core-splits-contracts-from-engine.md)
- Aligned work: [ADR 0029](../adr/0029-runtime-execution-flows-through-the-pipelines.md) ·
  [`specs/084`](../../specs/084-runtime-move2-slot-decomposition-remainder/plan.md) ·
  [Runtime Execution Seam](../program-goals/runtime-execution-seam.md)
- Gates: framework §2.1, §2.17, §2.16
- Bucket: [Elsa 4 review remediation](../program-goals/elsa-4-review-remediation.md)
