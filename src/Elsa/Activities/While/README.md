# Elsa.Activities.While

Conditional-loop `While` composite activity module. The activity evaluates a boolean `Condition` input
**before** each pass and, while it holds, schedules its single `Body` branch through the `While.Body`
named child slot and the runtime composite-activity seam. The condition is re-evaluated on entry and
again after every body completion, so the body runs zero or more times. A condition that is false on
entry completes the composite immediately without ever running the body. The composite completes with a
`Done` outcome once the condition no longer holds.

Each pass is scheduled with a distinct engine `IterationId` threaded through the body child's
`ActivitySchedulingProvenance.IterationId` (ADR 0028 / #259), so every body execution records a distinct
`ActivityExecutionState.IterationId`. `While` is a condition-only loop with no current item or index, so
it declares no per-iteration item variable; the iteration identity is its per-pass loop state.

## What the body can change to make the loop terminate

The runtime re-materializes this composite's inputs before every condition re-evaluation, drawing them
from persisted runtime state. The condition therefore terminates the loop only when it reads state the
body actually **persists** each pass:

- an **activity output** the body produces — e.g. a body whose counter activity emits output `count`,
  with the condition `count < 3`, runs three times and then stops; or
- a **container-scoped variable** the body mutates — ADR 0027 scope mutations are written back and
  re-projected into materialization each pass.

**Known limitation (#286):** a **workflow-scope** variable mutated mid-run by the body (e.g. via a
SetVariable activity) does *not* yet flow back into materialization. Workflow variables are seeded at
start only (`RuntimeWorkflowStateSeed` — Seam C has no write-back), so the condition keeps seeing the
start-time value and a `While` whose condition reads such a variable will **not** terminate until #286
(mid-run workflow-variable write-back) lands. Use an activity output or a container-scoped variable for
the loop condition until then.

An unbound or null `Condition` resolves to `false` (the default of `bool`), mirroring `If`: a `While`
with no condition wired up never runs its body and completes immediately.

The runtime activity class (`Activities/While.cs`) references only the runtime contract surface. The
design-side `WhileStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
