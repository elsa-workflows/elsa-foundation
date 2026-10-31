# Implementation plan

**Branch**: agent/codex/3518-switch-conditions
**Base**: a455c1299b91ce4961804ff1d3904c8eb4cc631a
**Spec**: [spec.md](spec.md)

## Constitution gate

Framework gate index, test/visibility rules (§2.21–23), extension-point maintenance (§2.22), and Elsa §E2.2/§E2.6 apply. Runtime must remain artifact-only and must take no Design dependency. No new projects, packages, heavy dependencies, feature identity or persistence schema are planned. New cross-package models/contracts are public because activity/module authors consume them; implementation helpers remain internal. Existing catalog/version mechanisms are used without asserting ratification of provisional §E2.8/§E2.9. No exception is requested.

## Design

### Structure-owned typed inputs

Switch conditions are activity-owned structure members, not CLR properties and not an untyped expression blob. Add a narrow projection to `IActivityStructureHandler` / `IActivityStructureService`: a structure may project named input definitions plus ordinary `ArgumentState` values, and identify ordinary catalog inputs whose evaluation it owns. Existing handlers default to an empty projection. Design/Core models contain only Design/Expressions types.

`ExecutableNodeCompiler` compiles projected definitions/states with `RuntimeInputBindingCompiler`, exactly as ordinary inputs. Keep them in the existing `ExecutableNode.InputBindings` and `ActivityContract.Inputs`, marked by a new optional `ActivityInputContract.IsStructureInput` flag. Omit false from serialization/fingerprint projection so old documents/hashes are unchanged. The flag distinguishes these inputs from CLR property hydration and allows an activity-owned ordered materialization policy. Switch's payload records condition input keys; it does not compile expression syntax itself. This avoids a second opaque binding store and lets existing placement, linking, inspection and hashing see the bindings.

All evaluation happens in `RuntimeActivityInputMaterializer`, before the existing Scheduled-to-Running checkpoint and before activity activation (ADR 0045 section 3). Ordinary property inputs are materialized normally. Structure inputs are selected through an explicit runtime policy keyed by the owning structure kind/schema. A policy receives the immutable executable and a constrained key-based callback into the same materializer; it cannot resolve services or arbitrary bindings. The callback uses one ordinary closed-scope resolution context, validates/converts/policy-checks each requested input and records it once. Switch's policy requests cases in order and stops at true. All consumed values are committed in the existing `ActivityInputSnapshot.Values` before activation. Unconsumed structure keys are deliberately absent, distinct from an unbound predicate, which is materialized as false.

The runtime Switch reads only pinned inputs. Redelivery/retry after the Running checkpoint never re-evaluates conditions, even if their variables have since changed. CLR property hydration ignores structure-owned contracts and still requires every ordinary property input. A narrow runtime structure-input consumer interface receives the structure-owned values from the ordinary activation-resolved snapshot (after normal external-envelope dereferencing). This avoids re-evaluation or requiring the activity to dereference opaque storage itself. Materialization policies receive readable transient views for selection but persist the original policy-governed envelopes. Missing/duplicate policy ownership fails closed. Secret reads retain their existing activation-only boundary: structure materialization must never resolve a withheld secret. A legacy draft whose Value is a direct Secret reference cannot be upgraded while retaining literal cases; refuse that unsupported upgrade explicitly and leave the original draft intact. Other withheld values retain normal refusal behavior.

Audit executable hashing/fingerprints, serialization, placement/remapping, input conversion linking, closed-scope validation and inspection for the additional typed contracts. Existing binding traversal should work without special Switch logic. Add causal tests proving producer/scope identities are remapped and skipped keys are not reported as null/evaluated values.

### Switch versions and cases

Introduce source-owned Switch version 4.1.0 (above the current 4.0 package-derived versions) and structure schema 2.0.0 using existing `VersionAttribute` / structure registration. Retain the v1 structure reader/handler and runtime matching behavior for pinned old artifacts and existing drafts.

Retain `match` as the display label/slot-name member, add a stable case ID for v2, and add an optional condition argument container. A non-null container means predicate mode even when its value is unbound; clearing an input must retain the container and materialize false. A null/absent container means a retained legacy Value/match case. New cases carry an ID and an explicit false condition. A legacy case has an explicit Use condition action that preserves its label/branch and creates that container.

