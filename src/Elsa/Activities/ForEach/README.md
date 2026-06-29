# Elsa.Activities.ForEach

Looping `ForEach` composite activity module. The activity evaluates a `Collection` input and runs its
single body activity once per item, through a named body child slot (`ForEach.Body`) and the runtime
composite-activity seam. Each pass exposes the current item (and, by default, a zero-based index) to the
body through a per-iteration variable scope built with the shared loop-scope primitive
(`RuntimeLoopIterationScopeFactory`, ADR 0028).

For each pass the activity chooses a distinct engine iteration identity (`IterationId`), calls
`BuildIterationScope` once with that item/index, and schedules the body node under the same `IterationId`
in `ActivitySchedulingProvenance.IterationId` so the body execution's iteration identity agrees with its
scope. On each body completion it recovers the finished pass from the completed child's iteration id,
advances to the next item, and when the collection is exhausted completes with a `Done` outcome. A null
or empty collection (or an empty body) short-circuits to `Done` without scheduling the body.

The runtime activity class (`Activities/ForEach.cs`) references only the runtime contract surface. The
design-side `ForEachStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
