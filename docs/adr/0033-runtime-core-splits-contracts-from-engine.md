# Runtime.Core Splits Contracts From The Engine

Status: proposed

`Elsa.Workflows.Runtime.Core` will be split along the framework §2.1 charter line into a retained
contracts-and-models `.Core` and a new sibling **implementation** package that owns the runtime engine.
The `.Core` keeps its NuGet identity and contains only `Contracts/`, `Models/`, `Constants/`,
`Exceptions/`, the pipeline *contract* (`Middleware/` + `[RuntimeMiddleware]`), `Builders/`, and
`Validators/`; a new `Elsa.Workflows.Runtime` (Layer 3, no `.Core` suffix) takes `Services/` and
`Resolvers/` — the scheduler work handlers, drainer, orchestrator, checkpoint-commit stores, coalescing
session, execution contexts, materializers — together with the `AddWorkflowRuntimeCore` composition root
(renamed `AddWorkflowRuntime`). This makes the runtime the same shape as every other domain in the repo:
a `.Core` of contracts an engine implements, plus a sibling package that implements them.

**The problem this fixes.** At branch `1d5bb6bb`, `Elsa.Workflows.Runtime.Core` is 19,029 LoC (grown
from the 15,042 the 2026-07 review measured, because W12 folded the ADR-0029 Move-2 phase logic and
specs/082–083 added the pipeline spine here). Of that, `Services/` is **10,092 LoC across 94 files** of
domain-decisive engine logic — a full runtime reference implementation living inside what §2.1 frames as
a contracts-only layer, and what §2.17 reserves for *mechanical, non-domain-decisive* thin utilities.
The composition root's own doc comment calls it *"the reference implementation"* of *"the runtime
execution spine."* The breach is **semantic, not mechanical**: the project's dependency envelope is
clean (it references only other `.Core` projects + `Logging.Abstractions`, so `ArchitectureGuardTests`
passes), but its *content* is an engine. No guard asserts "a `.Core` holds only contracts/models/thin
utility," so this drifted in silently — the same false-affordance class ADR 0029 flags for the pipeline.
Full evidence and the folder-by-folder charter verdict are in the
[MD-6 charter audit](../reports/elsa-4-w21-md6-runtime-core-charter-audit.md).

**Why the split line is here.** Models are explicitly charter-legitimate in a `.Core` (§2.1), so
`Models/` (6,246 LoC) and `Contracts/` (1,361) stay; the pipeline *contract* and builders stay because
consumers target slots declaratively. Only the logic-bearing `Services/`/`Resolvers/` and the
composition root move. Retaining the `.Core` NuGet identity for the contracts half honors the
refactor-cost test (§2.16): most of the 13 consuming projects reference `Runtime.Core` for contracts and
models only (the activity projects, Jint, persistence) and keep their reference unchanged; only the
engine-hosting projects (`Runtime.Api`, `Runtime.Resumption`, `Runtime.Scheduling`,
`Runtime.JavaScript`, `Activities.Runtime`) take a reference to the new implementation package.

**Alignment with the slot decomposition.** This is the structural completion of ADR 0029 / `specs/084`:
the slot-bound scheduler handlers are engine implementation and belong in the implementation package,
while the slot contracts, `RuntimePipelinePlan`, and `[RuntimeMiddleware]` placement metadata stay in
`.Core`. The split does not change runtime behavior; it relocates types across a project boundary.

**What is committed here, and what is deferred.** This ADR records the **direction and the split line**.
It does **not** execute the split: the move touches 13 consuming projects and the runtime execution
spine, so it is a separately-approved change owned by the
[Runtime Execution Seam](../program-goals/runtime-execution-seam.md) bucket and sequenced with
`specs/084`, not a W21 code change. **Ratification of this ADR sits with the runtime-execution-seam
owner / runtime architect**, not with W21 or Constitution Readiness. A secondary question — whether any
of the 6,246 LoC of `Models/` carries domain-decisive logic that should also move — is left to that
execution unit; it does not block the primary `Services/` split.

**Guardrail against recurrence.** When the split lands it should ship with a semantic architecture test
asserting the engine types (`*Service`/`*Handler`/`*Dispatcher`/`*Drainer`/…) live only in the
implementation assembly and not in `Elsa.Workflows.Runtime.Core`, so a `.Core` cannot silently re-absorb
engine logic — the mechanical dependency-envelope guards cannot catch this class of drift on their own.

**Consequences.** Contract-only consumers stop transitively dragging the runtime engine; the `.Core`
becomes an honest contracts/models layer; the runtime matches the repo-wide `.Core`+impl shape; and the
oversized-outlier finding (MD-6) is resolved structurally rather than by exception. The cost is a
cross-cutting reference update across the engine-hosting projects and a one-time `AddWorkflowRuntimeCore`
→ `AddWorkflowRuntime` rename, both mechanical and behavior-preserving.

**Alternatives considered.** *Grant `Runtime.Core` a charter exception* (accept an engine in a `.Core`)
was rejected: it would legitimize the exact false affordance the framework is trying to remove and would
leave every contract-only consumer dragging the engine. *Leave it and only document the size* was
rejected: MD-6 is a structural finding, and the slot decomposition already needs the contract/impl line
drawn. *Split `Models/` out too now* was deferred, not rejected — it needs the per-type audit above.

## Follow-up

- Execution unit (Runtime Execution Seam bucket, sequenced with `specs/084`): move `Services/` +
  `Resolvers/` + composition root to `Elsa.Workflows.Runtime`; update the 13 consumers; add the
  semantic guard test; behavior-preserving.
- Cross-references: [MD-6 charter audit](../reports/elsa-4-w21-md6-runtime-core-charter-audit.md);
  [ADR 0029](0029-runtime-execution-flows-through-the-pipelines.md);
  [`specs/084`](../../specs/084-runtime-move2-slot-decomposition-remainder/plan.md);
  [Runtime Execution Seam](../program-goals/runtime-execution-seam.md); framework §2.1, §2.16, §2.17.
