# Implementation Plan: Optional bpmn-js editor

**Branch**: `agent/codex/3597-bpmn-adoption-plan` | **Updated**: 2026-11-12 | **Spec**: [spec.md](spec.md)
**Canonical issue**: #3597 | **Bucket**: BPMN Engine

## Summary

Proceed with bounded production adoption after the source-bound representative prototype. The owner-selected outcome remains the same process in either full-toolbox editor, using Elsa's inspector and ordinary authoring lifecycle. The public XML interchange is not a lossless internal switch transport. The complete XML/disjoint-binding design is the implementation candidate; its production acceptance remains open.

The owner closed the aggregate PR without merging and required reviewable stacked slices. Follow [delivery-slices.md](delivery-slices.md), starting with inert contribution/preference and checkpoint prerequisites. Complete source/admission must precede real-host activation. Original T008–T012 IDs remain acceptance references; the refined slice sequence resolves their technical dependencies without dropping work or enabling the restricted prototype as the production editor.

## Technical Context

Existing React/TypeScript Studio, modular/lazy frontend loading and .NET10 Workbench; exact upstream candidate bpmn-js **18.31.1**. Existing authoring, inspectors and native/direct publication remain owning paths. Reconcile the candidate with current shared data-selection admission, whole-output selection and member-read lowering. Retain actual resolved template identity and conservative result-order proofs. No runtime engine or workflow-store replacement is included.

The 12 November baseline is main `dff7c490d0e5501b22d4ba89beb389c956d4f423`. None of the complete-document/optional-module implementation is on that main. Do not replace its newer wire serializer, compiler constructors or data-source guards with the preserved 5–6 November files. Fresh qualification belongs to each extracted head; earlier passing or failing evidence keeps its original source/profile.

## Constitution Check — before prototype

Read framework v6.1.0 and Elsa v6.3.0 gate indexes and relevant contributor rules. Preserve domain ownership, feature composition and design/runtime separation (§2.1/2.5/2.6, Elsa E2.2/E2.6); use risk-based behavior and mutation tests (§2.21/2.23), existing UI token/module contracts and source-owned authorization. No constitutional exception or change to draft/provisional rules is proposed. Recheck after prototype/design, particularly optional package boundaries/public SDK changes. Architecture guards/maps and applicable required CI remain delivery gates; documentation-only planning is not production evidence.

## Staged Critical Path

1. **Feasibility foundations**: settle claim/authorization, inventory actual native/direct/nested/reusable/read-only/runtime surfaces; pin/review upstream license/API/dependencies; record initial UX states and preservation hazards.
2. **Embedded prototype**: controlled bpmn-js canvas inside existing editor, identity-based projection/merge, shared selection/inspector, one canonical history; prove hard binding/listener/nested/layout regressions. No production contract freeze.
3. **Real-host feasibility**: both authoring directions through save/reload, publication, actual two-path execution and inspection; negative controls and exported XML. Measure actual optional network/assets and attribution. Record working/unsupported/unproved matrix and go/no-go with maintenance cost.
4. **Production design/integration**: review specification/plan/tasks against evidence. Implement full-toolbox authoring in both bpmn-js and the integrated Elsa canvas, one complete canonical preservation model, optional module/consumer loading, preferences/capability failures, both host editor surfaces, authorization, current execution admission/recovery and contributed bindings. Preserve unrelated execution-model owners.
5. **Qualification/delivery**: complete affected suites and mutation proofs; actual composed-host themes/narrow/keyboard/focus/recovery, current-commit shared downloadable visuals/traces and independent UX review; architecture/maps and required current-head CI; review/merge under current org protocol; qualify resulting main and reconcile canonical issue/spec/docs.

## Provisional Synchronization Design

