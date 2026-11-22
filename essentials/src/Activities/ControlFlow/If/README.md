# Elsa.Activities.If

Boolean control-flow `If` composite activity module. The activity evaluates a `Condition` boolean
input and schedules either the `Then` branch (when true) or the `Else` branch (when false) through the
named `If.Then` / `If.Else` child slots and the runtime composite-activity seam. It completes with a
`True` or `False` outcome reflecting the evaluated condition.

An unbound `Condition` resolves to `false` (the default of `bool`), so an `If` with no condition wired
up runs the `Else` branch and emits `False`. This is expected behavior, not a bug.

The runtime activity class (`Activities/If.cs`) references only the runtime contract surface. The
design-side `IfStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
