# Implementation Plan: Step bodies change workflow state

**Branch**: `agent/codex/3052-s13-engine-state` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

## Summary

Extend the existing inline leaf evaluation with workflow output/identity intents. Prepare all changes immutably, merge them with S-7 variable changes, project captures last, and submit one `RuntimeCheckpointCommit`. No setter persists. Add a narrow body-only script bridge, faithful Elsa 3 lowering, pinned capability metadata, existing runtime inspection/Studio presentation and recovery proofs. Implementation begins only after this specification is approved and merged; it uses a separate implementation PR targeting the initiative.

## Technical Context

**Language/Version**: C#/.NET 10; TypeScript/React in the existing Studio clients.

**Dependencies**: existing Activities.Runtime.Core, Expressions.JavaScript.Core/Jint, Workflows.Runtime.Core/runtime, Design validations, Publishing and Elsa3.Mapping. No new package/project, host interop or dependency from Runtime to Design.

**Storage**: current workflow/activity state and `DurableValueState` in the existing checkpoint, both in-memory and EF-backed. Keep output/identity slot IDs and payload/protection metadata. No new table or migration is planned; any implementation-discovered durable schema change must use the established frozen-format/migration workflow and be reviewed before delivery.

**Testing**: existing xUnit projects, Studio Vitest and composed-host Playwright. Behavior tests focus on rollback, identity/bookmarks, conversion, bypass and recovery. No source/Markdown assertion tests. Elsa has no mandatory TDD cadence (ADR 0080 D9); tests are developed alongside each behavior slice, with pre-change failure or a validated negative control for changed contracts. This is an extension of tested paths, not an ownerless greenfield application.

**Platform**: Windows authoring; permitted Linux validation path if Windows Application Control prevents execution. Record every failed/not-run command; never disable policy. Builds/tests run through the installed build-slot wrapper, one affected project at a time, and load is checked before interpreting timing failures.

**Constraints**: one transaction per existing supported evaluation; selected persistence mode unchanged; no per-setter flush; sticky refusal; JSON/precision/protection rules reused. CI waits deferred to initiative integration, without claiming CI passed.

**Scope**: leaf invoke/resume and script bodies only. Structural/background/closed graph capability use is refused. Reusable graph intrinsic support remains separate; ordinary read-only graph scripts retain their supported behavior.

## Constitution Check

Both `.specify/memory/constitution-framework.md` v6.1.0 and `.specify/memory/constitution.md` v6.5.0, both Gate indexes, and `docs/contributing/review-checklist.md` were read. The entrypoint's older Elsa version does not override the initiative's actual document.

| Gate | Design result / implementation obligation |
|---|---|
| Framework §2.1–§2.4; Elsa §E2.2 | Public author contracts in Activities.Runtime.Core / Expressions.JavaScript.Core; workflow models/storage in Runtime.Core/runtime. Activities.Core currently has no Runtime dependency: do not add one for this capability. Pass JSON/capability contracts across the existing seam. |
| §2.6.2, §2.6.4; Elsa §E2.6 | Separate authoring effect metadata from runtime executable metadata. Runtime reads pinned artifact/contracts only, never Design stores or syntax services. The body capability is a passed object, not DI or a general service locator. |
| §2.9–§2.10 | Existing stores remain query/command separated; no required base DbContext or second persistence owner. |
| §2.21–§2.23 | Risk-based behavior and negative controls in existing sibling tests; internal sealed implementations, public contracts only where activity/provider authors need them. Update PublicAPI.Unshipped files and extension catalogs with public changes. Plain registration uses the composition test; add registration tests only for new conditional wiring. |
| Elsa §E2.7 | Elsa 3 names/lowering stay in Extensions/Elsa3. Native runtime has no Elsa 3 compatibility model or mode. |
| Elsa §E5–§E7 | Do not hand-edit versions. Keep additive context/request members and existing constructor/overload compatibility; no unnecessary required interface additions. Use existing test projects/CI tiers. |
| Draft/provisional boundaries | Framework §2.24 and Elsa §E2.8 versioning/Model X and §E2.9 are unratified. Their status is not needed to approve the transaction contract; reuse existing authoring shapes without representing those sections as ratified authority. |

Pre-research check: no identified violation. Post-design recheck: same; no architectural exception or ADR amendment proposed. Public shape changes require the normal implementation API review, not new architecture authority.

## Project Structure

