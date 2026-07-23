# Plan: Author-time activity output → variable capture

**Status:** Draft for review
**Date:** 2026-07-23
**Scope:** elsa-foundation (backend) + elsa-foundation-studio (Inspector UI)
**Related decisions:** [ADR 0045](../adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) · [ADR 0046](../adr/0046-output-binding-coercion-uses-pinned-value-representations.md)
**Related plan:** [Typed Output Binding Coercion PRD](./output-binding-coercion-prd.md)

> File paths below are repo-relative within **elsa-foundation** unless prefixed with `elsa-foundation-studio/`.

## Context

The Studio Inspector shows a read-only **Outputs** list (display name · type · description) but gives the
author no way to answer the obvious next question: *"capture this activity's `Result` into a variable."*
Today the only variable-write authoring is the **Set Variable / Set Output** intrinsic (a separate node);
cross-activity flow otherwise happens on the consuming side via input references. There is no per-activity
output-capture editor for ordinary activities (ReadLine, HTTP, file, DB, message activities — exactly the
cases the coercion PRD calls out).

This plan makes that binding **authorable in the Inspector**, end to end.

## Feasibility summary

This is **mostly wiring existing, proven plumbing**, not a from-scratch build. No new persisted authoring
field, no new compiled type, and no new runtime execution path are required.

- **ADR-0045 permits it.** It forbids *activity code* silently mutating variables (only the `Set` intrinsic
  writes from the activity's own execution). An output capture is a **compiled, engine-owned edge on the
  node**, not an activity-code write, and ADR-0046 legitimizes it as sharing the input-binding coercion seam.
- **The backend capture pipeline already exists** — authored field, compiler, compiled model, inspection
  view, conversion machinery, and the runtime projector that commits the capture in the completion
  checkpoint. It is wired **only for reusable-activity boundary nodes** today; ordinary/leaf activities get a
  hardcoded-empty `outputCaptures`.
- **The Studio wire already round-trips `ActivityNode.outputs` losslessly** (a passthrough channel, currently
  always `[]`). The input fold/expand machinery is a mature template to mirror for outputs.

## Backend work

The authored field `ActivityNode.Outputs : IEnumerable<ArgumentState>`
([src/Elsa/Workflows/Design/Core/Models/ActivityNode.cs:29](../../src/Elsa/Workflows/Design/Core/Models/ActivityNode.cs))
already exists and round-trips. The binding ("output → variable") is an `ArgumentState` whose
`Value.ExpressionType == "Variable"` carrying a `VariableReference`, with optional `Conversion` — identical
to an input binding.

1. **Wire capture compilation into the leaf path.**
   [`ExecutableNodeCompiler.CompileNode`](../../src/Elsa/Workflows/Publishing/Api/Services/ExecutableNodeCompiler.cs)
   hardcodes empty `outputCaptures` (~line 166). Instead invoke the existing
   [`RuntimeOutputCaptureCompiler`](../../src/Elsa/Workflows/Publishing/Api/Services/RuntimeOutputCaptureCompiler.cs)
   against `activity.Outputs`, the leaf activity's result-projection contracts (already built in
   `ExecutableNodeCompiler.cs` ~lines 311-330), and `state.Variables`.
2. **Adapter for leaf projections.** `RuntimeOutputCaptureCompiler.CompileBoundaryOutputs` takes
   `IReadOnlyCollection<ActivityOutputContract>` (the reusable-activity contract shape). Leaf projections are
   `ActivityResultProjectionContract` — add an overload/adapter so the same compiler serves both. All
   conversion resolution (`ValueConversionPlanResolver` / `AuthoredValueConversionMapper`) is reused unchanged.
3. **Thread the dependency + storage aggregation.** Inject `RuntimeOutputCaptureCompiler` into
   `ExecutableNodeCompiler` (today only `WorkflowExecutableCompiler` holds it) and include leaf captures in
   the storage-driver requirement aggregation
   ([src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs](../../src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs) ~line 228, which already folds
   `OutputCaptures.Values`).
4. **Validation (mirror the boundary rules).** Workflow-scope Variable target only (currently hard-required),
   `TransientResource` source rejection (`VF-ACT-005`), variable existence + type/coercion compatibility at
   publish. **Open decision:** keep workflow-scope-only or allow container/nested-scope targets.
