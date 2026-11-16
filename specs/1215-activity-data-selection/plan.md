# Implementation Plan: Activity Data Selection

**Branch:** `agent/codex/3885-data-selection-contracts` | **Date:** 2026-11-05
**Spec:** [spec.md](spec.md) | **Program:** [#3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882)
**M2 task:** [#3885](https://github.com/elsa-workflows/elsa-foundation/issues/3885)
**Status:** Reviewed production plan; approval takes effect on M2 PR merge. Production dispatch requires
resulting-main qualification. Research results are not product acceptance.
**Inspected baseline:** `e8476790d6f9d146c04ff5e02af266566e0b9d24`; requirements reviewed at `46b1a830d`.

## Summary

Add **Choose data** to eligible consuming inputs. Authors select a whole earlier output or nested member,
see readable provenance, and save/publish/run without creating a variable or writing an expression.
The same source browser supports installed expression adapters when authors explicitly transform data.

Reuse role-owned source identities, causal resolution, document/history ownership, the activity catalog
and existing property/editor contributions. New nested reads are immutable consumer instructions for
request, scoped-variable/current-item and activity-result sources. Basic selection has no optional
language dependency. [Research](research.md) explains decisions; [data model](data-model.md),
[binding contract](contracts/binding-contract.md) and [authoring contract](contracts/authoring-contract.md)
own details. [Quickstart](quickstart.md) defines the finite production verification inventory.

## Readiness and qualification

[M1 closed](https://github.com/elsa-workflows/elsa-foundation/issues/3884#issuecomment-6670165196)
through its precise-failing-seam research exit. Both [backend/full CI42086549189](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42086549189)
and [Studio CI42086827962](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42086827962)
completed successfully at post-report descendant `e8476790d6f9d146c04ff5e02af266566e0b9d24`.
The [closeout receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3893#issuecomment-6670164301)
preserves the failed/not-run product criteria. No research code or fixture is adopted.

Product, technical and cross-artifact reviews completed sequentially, with all findings fixed/rechecked.
The 25 functional requirements and eight success criteria map to 51 tasks across five stories. M2's
approval/status transition takes effect when its normal exact-head review/CI and merge complete; it is
approval of this plan and its semantic contracts, not an implemented API or passed production journey.

The branch includes newer main `6337982e5b24f20a684c8adbef763405c2779daa`. Its
[separate CI42103010890](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42103010890)
was unfinished at M1 closeout and is not qualified by the older runs. The audited template-toast and
shared-source-row family deltas leave M1 findings and these contracts intact. M2 must pass its own
required CI/review on the final current-main head; M3 starts only after M2's resulting-main qualification.
Check live run/tracker state rather than treating this dated receipt as current health.

## Technical Context

| Area | Existing technology and planned boundary |
|---|---|
| Server | C#/.NET 10; repository SDK 10.0.300 with latestFeature roll-forward. Existing CShells features, MinimalEndpoints, System.Text.Json and role-owned executable contracts. |
| Studio | TypeScript, React 19, Vite, pnpm 11.9.0; existing Studio module/UI, property contributions, canonical document/history and expression-editor sessions. |
| Storage | Existing drafts/versions, executable/template artifacts, protected result envelopes and committed invocation/input snapshots. No migration, new store or persisted chooser state. |
| Serialization | Exact current activity-value Web/camelCase/strict-number profile; literal serialized-name segments. General payload serialization and S-9/#3805 remain separate. |
| Testing | Existing xUnit, Vitest, Playwright, owned composed Workbench/Studio runner and real backend e2e lifecycle. Focus on changed behavior and cross-seam risks. |
| Target | Existing Design-only, Runtime-only and combined compositions, normal/minimal Studio, supported browsers and existing durable stores. Runtime needs no Design/provider metadata service. |
| Scale/performance | Bounded metadata search/member traversal under existing document/JSON limits. No producer execution, instance serialization, unbounded recursive tree or workflow-wide result scan. No new benchmark target. |
| Compatibility | Preserve whole-value bytes/hashes, constructor compatibility, existing enum ordinals and expression parameters. New member discriminants plus engine requirements refuse unsupported readers. |

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.4.0, both Gate indexes, and the
[review checklist](../../docs/contributing/review-checklist.md). This is a design assessment, not a claim
that implementation architecture tests ran. Recheck these gates on each production PR and after integration.
No constitutional exception or amendment is proposed.

| Gates | Plan assessment and required implementation evidence |
|---|---|
| Framework §§2.1, 2.4, 2.17; Elsa §E2.2 | Core contains immutable contracts and one thin existing-profile definition. Reflection, cache, analysis, HTTP and traversal implementations stay outside Core. No heavy Core dependency, new project, Runtime→Design edge or cross-domain friend grant. Run affected architecture/composition and maps checks. |
| Framework §§2.5–2.7, 2.11, 2.18 | One scoped analysis service behind a Core port; discovery, validation and publication share it. Owning structures supply control-flow facts. Missing/duplicate authority fails composition rather than skipping validation. |
| Framework §§2.10, 2.20; Elsa §§E2.5, E2.6 | Nonpersisting snapshot analysis is a query; normal mutation retains its command. Executables pin runtime facts with no Design lookup. No mandatory DbContext/new provider. |
| Elsa §E2.8 | Admitted catalog/provider contracts determine visibility. Reflection augments authorized shape; CLR scanning stays reflection-only. |
| Framework §§2.21–2.23; Elsa §E7 | Behavior tests cover wire/admission/causal risks. Extend existing suites/journeys; child-host suites belong to integration tier. Retain downloadable independently inspected actual UX evidence. |
| Framework §§2.13, 2.16, 2.23.3, 4; Elsa §§E5, E6 | No new package. Internal by default; review/track required public DTO/helper/binding API deltas, preserve constructor signatures and use computed versioning. |
| Framework §§2.22, 2.25 | Update owning feature docs/extension points and customer guidance. No unrelated artifact retirement. |

Framework §2.24 and Elsa §E2.8 versioning/Model X and §E2.9 are draft/provisional. Preserve existing
document/lifecycle boundaries without claiming ratification. Accepted ADR 0045 governs; draft ADR 0089
and broader ADR 0046 are not adopted. If implementation requires a semantic amendment, stop that scoped
change for its governing process. M2 reviews this concrete proposal before adopting it.

## Project Structure

```text
specs/1215-activity-data-selection/
  spec.md, plan.md, research.md, data-model.md, quickstart.md
  contracts/binding-contract.md, contracts/authoring-contract.md
  checklists/requirements.md
  tasks.md                       # after plan/contract review

Essentials/src/Activities/
  Runtime/Core/                  # thin unchanged activity-value profile definition
  Runtime/Elsa.Activities.Runtime/ # existing completion/hydration callers
  Design/Core/                   # serialized-shape contract facts where needed
  Design/Reconciliation/Clr/     # CLR contract metadata extraction
  Flowchart/                     # owning control-flow structure implementation
Essentials/src/Workflows/
  Design/Validations/Core/       # shared analysis port/facts
  Design/Validations/Elsa.Workflows.Design.Validations/ # analysis/validation
  Design/Api/                    # authorized current-snapshot capability
  Publishing/Elsa.Workflows.Publishing/ # compiler/publication/template closure
  Runtime/Core/                  # immutable compiled member-read model
  Runtime/Elsa.Workflows.Runtime/ # selection/fit/admission/ordinary/intrinsic paths
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
  activityInputWire.ts, ActivityPropertiesPanel.tsx, module.tsx
  expression-tooling/, workflow-editor/, existing document/editor integrations
Studio/tests/acceptance/journeys.spec.ts
tools/ux-acceptance/run.py
Essentials/tests/, Studio/**/__tests__/, e2e-tests/
```

Use existing projects. Exact new internal filenames follow local conventions; these package/ownership
boundaries are the reviewed proposal, not permission to build a generic data platform.

## Implementation seams

### Shared source analysis and publication authority

Put the small source-analysis port and language-neutral fact DTOs in existing
`Elsa.Workflows.Design.Validations.Core`; implement the scoped coordinator internally in
`Elsa.Workflows.Design.Validations`. API/Publishing consume the port, not each other's internals.
Design API already depends on the validation feature; endpoint-free Publishing does not. Add an explicit
Publishing feature dependency on WorkflowDesignValidations so its shared analysis authority is required,
and remove optional-admission fallbacks on affected executable paths. Prove resolution/admission in
Design-only, endpoint-free Publishing and combined hosts. Serialized-shape facts belong to Activities Design Core, with reflection/provider implementations
in their existing layers. Transport acquisition/request identity stay in Design API.

Resolve untouched declared and structure-projected inputs. Authorize resource/base revision, analyze
the exact current canonical document, and return bounded metadata with context identity. Normal autosave
uses its acknowledged queue; manual/held surfaces use authorized nonpersisting snapshot analysis or
explicit save-and-load. No second draft/save path or trusted client symbols/types.

Discovery and ValueFlowValidator share availability facts. Sequence/Flowchart structure handlers own
order, all legal entries, joins and backedges through the structure service. Extend that contract minimally
for conditional/unavailable reasons; do not copy graph algorithms into HTTP/UI. Retain projected inputs,
including Switch conditions. Enforce static direct and portable-parameter admission on every
executable-producing/test-run path before artifact/publication/activation writes. Concurrent-write
analysis remains separate.

### Role-specific nested reads and runtime admission

Implement the binding contract's authored/compiled member discriminants and immutable
`RuntimeReadSelection`. Add no path to variable/output write targets. Lower InputMember through reusable
boundaries losslessly, pin leaf/deferred contracts before conversion, and hash all behavioral fields.
No-selection bytes/hash rendering stay unchanged.

Share the exact existing activity-value profile through a thin definition in Activities Runtime Core.
Keep options/cache ownership in callers and reflection/provider work outside Core. Metadata extraction
must not execute getters/converters, load code from MetadataLoadContext or claim opaque shapes.

Put literal selection and bounded target-fit implementation inside Workflows Runtime. Resolve each
existing frame/causal role first, then its declared projection and literal selection. Cover inline/external
values and ordinary/intrinsic consumers. Validate deferred fit under the unchanged activity profile
before committing a selected snapshot or constructing the consumer. Reuse alias/fit logic where possible;
keep the later hydrator defense. No general codec registry: unsupported opaque targets reject rather than
being inferred. Preserve binding-contract presence/null/protection/conversion and producer completion.

Advertise `elsa.runtime.input-member-selection` schema 1 through the existing runtime capability registry.
Propagate requirements through templates/closure/import/placement. Reuse IRuntimeRequirementChecker at
local start/resume ingress and pinned queued/recovery execution before scheduling/user code. Test actual
old named/numeric enum readers and new admission. Do not promise prepublication rejection in an unmodified
old authored compiler; its limitation is explicit in the binding contract.

### Shared input selection and canonical editing

Add a language-neutral client/shared chooser in the existing Studio Workflows module using Studio controls
and the host's canonical document/history edit. Whole-result selection may ship through #3094 after its
independent criteria pass; gate member selection until compiler/runtime capability exists. Never generate
unsupported bindings from a partial UI delivery.

Cover the spec's full surface inventory through shared contributions, with explicit integration for
intrinsic value inputs and structure-owned Switch conditions. Ordinary BPMN bound inputs reuse it;
model-owned closed service/events/gateway controls retain their contract. Constrained/custom/read-only/
secret/source-owned values preserve authority. Repair the structured wire allow-list and render working
provenance/navigation/replace/clear. Preserve content during loading/errors/revocation/module loss and
reject late responses by exact target/snapshot. Discovery includes no evaluated samples or producer runs.

### Installed expression adapters and graph identity

Use the same chooser in compact/expanded installed expression editors. Existing whole-source portable
bindings remain; adapters insert escaped member access with collision-free legal aliases. Text plus
bindings/cursor form one document operation. Unsupported syntax/module loss preserves authored content.

Centralize role-aware reference traversal for copy/paste/diagnostics in existing document transactions.
Remap internal producer/scope IDs, never labels/member segments. Rename is label-only; moved/deleted
sources remain inspectable and repairable. Include structure-projected inputs and reusable graphs.

## Delivery order and parallel work

1. M1 research is closed at its qualified fixed source. Complete normal M2 spec→plan→tasks approval,
   merge and resulting-main qualification before any production worker starts.
2. Establish shared contracts/authority and the whole-output wire/input path. Reuse #3094 for its bounded
   slice; retain nested-runtime/final qualification obligations in their actual leaves.
3. Once shared contracts merge and qualify, runtime/member work and Studio picker/member integration may
   proceed in separate worktrees. Sequence shared binding-model/inspector edits and composed builds.
4. Integrate expression insertion/reference-aware editing after chooser/binding interfaces stabilize.
   Delegate independent UI/runtime reviews throughout.
5. Run M5 against the integrated exact commit, complete customer guidance, review/merge and qualify
   resulting main. Native child/dependency records and program transitions follow actual leaves.

[Tasks.md](tasks.md) follows independent plan/contract review and records ownership, dependencies,
acceptance and finite verification. Create only the next executable leaves after M2 approval. Workers
claim issues, use separate worktrees and open one reviewable PR per issue. Check open `main is red: CI`
trackers and exact prerequisite heads before dependent work; a mock endpoint is not product acceptance.

## Verification and completion

[Quickstart](quickstart.md) owns nine finite journey families: basic Sequence/Flowchart mapping, expressions,
graph repair, causal paths, actual restart/retry, equivalent/restricted targets, current unsaved/partial
information, normal/minimal/reader composition and rendered accessible behavior. Tasks map requirements
and success criteria to those journeys rather than duplicating their inventory.

Run focused wire/shape/availability/publication, plan/hash/target-fit/capability, ordinary/intrinsic/
external, document-history and remapping suites. Invalid deferred input reaches neither constructor nor
committed snapshot. Whole-value controls prove unchanged bytes/hash and actual baseline readers prove
refusal. Backend changes also require affected real e2e. Broaden only for new failures/changes/required CI.

Extend both composed journey and capture inventory. Run four desktop/narrow × light/dark profiles,
focused 320px picker/reference state, normal/minimal owned hosts and producer-once actual persistent
process restart/resume. Preserve Failed/Not run/Skipped/Canceled distinctions and failed artifacts.
The known restart script's green resumedCount=0 path does not satisfy acceptance.

Each delivery PR records exact head, meaningful checks, downloadable actual host/runtime/visual evidence
and independent review. M5 completes only when all scoped criteria pass, screenshots are actually opened,
customer guidance is current, review/merge completes and resulting main qualifies. Approved documents,
research controls and closed children alone do not satisfy the owner goal.

## Complexity Tracking

No constitutional violation proposed. Added complexity is one immutable read plan, one shared analysis
authority and one shared chooser in existing modules, each addressing an observed failure. No new storage,
language, project, generic reference framework, serializer baseline or global dataflow system is required.
