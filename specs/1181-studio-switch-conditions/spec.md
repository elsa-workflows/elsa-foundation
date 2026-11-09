# Switch case conditions

**Status**: Implemented
**Issue**: #3518
**Program bucket**: none/free-flow

## User scenario

An author labels a Switch case Approved, selects an expression syntax, and writes a boolean condition using the same controls and scope tooling as an ordinary activity input. The label and condition are independent. They select a branch, save, reload, publish, and run the workflow. The first true case runs; Default runs if none are true.

## Acceptance criteria

1. Each new case exposes a label, a boolean condition with the standard expression editor/syntax picker, and its branch. Labels and breadcrumbs remain human-readable.
2. Literal booleans, supported expression languages and scoped data references retain normal input typing, conversion, validation and error handling. Reuse each host's existing ordinary-input capabilities and report unavailable language services honestly. Case conditions are compiled bindings, never ad-hoc source evaluation inside Switch.
3. Conditions are evaluated in authored order and short-circuit at the first true value. Later conditions are not evaluated after a match. An unbound predicate is false. A failed evaluated condition records the normal blocking input-failure incident instead of selecting Default.
4. A selected empty case completes with its label outcome; Default handles only no-match. Branch completion and Break propagation retain existing behavior.
5. Saved conditions retain expression syntax and payload through reload, branch edits, label edits, undo/redo, and publication. Moving cases changes priority without changing their identity. Deleting a case preserves every remaining condition and descendant; pending editor/tooling results cannot attach to another case.
6. Both workflow and activity-definition hosts support Sequence, Flowchart and BPMN, keyboard/narrow layouts and read-only/source-owned controls.
7. Existing value/match drafts and pinned executables keep their meaning. Moving an existing draft to condition-capable authoring is explicit and preserves its labels, branches and literal matching until an author chooses a condition. An upgrade that cannot preserve an activation-only Secret reference is refused clearly and leaves the original intact.
8. Conditions reference the right variables and activity results after placement of a reusable activity graph. Published execution requires no Design services.
9. All consumed condition values are durably pinned before activity activation. Worker loss/redelivery/retry after Running uses the same values and selection even if their source variables change. Unconsumed later conditions are not evaluated on recovery.

## Verification

Compiler/runtime tests cover false/true, two true cases, skipped later failures, evaluated errors, fallback, empty branches, legacy artifacts, expression typing, hashing/serialization, and remapped data references. Rendered/browser tests cover real expression controls, syntax persistence, label/branch edits, history, read-only and both hosts/surfaces. A fresh composed Workbench/Studio proof authors, saves/reloads, publishes and executes matching and fallback paths. Complete affected frontend/backend, architecture/maps, hosted review/checks and resulting-main qualification.

## Boundaries

No new expression engine, module, project, persistence provider, workflow-state member, scheduling model or program is introduced. Switch owns its ordered cases and branch semantics. Generic publishing/runtime own typed compilation and materialization. Existing version mechanisms are used; this work does not ratify the provisional catalog/versioning constitutional sections.