5. **Contract gate.** Ratify the output-capture slice of ADR-0046 (this branch amends its status). ADR-0045's
   reconciliation list flags the active-output/capture model (specs 060/061) as unfinished; keep the broader
   reconciliation separate from this slice.

## Studio work (elsa-foundation-studio)

The wire channel already exists; the machinery to fold/expand and edit does not.

1. **Authored output-capture shape + fold/expand.** Add an `outputs` counterpart to the input machinery in
   `elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/activityInputWire.ts` — mirror
   `canonicalizeActivityNode` / `expandActivityNode` (and `toWireArgumentState`) so authored per-output
   captures fold into `node.outputs[]` on save and expand back on load. The compiled
   `WorkflowExecutableOutputCapture` (client `workflowTypes.ts`) is a good field template.
2. **Wire shape (one output binding).**
   ```jsonc
   // ActivityNode.outputs[] element
   {
     "referenceKey": "Result",                       // the output's referenceKey
     "value": { "value": { "referenceKey": "orderId", "declaringScopeId": "workflow" },
                "expressionType": "Variable" },
     "conversion": { "mode": "auto" }                // optional; omitted when default
   }
   ```
   Unbound outputs simply have no entry.
3. **Inspector editor.** Evolve `ActivityOutputsPanel` from a read-only list into an editor. For each
   browsable declared output (`descriptor.outputs`): render the output name + type, a **variable-target
   picker**, and an optional **conversion control**. Reuse, don't reinvent:
   - the scope-aware picker from `IntrinsicVariablePicker` (`IntrinsicInspector.tsx`), fed by
     `useScopedVariableAnalysis` / `analyzeScopedVariables` — the same visible-variable discovery inputs use;
   - `ConversionControl` + the `conversionSettings.ts` model for the optional coercion mode/profile.
4. **Empty state.** "Select a variable…" = not captured. Keep the panel quiet when nothing is bound so the
   simplified-Inspector goal holds.

## UX

Each output row mirrors an input `PropertyRow`, but the *value* side is fixed (the activity's output) and the
*target* is what the author chooses:

```
Outputs
  Result            String
    Capture into  [ orderId · workflow ▾ ]     ⚙ (conversion: Auto · String → String)
  Response          HttpResponseMessage
    Capture into  [ Select a variable… ▾ ]
```

The conversion affordance is the same collapsed `⚙` chip pattern inputs use, so the panel stays clean until
an author opens it. Only workflow-scope variables appear until/unless the backend accepts container scopes.

## Open decisions

1. **Capture-target scope** — workflow-scope-only (matches the current backend rule, simplest) vs. also
   container/nested scopes (needs a backend validation change). Recommend **workflow-scope-only for v1**.
2. **Interim read-only list** — the read-only Outputs list already merged on the studio
   `activity-details-ui-simplify` branch can stay as a stepping stone or be dropped. Recommend **keep it**;
   this feature upgrades it in place.
3. **ADR-0046 ratification form** — this branch records a scoped amendment accepting only the output-capture
   slice, leaving the broader coercion model proposed. Confirm that framing.

## Suggested delivery sequence

1. Backend: leaf-path capture wiring + adapter + validation (behind the existing publish flow; no UI). Verify
   with a compiled-inspection test that a leaf node with an authored workflow-scope Variable output emits a
   `RuntimeOutputCapture` and the runtime projector commits it.
2. Studio: outputs fold/expand in `activityInputWire.ts` + round-trip tests (mirror the input-wire tests).
3. Studio: the Inspector editor (`ActivityOutputsPanel` → editor) + tests, reusing the intrinsic picker and
   conversion control.
4. E2E: author a capture in the Inspector, publish, confirm the Executable Inspector's read-only "Output
   captures" view shows the pinned plan, and a run writes the variable.

## Verification

- Backend: `dotnet test` on the Publishing/Runtime capture + conversion suites; a new leaf-capture compiler test.
- Studio: `npm test` (new `activityOutputWire` round-trip + outputs-editor tests) and `npm run build` (type +
  bundle-size gates) from `src/Elsa.Studio.Workflows/Client`.
- Manual: bind `ReadLine.Result` → a workflow variable, run the workflow, confirm the variable is populated;
  confirm visual/JSON/code-first equivalence (PRD goal 8).
