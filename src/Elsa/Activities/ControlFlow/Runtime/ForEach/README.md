# Elsa.Activities.ForEach

Looping `ForEach` composite activity module. The activity evaluates a `Collection` input and runs its
single body activity once per item, through a named body child slot (`ForEach.Body`) and the runtime
composite-activity seam. Each pass exposes the current item (and, by default, a zero-based index) to the
body through a per-iteration variable frame built with the shared loop-frame primitive
(`RuntimeLoopIterationFrameFactory`, ADR 0028).

For each pass the activity chooses a distinct engine iteration identity (`IterationId`) and schedules the
body node under the same `IterationId` in `ActivitySchedulingProvenance.IterationId`, carrying the pass's
item/index as protected envelopes on a typed `LoopIterationScopeRequest`. When the runtime starts the body,
`RuntimeContainerScopeService` validates and consumes that request to layer a fresh durable per-pass frame
as the innermost frame
on the body's visible chain — so a body input bound to a `Variable` expression
`{ referenceKey: "currentItem", declaringScopeId: <ForEach node id> }` resolves to the current pass's item
through the real expression evaluator (the variable name doubles as the reference key under #296's wiring).
This wires the `#259` loop-frame primitive into the execution path. On each body completion the activity recovers the
finished pass from the completed child's iteration id, advances to the next item, and when the collection
is exhausted completes with a `Done` outcome. A null or empty collection (or an empty body) short-circuits
to `Done` without scheduling the body.

The runtime activity class (`Activities/ForEach.cs`) references only the runtime contract surface. The
design-side `ForEachStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
