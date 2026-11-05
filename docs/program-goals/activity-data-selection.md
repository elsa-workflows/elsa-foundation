# Activity Data Selection

- **Status:** Active; owner authorization, 5 November 2026. Establishment precedes the bounded proof;
  reviewed production contracts/specification precede implementation.
- **Area:** Foundation value binding/source discovery and Studio eligible-input authoring.
- **Steward:** Sipke. Codex coordinates the first proof; executable ownership is recorded by a valid claim per leaf.
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

The current objective is M0 establishment and the first Codex proof assignment. M1 may prepare read-only;
its research code starts after the establishment records merge and resulting main is qualified. M2 uses the
proof findings before parallel production workers start. A separate Project board is optional and does not
delay the first result.

## Completion

The program finishes when the scoped requirements have actual persisted Studio-to-runtime evidence,
causal branch/loop and persistent restart proof, eligible/restricted/minimal-host coverage, independently
inspected current-commit rendered evidence, documentation and reviewed resulting-main qualification.
Establishment, a successful experiment, fixture screenshots and closed child counts do not establish that
outcome. Record failed, skipped and uninspected criteria separately from passed acceptance.
