# Activity Data Selection: program and delivery plan

**Status:** Owner-authorized delivery plan, 5 November 2026; reviewed production artifacts are in [spec 1215](../../specs/1215-activity-data-selection/spec.md).
**Date:** 2026-11-05
**Planning baseline:** `origin/main` at `8e2acfd4d85a937e888ad378fdb7c6a742a80bda`.
**Establishment refresh:** `697e9a342998282a08a598ad7137b2e90c9435c2`; source gaps and existing issue ownership rechecked.
**Program:** [#3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882) · [current goal](../program-goals/activity-data-selection.md).
**Existing feature:** [#3094 — Author activity-output references in Studio](https://github.com/elsa-workflows/elsa-foundation/issues/3094).
**Program-goal state:** **Activity Data Selection**, authorized by the owner's "Proceed as proposed" instruction.
**Product owner:** Sipke. Codex coordinates delivery through M5 under the 5 November all-milestones continuation; executable ownership is claimed per issue.

## Recommendation

Deliver the focused program for selecting data at activity inputs. Start with a bounded proof in the real
Studio/Workbench, then review the source/binding contract and distribute production work. The interaction
direction is clear enough to build against; a second standalone visual prototype would not resolve the
remaining integration questions.

The ordinary journey is: open a consuming activity's input, choose data, select an earlier activity's
nested field, save, and run. Users manage neither an intermediate variable nor a handwritten expression.
An expression remains available when they want to transform or combine values.

Reuse #3094 rather than opening another feature for the same gap. Coordinate its existing compensation
dependency while linking it to the proposed program; preserve its narrower deliverable where it can ship
independently. Share existing expression infrastructure with [Studio Expression Developer Experience
#2310](../program-goals/studio-expression-developer-experience.md), whose charter covers text-expression
authoring. This program owns the general data-selection experience on eligible ordinary inputs.

## Product direction and first-release boundary

The owner agreed to input-centered selection, readable references, nested-field discovery and an expression
escape hatch. Start with the compact inline chooser shown in the discussion. A larger data sidebar remains
an alternative to evaluate only if the composed journey demonstrates a need.

Use the existing Studio property row and workflow-input argument picker as the product reference. The
intended interaction is an input-adjacent action, a browsable source/member tree and a readable selected
reference. [Logic Apps property tokens](https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-content-type#create-tokens-for-json-properties)
and [n8n's UI mapper](https://docs.n8n.io/build/work-with-data/reference-data/use-the-ui-mapper.md) provide
interaction references. The discussion sketches are illustrative; actual Studio evidence comes from M1.

The proposed first release includes:

- A consistent **Choose data** action on eligible inputs, using existing Studio controls and contributions.
- One source browser for available activity outputs, workflow inputs, visible variables and current-item
  sources exposed by the owning structure. Preserve any existing source-specific restrictions.
- Whole-output and nested-member selection, with readable producer/output/member labels and source navigation.
- The same source selection in supported compact and expanded expression editors, with generated bindings.
- Canonical save/load, graphical/code-view equivalence, document undo/redo and reference-aware graph editing.
- Authoritative availability, types, cardinality and diagnostics across the affected native and BPMN surfaces.
- A useful metadata-only experience before a test run and in hosts without optional language tooling.

Begin with declared types and member metadata. Authorized test-run examples are a separately scheduled
increment after the core journey: retain their benefit without making diagnostics access or a prior run a
prerequisite for choosing a field. Unknown shapes remain explicit; an explicit member path needs validation
and a clear account of what can be checked before execution.

Variables continue to represent intentional mutable state and accumulation. Existing workflows, manual
expressions and provider-owned inputs retain their meaning. Basic selection should be language-neutral;
choosing a field must not silently make JavaScript a required host module.

Collection iteration and aggregation use existing explicit constructs. Automatic item fan-out, guessed
field matching, globally latest output lookup, a new scripting language, general JSON-schema inference,
new expression-editor infrastructure, blanket output retention, releases and deployment are outside this
proposal. Source examples must not run a producer or evaluate a user's expression to fill a picker.

## Current evidence and gaps

These are source findings, not execution acceptance. The live issue/PR observations were checked on
2026-11-05 and must be refreshed before claiming work.

| Area | Existing basis | Work or uncertainty |
|---|---|---|
| Value semantics | Accepted [ADR 0045](../adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) distinguishes immutable results from variables and resolves causal occurrences. | Preserve its rules; a reference never means the newest execution of a node. |
| Direct result input | [RuntimeInputBindingCompiler](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs) compiles `ActivityResult` with producer, projection, scope and optionality. | It selects a declared projection key; arbitrary nested member paths are not currently a field on that reference. |
| Expression bindings | [ExpressionDefinition](../../Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs) supports activity-result parameters. [#3501](https://github.com/elsa-workflows/elsa-foundation/issues/3501) and merged [PR #3502](https://github.com/elsa-workflows/elsa-foundation/pull/3502) delivered a workflow-input argument picker. | Reuse portable parameter handling. Define nested selection separately from the installed language adapter used for transformations. |
| Source discovery | [PersistedExpressionAuthoringContextSource](../../Essentials/src/Workflows/Design/Api/Services/PersistedExpressionAuthoringContextSource.cs) supplies typed available outputs; [expressionToolingClient](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/expression-tooling/expressionToolingClient.ts) projects them to Studio. | Prove current-draft, non-code input usage. CLR member discovery is bounded; it is not general dynamic JSON-schema discovery. |
| Main canvas | #3094 identifies Sequence/structure-owned availability and omitted Flowchart producers. | Prove a simple linear Flowchart in the first milestone; specify general graph availability before production expansion. |
| Editor and wire | [module](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx) registers existing reference editors; [activityInputWire](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/activityInputWire.ts) preserves structured Variable/Input/Connection payloads. | Add the missing result authoring contribution and lossless structured result serialization; a new picker alone is insufficient. |
| Graph operations | [duplicateActivities](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/duplicateActivities.ts) remaps node/structure identities. | Define traversal/remapping of producer references inside inputs and expression parameters. |
| Input constraints | [ActivityPropertiesPanel](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx) honors secret-only, options-provider, masked and read-only contracts. | Apply Choose data only where the target's source contract permits it. |

#3094 was open, without an assignee, comments or a native parent at planning and establishment refresh. No open
covering PR was found. Establishment links it to #3882 while preserving its original scope. #2310's GitHub parked label is stale relative to its accepted
23 October restart in [ADR 0080](../adr/0080-elsa-4-simplification-decisions.md); its bookkeeping is separate
work. This program must not silently enlarge or repair that program.

The M2 handoff refresh confirms that #3094 has no native blockers. Its original "before compensation M3"
sequencing note is historical: [#3046 completed](https://github.com/elsa-workflows/elsa-foundation/issues/3046#issuecomment-6297263367)
with #3126/#3127 and Implemented spec 258. Preserve compensation-handler own/prior forward-result visibility
through its structure hook and existing causal/restart controls; do not wait for closure of the broader
compensation program or absorb its later qualification work. M1/M2 and shared-authority gates still apply.

The [ADR 0089 amendment PR #3420](https://github.com/elsa-workflows/elsa-foundation/pull/3420) was draft and
unmerged when inspected. The picker must not settle proposed ambient activity-name/occurrence syntax.
Broader ADR 0046 coercion also remains proposed outside its accepted output-capture slice. Link existing
decisions; route any needed semantic amendment through its normal agreement process.

## Milestones and handoff gates

The owner's 5 November instruction, **"Set a goal to deliver all milestones"**, continues this plan
through M1–M5, normal review/merge and resulting-main qualification. The
[program receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3882#issuecomment-6667218844)
records that authorization without expanding scope or waiving milestone gates.

### M0 — Establish the scoped program ([#3883](https://github.com/elsa-workflows/elsa-foundation/issues/3883))

Record an owner amendment to ADR 0080 D0 naming this program and its sequencing, add its concise goal file
and registry row, and open the program issue. Proposed sequencing: run alongside existing active programs,
preserve consolidation's priority, coordinate shared Studio/runtime paths, and reactivate no unrelated work.

Reuse #3094 and preserve its compensation relationship. Initially elaborate only the proof and the next
contract/spec work. Create new issues as `needs-triage`; the maintainer controls readiness. Use existing
issue hierarchy and dependency records, with one integration lane. A separate Project board is optional
and must not delay the first useful result.

**Exit:** one discoverable charter, existing-feature links, a delivery lead and a bounded first assignment.
The owner authorized establishment and the proposed sequence on 5 November. Program establishment is not
product delivery; the first worker's code begins after merged establishment records and resulting-main qualification.

### M1 — Prove one complete customer journey ([#3884](https://github.com/elsa-workflows/elsa-foundation/issues/3884))

Use an isolated research worktree and owned disposable hosts/stores. Exercise production authoring,
publishing and runtime seams with deterministic customer data. Reuse cataloged activities or a disposable
authored activity; keep proof fixtures out of the shipped activity catalog.

Prove a **simple linear Flowchart** producer-to-consumer mapping and a **Sequence** control case. The
producer returns a customer object with a nested `customerId`; selecting a separately declared scalar
output is not a substitute for nested selection.

The proof must demonstrate:

1. Browse the declared customer fields before running the workflow.
2. Select Customer ID at the consumer input without creating a variable/capture or typing an expression.
3. Show readable provenance and a canonical binding with stable source identity.
4. Save, reopen, visit code view and apply it back without changing the mapping's meaning.
5. Publish and run through the real host; observe the expected consumer input value.
6. Add a small explicit transformation using the same source picker and generated expression bindings.
7. Exercise the basic selection path in a host without optional JavaScript/code-editor modules.
8. Rename the producer and inspect one unavailable/deleted-source case.

Resolve two questions with concrete findings:

- **Nested representation:** prefer a language-neutral member projection. Evaluate the smallest extension
  to the existing result/projection contracts; compare it with explicit expression lowering. Record types,
  optionality, path validation and durability consequences. Check CLR member names against camelCase and
  explicit serialized JSON names, including conversion-plan source typing. Do not freeze a new wire contract from a sketch.
- **Availability:** establish the source of truth for unsaved/current draft revisions, permissions and
  linear Flowchart order. Characterize joins, branches and backedges for the production spec; do not
  approximate them from canvas coordinates or broad reachability.

**Exit:** real persisted-run evidence, inspected UI evidence, a proposed binding/source contract and a
clear result for each criterion. If a criterion fails, name the failing seam and amend the next unit.
Keep the experiment bounded to these questions; further work becomes a scoped spec. Research code is
adopted only through the reviewed production spec and normal delivery process.

### M2 — Review the production specification and contracts ([#3885](https://github.com/elsa-workflows/elsa-foundation/issues/3885))

Follow [specification → plan → tasks](../contributing/task-paths.md#feature-development), reviewing each
artifact before the next. Allocate the next [spec number](../reference/spec-lifecycle.md) against the tree,
archives and open PR allocations when this phase starts.

The reviewed [spec 1215 package](../../specs/1215-activity-data-selection/plan.md) records the qualified
M1 research exit, contract choices, 51 tasks and remaining exact-head review/merge/resulting-main gates.
Approval takes effect on M2 merge; product acceptance remains M3–M5 work.

Specify the source tree, target eligibility, member/path identity, source labels, types, nullability,
cardinality, host capability, canonical binding and compatibility diagnostics. Define current-revision
invalidation and loading/empty/unavailable/denied states. Keep unknown distinct from incompatible.

Define whole-output and nested-member behavior without optional language dependencies. Specify explicit
expression insertion through existing syntax adapters and portable bindings, including parameter-name
collisions and source/editor undo continuity. Shared code remains language-neutral.

Treat Flowchart availability as its own bounded concern, as #3094 requests. Specify definite completion
on every valid incoming path, conditional absence, parallel joins, loops and scope crossings. Reuse the
owning activity structure's extension seam and the same authority as publication validation. Scope any
unresolved architectural change separately; keep basic authoring work independently deliverable.

**Exit:** reviewed spec, plan, contracts and dependency-ordered tasks, with no unresolved contract question
that parallel workers would otherwise answer differently. Production work begins through the normal gates.

### M3 — Deliver the picker and binding path

Deliver small end-to-end slices: available producer/whole output first, then nested members and Flowchart
coverage as their contracts become ready. Complete #3094 only against its reconciled acceptance criteria;
retain the larger program's remaining obligations.

Integrate Choose data, a readable reference, replace/clear/fixed-value transitions, source navigation and
useful diagnostics. Wire canonical serialization, validation and publication in the same delivery chain.
Reuse the existing SDK/editor contributions, authorization and document edit mechanisms.

**Exit:** a usable mapping feature through persisted real runs on the specified surfaces, including a
minimal host without optional language modules. Focused tests and independent UX inspection pass.

### M4 — Complete expression and editing behavior

Expose the shared picker in supported compact/expanded expression surfaces. Insert source text and its
bindings as one document operation. Preserve existing manual source, unsupported definitions and missing
module behavior. Do not invent another code editor or an ambient activity-reference API.

Complete rename, move, duplicate, paste, delete and undo/redo semantics. Copying a producer and consumer
together remaps their internal reference; copying a consumer alone preserves or diagnoses its external
reference. Same-label producers remain distinguishable. Invalid mappings remain inspectable and repairable.

**Exit:** authored mappings and expressions retain their meaning through graph edits, saving and reopening;
actionable failures replace silent substitution or value loss.

### M5 — Qualify the complete experience and close delivery

Run the matrix below against current committed production code. Audit every reachable use of the shared
input surface: native Flowchart, Sequence, reusable activity-definition graphs, and BPMN where that editor
is actually composed. Record source restrictions and unsupported surface capabilities explicitly.

Provide a short customer example and update existing authoring documentation. Run affected suites, real
backend e2e journeys where publishing/runtime changes, applicable architecture/maps checks and required CI.
Use the [Studio UX acceptance](../contributing/studio-ux-acceptance.md) loop and retain downloadable visual
evidence for the reviewed commit. Verify the resulting `main` after merge and reconcile issue/spec/program
records. Failed, skipped and uninspected states remain distinct from passed acceptance.

**Exit:** the complete scoped matrix passes and required independent review and resulting-main evidence
exist. Qualification is not inferred from closed child issues, fixture screenshots or the earlier proof.

## Worker boundaries

| Role | Owns | Start condition |
|---|---|---|
| Delivery/integration lead | M1 proof, shared contract decisions, dependency order, owned composed hosts and final integration | Program establishment and first bounded assignment |
| Foundation worker | Authoritative source discovery, structure-owned availability, type/path projection, compilation/runtime validation where needed | Reviewed contracts; separate shared contract commits from implementation |
| Studio picker worker | Shared source browser, reference display, eligible-input contribution, canonical wire and document integration | Committed source/binding contract; coordinate the main inspector edit serially |
| Expression/editing worker | Installed-syntax insertion, portable parameters, compact/expanded continuity and reference-aware graph operations | Committed binding contract and picker integration seam |
| Independent acceptance reviewer | User-goal, functional and rendered acceptance of the integrated result | Current committed host/run evidence; separate from author acceptance claims |

Use separate worktrees and claims for executable leaves. Parallelize Foundation and picker work only
after the contract is committed. Sequence shared model/inspector edits and composed builds/runs. Keep the
next deliverable small; elaborate later leaves from proof findings rather than creating a large speculative
backlog. Coordinate compensation and existing expression work through their actual shared records.

## Acceptance matrix

| Journey | Observable pass condition | Evidence |
|---|---|---|
| Basic mapping | Whole customer and nested Customer ID work without a synthetic variable, output capture or handwritten source. | Real Flowchart and Sequence author/save/reopen/publish/run journeys; canonical wire roundtrip regressions. |
| Expression composition | The same source can be transformed or combined; generated parameters are correct and edits remain one undo operation. | Compact/expanded editor regression and one persisted run. |
| Graph editing | Rename, same-label producers, move, deletion, pair/consumer-only copy and undo preserve or explicitly invalidate the intended source. | Focused reference-remap/document tests and one composed edit/reopen journey. |
| Paths and iterations | Customers A/B in two iterations feed A/B correctly; unavailable branches are not offered as guaranteed required data. Collections retain their cardinality. | Causal/availability regressions plus real loop and conditional/join journeys. |
| Durable continuation | Suspend/restart between producer and consumer retains the correct committed result and intended retry input semantics. | Actual persistent-host restart journey in a supported durable mode. |
| Restricted inputs | Secret-only, constrained/provider-owned, masked, denied and source-owned/read-only inputs preserve their permitted behavior and values. | Mounted inspector coverage and composed restricted/read-only checks. |
| Partial information | Unknown/truncated shapes, no sources, loading, denied and unavailable states are distinguishable; late responses cannot replace a newer activity/scope's sources. | Focused revision/cancellation tests and one composed retry/cancel journey. |
| Host composition | Basic selection works without optional text-language modules; missing modules preserve existing authored source and references. | Minimal-host composition and persisted-binding proof. |
| Rendered experience | Keyboard select/cancel works, focus returns correctly, references remain readable at narrow width, and all new states fit light/dark presentation. | Feature-specific composed journey and current-head screenshots independently opened and inspected. |

If runtime examples are scheduled later, their acceptance adds authorized diagnostic access, visible
example provenance, redaction and invalidation on run/permission changes. Contract fields and observed
sample fields stay distinguishable; samples never silently become the published schema.

## Governance and validation boundary

Use [program-goal rules](../contributing/program-goals-and-drift-guard.md), [repository hygiene](../contributing/repository-hygiene.md)
and the org-wide [Issue Bus Lite](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md) for establishment,
claims and delivery. This proposal does not create initiative membership for #3094. A future change that
belongs to an existing initiative follows that initiative's agreement rules for that specific change.

Planning considered framework constitution v6.1.0 and Elsa constitution v6.4.0: preserve module boundaries,
artifact-only execution, explicit value roles and meaningful risk-based tests. No exception is proposed.
Draft/provisional framework §2.24 and Elsa §E2.8 versioning/Model X and §E2.9 remain unratified; this proposal
does not ratify them. Recheck affected gates during the formal spec/plan review.

M0 publishes the owner amendment, registry/goal/plan and native tracker links. M1 owns the actual experiment
and its evidence; no workflow run or prototype acceptance is claimed by these establishment records.
