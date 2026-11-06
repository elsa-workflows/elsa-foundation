# Activity Data Selection

- **Status:** Active; shared-foundation implementation in #3922. Bounded M1 research and M2 production
  agreement are closed; production acceptance remains outstanding.
- **Area:** Foundation value binding/source discovery and Studio eligible-input authoring.
- **Steward:** Sipke. Codex coordinates delivery through M5 under the owner's 5 November continuation;
  executable ownership is recorded by a valid claim per leaf.
- **Program:** [#3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882).
- **Delivery plan:** [Activity Data Selection](../plans/activity-data-selection.md).

## Purpose

An author configuring an input chooses an available earlier activity's whole output or nested field,
without declaring an intermediate workflow variable or writing an expression. The reference shows its
source; explicit transformations use the same data picker through existing expression infrastructure.
The customer example is Get customer → Customer → Customer ID → consuming activity.

The [program charter](https://github.com/elsa-workflows/elsa-foundation/issues/3882) owns scope and acceptance;
the delivery plan owns the detailed milestone gates and evidence matrix. This page tracks current objectives.

## Sequencing and relationships

[ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) records the scoped owner instruction,
"Proceed as proposed". Run alongside active programs, displace none, preserve Foundation/Studio
consolidation's priority one, and coordinate shared Studio/runtime changes. Keep one integration lane;
parallel Foundation/picker work follows the committed shared contract and uses separate worktrees/leaves.

- Reuse [feature #3094](https://github.com/elsa-workflows/elsa-foundation/issues/3094). Preserve its compensation
  relationship and narrower independently deliverable whole-output slice instead of duplicating the feature.
- [Studio Expression Developer Experience #2310](studio-expression-developer-experience.md) retains text
  code-authoring scope. Share existing SDK, editor adapters, portable bindings and the delivered #3501/#3502 picker.
- [Native Compensation #3035](native-compensation.md) and its consumers retain their semantics and ownership.
  #3094's original "before M3" note is historical: [#3046 is completed](https://github.com/elsa-workflows/elsa-foundation/issues/3046#issuecomment-6297263367).
  Retain compensation regression coverage; closing the still-open compensation program is not a prerequisite here.
- Accepted [ADR 0045](../adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)
  governs causal occurrences. This program does not decide draft ADR 0089 activity-name/occurrence syntax or the
  broader proposed ADR 0046 coercion model. Governed amendments follow their existing agreement process;
  linking dependencies does not invent Initiative membership or an approver roster.

Begin with declared metadata. Basic whole/nested selection is language-neutral and useful without optional
JavaScript/code-editor modules or a prior test run. Authorized test-run examples are a later separately
scheduled increment. Preserve eligible-source restrictions, intentional mutable variables and explicit
collection/merge constructs. Automatic fan-out, guessed mapping, latest-result lookup, new editor/language
infrastructure, general JSON-schema inference and blanket output retention are outside scope. Releases,
deployment, credentials, provider purchases and publisher activation are excluded.

## Milestones and active objectives

| Milestone | Outcome | Owning issue |
|---|---|---|
| M0 — establishment | Owner amendment, registry/goal/plan, native hierarchy and concrete first assignment | [#3883](https://github.com/elsa-workflows/elsa-foundation/issues/3883) |
| M1 — customer proof | Real Flowchart/Sequence nested selection, save/reopen/code roundtrip, publish/run, expression and minimal-host proof | [#3884](https://github.com/elsa-workflows/elsa-foundation/issues/3884) |
| M2 — production agreement | Reviewed spec → plan → tasks resolving source/path identity, availability and composition | [#3885](https://github.com/elsa-workflows/elsa-foundation/issues/3885) |
| M3–M4 — delivery slices | Picker/binding integration, installed-syntax insertion, reference-aware editing | Existing #3094 and later leaves refined from M1/M2 evidence |
| M5 — integrated acceptance | Finite affected-surface/runtime/restart/UX matrix, documentation, reviewed merges and resulting-main verification | Refine after M2 |

Native sub-issues link all three initial tasks and #3094 to #3882. M1 is blocked by M0; M2 is blocked by
M1. #3094 retains its narrower criteria and follows the applicable contract gates, rather than acquiring
unrelated scope from the new parent. New issues start `needs-triage`; assignment does not promote them to Ready.

The owner instructed **"Set a goal to deliver all milestones"** on 5 November, recorded on
[#3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882#issuecomment-6667218844).
The active delivery objective includes M1–M5, review/merge and resulting-main qualification within the
existing scope. M0 merged and its initial resulting main qualified. The
[M1 research report](../reports/activity-data-selection-m1.md) merged: supporting persisted runtime
controls work, while the requested no-expression UI journey fails at identified binding/source seams.
M1 [closed as bounded research](https://github.com/elsa-workflows/elsa-foundation/issues/3884#issuecomment-6670165196)
after backend/full and Studio qualification passed at post-report descendant `e8476790`. Its
[receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3893#issuecomment-6670164301) preserves
failed/not-run product criteria and the newer-main qualification limit; cancelled runs are not passes.

[Spec 1215](../../specs/1215-activity-data-selection/spec.md), its plan/contracts and 51 tasks completed
sequential independent review. [PR #3897](https://github.com/elsa-workflows/elsa-foundation/pull/3897) received
formal approval, passed its required exact-head checks and merged as `f50b72075`. M2
[closed after resulting-source qualification](https://github.com/elsa-workflows/elsa-foundation/issues/3885#issuecomment-6676152380)
at exact descendant `f6ebd0e10`: all selected GitHub jobs succeeded and the unchanged aggregate predicates
passed separately; the queued hosted aggregates were not relabelled passed. This does not qualify later main.

Foundation [#3922](https://github.com/elsa-workflows/elsa-foundation/issues/3922) now owns T004–T010 under
its specific claim, with separate metadata, analysis, Graph and integrating transport/publication worktrees.
Its initial base is qualified f6; newer Decisions/auth source is integrated but still needs candidate
qualification. No production journey is accepted by these contracts or source changes. #3094 and the
remaining runtime, Studio, expression/editing, causal/restart and final UX work retain their own gates.

## Completion

The program finishes when the scoped requirements have actual persisted Studio-to-runtime evidence,
causal branch/loop and persistent restart proof, eligible/restricted/minimal-host coverage, independently
inspected current-commit rendered evidence, documentation and reviewed resulting-main qualification.
Establishment, a successful experiment, fixture screenshots and closed child counts do not establish that
outcome. Record failed, skipped and uninspected criteria separately from passed acceptance.
