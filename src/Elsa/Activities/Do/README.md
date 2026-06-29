# Elsa.Activities.Do

Post-test conditional-loop `Do`/`DoWhile` composite activity module. The activity schedules its single
`Body` branch (through the `Do.Body` named child slot and the runtime composite-activity seam) **before**
any condition check, then re-evaluates the boolean `Condition` input **after** every body completion,
scheduling the body again while it holds. Because the first pass runs unconditionally, the body runs **at
least once** even when the condition is false on entry — this is the post-test counterpart to
[`While`](../While/README.md), which checks the condition first and may run the body zero times. The
composite completes with a `Done` outcome once the condition no longer holds.

A body that completes with a `Break` outcome ends the loop early with `Done` without re-checking the
condition. `Break` is recognized by outcome name so the loop takes no hard dependency on a Break activity
module.

Each pass is scheduled with a distinct engine `IterationId` threaded through the body child's
`ActivitySchedulingProvenance.IterationId` (ADR 0028 / #259), so every body execution records a distinct
`ActivityExecutionState.IterationId`. `Do` is a condition-only loop with no current item or index, so it
declares no per-iteration item variable; the iteration identity is its per-pass loop state.

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
start-time value and a `Do` whose condition reads such a variable will **not** terminate until #286
(mid-run workflow-variable write-back) lands. Use an activity output or a container-scoped variable for
the loop condition until then.

An unbound or null `Condition` resolves to `false` (the default of `bool`): a `Do` with no condition
wired up still runs its body once (the unconditional first pass) and then completes, because the
post-completion re-evaluation reads `false`.

The runtime activity class (`Activities/Do.cs`) references only the runtime contract surface. The
design-side `DoStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
