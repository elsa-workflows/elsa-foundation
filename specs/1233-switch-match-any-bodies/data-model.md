# Data model: Switch MatchAny case activities

## Authored and executable structures

Retain `elsa.switch.structure` and the existing supported schema readers. An authored case holds `caseId`, `match`, optional `condition` and optional `activity`. Default is a separate optional child, and `mode` chooses existing MatchFirst/MatchAny semantics. Executable cases hold the corresponding label, body node ID and condition input key. Publishing remaps every body node ID and pins ordinary typed condition bindings.

No generic `ActivityNode` child collection, workflow-definition field, global join state or new persistence entity is introduced. Retained schema-1 literal matching and schema-2 case conditions keep their current upgrade rules.

## Invocation selection

For one committed Switch invocation, derive an ordered list of matching executable cases from its immutable inputs. The list is not a live query over workflow variables. A selected empty case contributes its label but no child execution. If the list is empty, Default supplies the only selected outcome and optional child.

## Child identity and state

Each populated selected case is a direct child of the Switch activity execution with independent execution identity and branch provenance. The selected body IDs and parent execution define the completion accounting boundary. Read persisted direct-child records through the existing store contract. Nested descendants, another parent invocation, unselected cases and Default on a matching run do not contribute.

| Child disposition | Contribution to normal Switch success |
|---|---|
| Completed with an ordinary or explicitly empty selection | That selected body succeeded; its case label remains selected |
| Running, scheduled or suspended | Unfinished; Switch defers |
| Faulted | Unfinished; preserve blocking incident and existing recovery admission |
| Cancelled | Not successful; no normal completion is inferred |
| Superseded, stale or duplicate notification | Not an additional success; never fills another selected body's place |
| Completed with Break | Immediate Break control exit with atomic live-child subtree cancellation |

## Parent transitions

1. Materialize all MatchAny conditions in case order and commit the invocation snapshot. Input failure starts no bodies.
2. Schedule selected populated children in case order and defer; if none exist, complete directly with the selected labels or Default.
3. On each owned child completion, reconstruct selection and inspect selected direct-child state. Defer until all selected bodies succeeded, then return the ordered labels once.
4. On Break, stage live-child subtree cancellation and Complete(Break) together. No ordinary case selection is emitted.
5. Enclosing cancellation and workflow Finish follow the existing runtime's terminal lifecycle. Recovery uses committed records and queued parent work, without evaluating conditions again or rerunning successful steps.

Fault processing and operator re-execution are not new transitions in this model. Switch does not acquire the opt-in for rescheduling processed faulted children.