- Full `ActivityNode` tree, version/binding identities and authored structure stay canonical. No persistently duplicated XML document.
- Canvas projection carries only modeled structure, stable element/flow IDs and diagram data. Untouched configuration remains opaque to it.
- Apply supported canvas changes atomically through the host; preserve child/listener inventories, unknown retained fields and nonvisible nested configuration. Refuse unsupported or ambiguous changes before canonical mutation.
- Studio history is the user-visible authority for canvas and property edits. Reimport clears bpmn-js command history, so independent native Undo is insufficient. Pending asynchronous work requires revision guards/serialization and must be flushed or refused before save/switch.
- Existing Elsa inspectors beside the bpmn-js canvas and existing nested editors are the owner-selected configuration UI (5 November 2026). Existing run viewers are retained unless evidence justifies replacing them.
- Production toolbox decision (6 November 2026): show the **full BPMN toolbox**, aligned with separately owned full-spec execution-model refactoring. Do not ship the prototype's narrow edit admission as the product policy. Design one canonical preservation model for all draft constructs and capability-aware validation/publication refusal for execution the present host cannot support. Preserve full content when switching; explain an unrepresentable transition before mutation. This decision does not authorize taking over or resuming execution-model streams.
- Corrected owner choice (6 November 2026): **expand the integrated Elsa editor to edit the full toolbox in this delivery**. Accurate advanced notation, structural/property editing and lossless switching are production acceptance, not a read-only fallback milestone. The earlier read-only-view response was immediately corrected and does not govern implementation. Keep permission/source-owned/historical read-only modes intact.
- Preserve relevant BPMNDI sizes/waypoints as well as positions; keep integrated-editor changes coherent with projected layout.
- Prototype profile admission is explicit before import: validate raw authored records (never silently filter malformed arrays), global BPMN identity uniqueness, and every represented semantic field. Generate a deterministic projection-only process/DI ID outside all authored IDs; never mutate canonical owner identity. Unsupported semantics that cannot be faithfully represented hold the alternate canvas with a reason and preserve the integrated editor. Profile coverage must be expanded/evaluated from actual proof before production adoption, not treated as accepted scope reduction.
- Initial experimental edits cover ordinary start/end/terminate events, tasks, collapsed Elsa-bound subprocesses, exclusive/inclusive/parallel gateways, names, supported outcome/default routing and diagram layout. Unknown executable constructs, non-outcome conditions, collaboration, boundary/events, multi-instance/transaction/compensation and unsupported type/binding transitions require explicit representation/admission or refusal before edit. Retaining opaque configuration alone does not prove the canvas accurately represents its semantics.

## Actual Hazards to Disprove

The complete-source production proposal and reversible contract/notation/module spikes are recorded in [production-design.md](production-design.md). The restricted prototype model above does not establish the preservation format for full-toolbox authoring. Production contract freeze still depends on real-host feasibility and preservation/admission proofs.

Public export omits expressions/output/reference/structured bindings; gateway child binding is outside its task collection. Import synthesizes child IDs and loses typed variable/nested layout data. `syncBpmnCanvasToScope` currently trims activity inventory using only child IDs, ignoring listeners. Native publication and direct human/service publication are different profiles. #2736 is still open/parked; do not claim its general safeguards delivered. A local controlled bridge must not infer universal publication protection from current root-graph preflight.

The integrated renderer currently depicts unknown types as task-like frames. That cannot satisfy the corrected full-toolbox choice. Review a complete canonical syntax/DI representation and both editors' operation mappings before production implementation; derive current-host admission from actual full content before typed lowering, never from a removable unsupported flag.

## Validation and Evidence

Start with bound Decision expression/default path, task binding plus outputs/references, nested body and listener preservation. Exercise structural creation/deletion/reconnect/type change, variables, unknown metadata and layout. Target affected frontend suites and BPMN projects; respect build slots and machine load. Existing isolated BPMN browser fixtures support regression evidence; only owned composed Workbench/Studio journeys qualify product behavior. Every stage keeps failed/not-run/unavailable states visible. Final evidence must identify exact source/head/main commits and accessible artifacts.

## Coordination

One claim and one implementation/review lane on #3597; no duplicate feature or new program. Link the existing BPMN Engine record and #2736 before shared changes. Coordinate #3610, #3588/#3892 and execution-model owners via existing issue/PR records. Do not deliver their streams or change shared-source semantics under editor scope.
