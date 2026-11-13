# Adoption decision and reviewable delivery

## Decision and baseline

Proceed with production adoption under the existing owner authorization. The representative prototype demonstrated same-process switching with preserved Elsa configuration and real published results. It supports completing the feature; it does not qualify full-toolbox production behavior.

The fixed choices remain the shared Elsa inspector, the full BPMN toolbox, and full editing in **both** canvases. The corrected integrated-editor choice supersedes the earlier read-only fallback. Permissions, source ownership and historical inspection continue to enforce genuine read-only states. Execution-model work remains separately owned; the editor cannot infer execution support from its progress.

On 9 November the owner [closed PR #3950 without merging](https://github.com/elsa-workflows/elsa-foundation/pull/3950#issuecomment-6739901422) and requested stacked PRs of roughly 3,000 changed lines, one specification slice per PR. This record refines delivery dependencies without removing requirements. The preservation candidate remains [complete XML plus disjoint Elsa bindings](production-design.md); this is an engineering adoption decision, not an assertion of accepted ADR status or production acceptance.

The 12 November comparison baseline is main `dff7c490d0e5501b22d4ba89beb389c956d4f423`. The preserved source head is `8cb4971d40bf044e98be897d8b2f31774019a7ce`, with additional unfinished Direct-publication/browser work. None of its optional package, complete-document handlers, authoring capability endpoint or complete-document editor family has landed on that main. The old branch, dirty files and evidence remain preserved.

## Landed work to retain

| Current-main work | Extraction constraint |
|---|---|
| [#3847](https://github.com/elsa-workflows/elsa-foundation/pull/3847), then [#4073](https://github.com/elsa-workflows/elsa-foundation/pull/4073) | Current wire serialization retains structured expression definitions except the established Literal/Object/Secret text contracts. Carry only the schema 2 native-forest traversal restriction; the old allowlist and Secret assertions are obsolete. |
| [#3948](https://github.com/elsa-workflows/elsa-foundation/pull/3948), [#4055](https://github.com/elsa-workflows/elsa-foundation/pull/4055), [#4066](https://github.com/elsa-workflows/elsa-foundation/pull/4066) | Preserve shared data-source admission, Graph document projection, publish-validated bindings, enclosing-scope proofs and conservative BPMN result-order analysis. Reconcile compiler dependencies; do not replace whole old files. |
| [#4165](https://github.com/elsa-workflows/elsa-foundation/pull/4165), [#4180](https://github.com/elsa-workflows/elsa-foundation/pull/4180), [#4182](https://github.com/elsa-workflows/elsa-foundation/pull/4182) | Keep result-member, variable-member and workflow Input lowering. Backend member-read support does not establish a member-selection UI. |
| [#3975](https://github.com/elsa-workflows/elsa-foundation/pull/3975), [#4187](https://github.com/elsa-workflows/elsa-foundation/pull/4187), [#4197](https://github.com/elsa-workflows/elsa-foundation/pull/4197) | Preserve current first-view sizing, narrow MiniMap and panel clearance when integrating both authoring hosts. |
| [#4127](https://github.com/elsa-workflows/elsa-foundation/pull/4127) | Retain current affected-shard/full-tier scheduling. Add feature qualification through that policy rather than restoring the old aggregate workflow. |

The new document/scope handlers must supply and qualify result-order facts through the current owning contracts. Their default Unknown result cannot be treated as source1 ordering proof. Historical fixtures that bypass the newer analyzer/projector composition need reconciliation before they can qualify a new head.

## Ordered slices

T001–T006 retain their historical evidence; T007 is this adoption record. Original T008–T012 combine prerequisites with activation. Extract their inert prerequisites first, then canonical source/admission, then finish package and host activation. T008/T009 remain incomplete until those later activation slices pass. This explicitly resolves the technical dependency inversion in the original coarse tasks.

The ordered slices below are the refined execution order. Estimates include owning tests; the roughly 3k target is not a reason to pad a small slice. Split a larger final delta at a coherent contract before publishing. Review every child against its actual parent and merge parents first. Partial PRs say **Part of #3597**; only the final delivery closes the feature.

| Order | Original task and result | Target / validation |
|---|---|---|
| S00 | T007: adoption, reconciliation and ordered plan | Documentation only; choices, links, preservation boundaries and evidence status reviewed. |
| S01 | T008 prerequisite: contribution discovery, protocol/resource/schema matching and scoped saved preference | 0.3–0.6k; SDK registry and preference controls. No canvas registration or retired schema 1 prototype protocol. |
| S02 | T009 prerequisite: per-resource checkpoints and parent-acknowledged history | 1.1–1.2k; delayed prepare, stale authority, retained work and cleanup-failure controls. No real-host activation. |
| S03 | T010 frontend source foundation: XML parser/commands, complete native inventory and narrow wire traversal | 2–2.2k; parser/namespace/root-address/inventory controls and existing wire contracts. Defer migration/rendering imports. |
| S04 | T010 backend retention: source contract, XML ownership, compact scope projection/replacement and retained drafts | 2.5–2.8k; whole BPMN and Workflows Design suites. Keep compilation closed until its owning lowering slice qualifies it. |
| S05 | T010 interchange: complete-source export/omissions, legacy refusal and authoring capability | 0.5–0.8k; whole Interchange/BPMN suites and negative old/incompatible-host contracts. |
| S06 | T010 lowering: resolved invocations, native/reusable Graph compilation and variable conversion | 1.6–2.1k; whole BPMN, Graph, Publishing and Publishing.Api suites; current data-source admission and legacy/sibling overlay controls. |
| S07 | T010 direct publication: strict complete-source assessment and optional feature-owned promotion contribution | About 1k; whole Interchange and BPMN.Hosting suites. Keep format 1/6 human and format 3 service profiles, immutable pins and permissions; no unsupported mixed human/service profile. |
| S08 | T011 notation/DI foundation for the complete construct inventory | Up to 3k; events/boundaries/gateways/scopes/data/associations and geometry controls. No generic-task fallback. |
| S09 | T011 operations: containment, connections, replace/delete and syntax/property editing | Up to 3k; structural, namespace and opaque-content controls; split operation groups if needed. |
| S10a | T008 native workspace prerequisite: pinned toolkit dependencies and CI workspace discovery | Metadata only; keep existing lock records unchanged. No scripts, renderer, registration, RCL or published entry point until the owning source/package slices. |
| S10b | T008 native implementation: actual projection, complete command groups and loss-detection reconciliation | 2–3k; pinned-modeler/diff controls plus actual owning test/typecheck entry points. Native serialization never replaces retained canonical XML. |
| S11 | T008 package/lifetime: retained controller, explicit recovery and optional assets/notices | Up to 3k including license text; whole optional tests, types, package/consumer checks and unchanged budgets. Exclude unimported schema 1 prototype code/fixtures. |
| S12 | T010 transform completion: first-edit migration, complete remapping, clipboard and export fidelity | Up to 3k; opening/no-op bytes, native/listener/header identity and opaque-reference refusal controls. |
| S12b | T010 native copy/clone ownership: preserve omitted standard/vendor content and disjoint Elsa configuration through the retained native command path | Bounded owning prerequisite before activation; qualified canonical clone/remapping and parent receipts, or retained atomic refusal while preparation is incomplete. Full-copy acceptance remains open until this bridge is qualified. |
| S13 | T009 Workflow activation: shared surface/inspector, nested editors, lifecycle and history | Up to 3k; actual editor controls, permission/source-owned states and composed persistence. Canonical source, migration, full editing and compatible backend must all be present before switching. |
| S14 | T009 Activity Graph activation: equivalent scopes, receipts, configuration and lifecycle | Up to 3k; whole owning editor suites and actual Graph persistence/admission with current data-selection guards. |
| S15 | T012: theme/narrow/keyboard/focus, attribution, network budgets and module guidance | Bounded changes with composed captures and independent inspection; retain current first-view behavior. |
| S16 | T013–T014: complete qualification and downloadable acceptance harness/evidence | Split owning helpers at 3k as needed. Preserve all 18 Workflow/Graph journeys; add separate real Direct human and service cases. No skipped cases or relaxed preservation/runtime oracles. |
| S17 | T015–T016: final review, applicable protected merge and resulting-main qualification | Exact-head external receipts plus lifecycle/bookkeeping. Close #3597 only after full acceptance. |

If source dependencies require another small prerequisite, record it here before extracting it. Activation hunks must stay coherent: an intermediate merge may add an inert seam, but must preserve the current editor until the complete replacement path exists. No restricted intermediate user flow is presented as the owner's full-toolbox feature.

The native source slice needs its toolkit installed and discovered by the ordinary recursive CI workspace. S10a therefore records pinned private workspace metadata before S10b introduces its test/typecheck scripts and source. It adds no empty-test success flag, runtime registration, public export or RCL. Existing importer/package/snapshot records stay unchanged.

The native copy inventory also distinguishes namespace provenance from omitted canonical configuration: copying a projected task cannot silently discard retained vendor or Elsa payload. S10b preserves the original and refuses an unqualified copy; S12b supplies the owning clone/remapping and retained admission bridge before either full host is accepted. This refinement removes no clipboard or full-toolbox requirement.

## Acceptance and maintenance

The principal cost is identity/history/namespace fidelity across two modeling implementations. Keep toolkit imports, styles, fonts and notices inside the optional module. Pin its dependency tree; on upgrades repeat semantic/native-configuration round trips, history/lifecycle, nested editing, theme/narrow, attribution and consumer checks. Keep existing run inspection unless a separate demonstrated need changes that scope.

The [ledger](evidence/prototype-ledger.md) preserves historical results. The schema 1 prototype's two directions/four real runs and inspected visuals establish feasibility only. The last whole source2 frontend and Publishing.Api phases failed; source repairs and focused passes do not relabel those phases. Current-source advanced retention, all-host activation, Direct browser proof, packages, network measurements and independent UX remain unqualified.

No old Azure lease or worker receipt is renewed by this plan. Use a newly authorized owned host or applicable CI. Heavy local builds/browser runs remain outside the preserved operating constraint. Each slice needs its whole affected suites, discovered/executed counts, meaningful causal controls and exact-head CI/review. Final completion also requires downloadable inspected composed evidence and resulting-main verification.