```text
specs/1234-step-workflow-state/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/workflow-state.md
  checklists/requirements.md
Essentials/src/Activities/Runtime/Core/Contracts/IStepWorkflowState.cs (new)
Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/StepState/ (new internal preparation)
Essentials/src/Activities/Scripting/Activities/RunJavaScript.cs
Essentials/src/Expressions/JavaScript/Core/Contracts/IJavaScriptScriptEvaluator.cs
Essentials/src/Expressions/JavaScript/Jint/Services/
Essentials/src/Workflows/Runtime/{Core,Elsa.Workflows.Runtime}/
Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/
Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
```

## Phase 0 — Research

[research.md](research.md) identifies existing slot/projection/checkpoint paths, S-7 restrictions, source conversion semantics, protection hazards and alternatives within the accepted ADR. No research question remains that prevents proposing this bounded design. Scope/API choices are concrete recommendations for coordinator approval, not implied agreement.

## Phase 1 — Design

The public [contract](contracts/workflow-state.md) uses `context.WorkflowState` and a body-only `workflowState` script object. It offers JSON output get/set and string/null identity get/set only. Native types declare `[AccessesWorkflowState]`; scripts carry known output names plus conservative flags for computed/opaque access. Publication pins the signal independently of variable access, enabling background and graph refusals even for identity-only writers.

The session factory reads current working output/identity state before body execution and holds only pending intents/copies. It does not refresh pinned inputs. The existing single-writer work-item boundary means no other evaluation interleaves while the inline body runs. On suspension/resume the same established lifecycle applies. Output/identity writes use serialized evaluation order, without extending ADR 0092's per-variable conflict protocol to new state kinds.

Each body output is a fresh `Any` envelope with InstanceInline owner policy combined with source protection minima through `ValuePolicyCombiner`; replacement uses Any while preserving prior destination policy/storage minima and creating a fresh encoded payload/reference. A prior type is not a schema constraint. Source protection and existing destination requirements combine through the single existing policy combiner; incompatible/unavailable external/custom/encrypted encoding refuses without inline fallback. Native identity normalization remains unchanged; converter-emitted `requireExactIdentity` guards reject blank source identities before that normalization can silently alter Elsa 3 behavior.

Preparation combines the most recent workflow snapshot with S-7's prepared frame changes, then applies identity changes to that combined record. Durable output and identity rows are built with the same policies/IDs as the existing intrinsics. Accumulation has one final change per physical ID, with captures taking final precedence only for actual overlapping targets. Validate everything before submitting the checkpoint. Extend all completion, suspension, refusal, disposal and recorded-completion branches; a completion with any session effects cannot be accepted as captures alone.

Identity projections and workflow header travel together; no bookmark/registration/route mutation is generated. A later wait explicitly binding current identity sees its committed value, while all other waits keep their own input semantics.

Output payloads retain protection and precision policy through the existing bridge. Public identity and output names have no protected envelope: conservatively refuse them if the body has protected inputs or reads, including a protected read later in the evaluation. This is an enforcement choice using existing withholding, not a declassification policy. Read output policy joins the evaluation's protection state. All host refusals are sticky across JavaScript catch blocks and native catches.

Use the existing instance output/identity read models and policy-controlled value endpoint. Add value-free state-kind attribution to activity inspection and show authored effects in Studio. Reuse existing UI primitives. The implementation captures current-head screenshots in the composed host and obtains independent UX review in its ordinary review loop.

## Phase 2 — Delivery

[tasks.md](tasks.md) groups runtime/bridge, correlation, conversion, recovery and Studio work by user story. A useful first slice is US1, but the authorized delivery is the complete approved scope in one implementation PR. Do not mark the spec Implemented while conversion, provider recovery or Studio acceptance is missing. Update lifecycle in that implementation PR only when its full scope is delivered.

## Validation and approval boundary

The proposal receives deterministic decision-doc/link/task checks and independent source-aware correctness/standards/acceptance review before coordinator approval. Its pre-merge resolved-base gate is the coordinator's gate; the existing main-sync work is owned elsewhere. Local proposal review must not claim that gate complete.

Implementation evidence records exact source/reviewed heads, affected builds/tests and negative controls; preserves failed/not-run outcomes; and distinguishes current execution from historical receipts. Repeat fix/review until an independent round has no verified findings. CI remains deferred as instructed. Final merge still follows initiative governance and protection.
