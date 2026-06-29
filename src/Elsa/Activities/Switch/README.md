# Elsa.Activities.Switch

Multi-way control-flow `Switch` composite activity module. The activity evaluates a `Value` string input
and schedules the branch of the first case whose declared match value equals it, through per-case named
child slots (`Switch.Case[{match}]`) and the runtime composite-activity seam. When no case matches it
schedules the `Switch.Default` branch. It completes with the matched case's match value as its outcome,
or `Default` when no case matched.

An unbound `Value` resolves to `null`, which matches a case whose match value is `null`/empty if one is
declared, otherwise falls through to the default branch. A selected branch that is empty (or an absent
default) finalizes the composite directly with the matching outcome without scheduling a child.

The runtime activity class (`Activities/Switch.cs`) references only the runtime contract surface. The
design-side `SwitchStructureHandler` (`Internal/`) references `Elsa.Workflows.Design.Core`. The activity
module bridges both `.Core` sub-domains; `Elsa.Workflows.Runtime.*` never references
`Elsa.Workflows.Design.*` (Elsa §E2.2).
