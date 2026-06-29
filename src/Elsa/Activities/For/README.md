# Elsa.Activities.For

Counted-loop `For` composite activity module. The activity runs a single body once for each index in a
numeric range, advancing by a step each pass, through the `For.Body` named child slot and the runtime
composite-activity seam. Each pass exposes the current index to the body as a per-iteration scoped
variable (`index`, #259 / ADR 0028) and is scheduled with a distinct engine `IterationId` that encodes
the index. When the range is exhausted — or empty — the composite completes with the `Done` outcome.

## Range / step semantics

The range is described by three integer inputs plus a flag:

- **`Start`** — the first index, **inclusive**. Defaults to `0` when unbound.
- **`End`** — the end index, **exclusive by default** (the half-open `[Start, End)` convention of
  `for (i = start; i < end; i += step)`). Set **`EndInclusive`** to walk the closed `[Start, End]`
  range. Defaults to `0` when unbound.
- **`Step`** — the amount each pass advances by. Defaults to `1` when the argument is not wired up. A
  **positive** step counts up; a **negative** step counts down.

Boundary rules:

- **Step direction must agree with the range direction.** A step that points away from the end (e.g. a
  positive step with `End < Start`) yields an **empty range**: the body never runs and the loop completes
  immediately. It does not throw and does not loop forever.
- **A wired Step that evaluates to `0` faults the activity** — a zero step can never reach the end, so it
  is a configuration error rather than an empty or infinite loop.
- **An empty range** (`Start == End` with an exclusive end, or a step pointing away from the end) runs
  the body zero times and completes with `Done`.

## Early exit

A body that completes with a `Break` outcome (#261) ends the loop early with `Done`; any other outcome
advances to the next index. `Break` is matched by outcome name so this module takes no hard dependency on
the Break activity module.

## Stateless iteration

The body re-runs as the same executable node with a fresh activity execution per pass. The loop holds no
mutable state across passes: on each child completion it recovers the just-completed index from the
completed child's `IterationId` (surfaced on `ActivityChildCompletedContext.CompletedChildIterationId`)
and computes the next index, so it is safe under the runtime's stateless re-construction of composite
activities.

The runtime activity class (`Activities/For.cs`) references only the runtime contract surface. The
design-side `ForStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