V2's projection consumes the ordinary Value input: it is not eagerly compiled/hydrated as a CLR property. If a legacy case remains, project Value as a structure-owned binding and materialize it only when that case is reached. If all cases are predicates, omit Value from compilation, expression validation and the ordinary inspector while preserving its authored data for undo/compatibility. A throwing retained Value must not break an all-predicate Switch or an earlier true predicate. V1 retains ordinary Value hydration.

Use a dedicated Upgrade to case conditions action in the shared inspector for v1 nodes when the v2 catalog version is available. One normal mutation atomically changes activity version and structure schema, assigns case IDs, and preserves Value, labels, children, unknown payload members and literal matching. It works in both editable hosts, with undo/redo. V1 refuses predicate fields rather than publishing them with old matching semantics. This does not depend on the reusable-version helper, which cannot migrate structure schemas.

Branch completion resolves the outcome from the selected branch, without re-evaluating conditions; Break behavior is unchanged. Empty selected branches complete with their case label, and evaluated errors fault normally.

### Studio and expression tooling

Add optional condition-member metadata to the existing collection-slot descriptor. The generic collection editor renders a standard boolean input panel for that member, using existing expression contributions, syntax picker, scope/data tooling, validation, conversion and read-only controls. It must not implement its own text-code editor or evaluation language.

Project descriptors/states to the normal panel and write changes back to the case. A condition's input key derives from its stored case ID, not its label/index. React rows/documents and v2 child-slot/navigation addresses are keyed by that ID; retain the current array index separately for mutations. Renaming, moving or deleting cases must not attach pending edits/tooling responses or an open scope frame to another condition/branch. Recompute breadcrumb labels from the resolved current slot. Add Move up/Move down for condition-capable case collections because order now controls priority. Keep v1/Parallel slot addresses and ordinary branch naming behavior unchanged.

Workflow persisted authoring-context and full-draft semantic validation must include projected arguments/definitions and Boolean expected types. Newly added unsaved cases retain the normal editor's local descriptor/variable controls and explicitly unavailable server context until autosave; after save the normal context resolves. Do not fake server context or skip authorization.

Activity Graph currently provides normal input controls and scoped-variable analysis but no persisted expression-tooling client/draft session. Conditions reuse that same supported surface, including syntax choices and honest language-service availability, rather than introducing an unrelated cross-domain authoring API in this unit. Publication/runtime still enforce Boolean types and ordinary binding semantics. Record this pre-existing language-service limitation separately; do not claim server completion/hover for Activity Graph. The composed graph proof must actually publish/place/run the conditions and their data references.

Both hosts share the inspector and preserve all existing history/persistence paths. Canvas and breadcrumbs continue to show the case label. Cover Sequence/Flowchart/BPMN, narrow keyboard use, read-only, label edits and removal.

## Verification and delivery

1. Causal compiler/runtime and rendered regressions first; record baseline red before implementation.
2. Focused control-flow, publishing/placement, typed-input/runtime hash/serialization and tooling tests; affected frontend suites, browser matrix, lint/typecheck/build/bundle budget.
3. Fresh composed source Workbench/Studio: author real conditions, save/reload, publish, verify first true and Default, and demonstrate an evaluated failure. Also publish a condition-bearing Activity Definition graph, place it in a workflow and execute conditions referencing its variables and prior results. Use private stores/owned hosts only.
4. Architecture and generated maps checks; document new/changed extension contracts in canonical catalogs and the Switch module README. No tests of documentation text.
5. Independent correctness, standards and acceptance reviews; exact-head hosted required CI and Studio qualification. Reintegrate/requalify if main advances. Verify resulting main and preservation of PR-owned files, keeping skips/quarantines/retries separate.

## Artifact review

Specification reviewed against #3518: separate label/condition, standard inputs, ordered evaluation, compatibility, both hosts and complete delivery are covered. Initial independent review found the need for pre-activation durable pinning, explicit mode/identity/upgrade rules, priority controls, and honest Activity Graph tooling scope. Correctness and standards re-review found no remaining blockers. Acceptance re-review additionally required stable slot/navigation addresses, now included with open-branch reorder/delete/undo tests. Task generation follows this reviewed design.


Implementation review identified two additional consumers of the same projection: ordinary variable/result-flow validators and the reusable graph occurrence overlay. They now use effective arguments as well. Graph occurrence compilation carries the Design projection only within Publishing; the coordinator lowers it to optional effective runtime input contracts on the existing overlay. Null is omitted when there is no projection, preserving unrelated template hashes. Placement compiles and stamps the same contracts and applies the existing closed-scope binding remapping. Switch's dynamic case outcomes are included on the placed contract. No new Runtime-to-Design dependency is introduced.
