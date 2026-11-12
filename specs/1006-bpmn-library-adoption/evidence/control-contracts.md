# Called-workflow control contracts (T039)

This increment implements only the reviewed versioned data boundary. It does not implement dispatch authority, producer staging, outbox enrichment, ordered inbox delivery or the BPMN consumer.

Workflows.Runtime.Core owns immutable bounded authored notifications/batches and an exact dispatch/source-outbox envelope. A present batch must declare ordinal and terminal status; an internal nullable wire projection distinguishes missing fields from valid zero/false without public setters. Versions, nonblank identity/code text, Unicode, counts, payload/content bytes, carried executable identity and source outbox shape are validated. Empty terminal barriers are allowed; empty nonterminal batches and nonterminal authored failure classification are refused. Model shape is not delivery authority.

Activity state has additive ProducedControl/ChildControl projections omitted when null. Structural WithControl and WithState compose without replacing the original runtime fault; authored failure classification requires a fault continuation. DispatchWorkflowResult retains its original four-argument constructor and legacy absent-field behavior, with a separate optional exact-child terminal control envelope. DiagnosticMetadata keeps its fixed allowlist and ordinary meaning.

Intentional API changes are three public Core control models, the two optional activity-state properties and structural continuation control methods/properties;37 new tracked Core API declarations. DispatchWorkflow Runtime adds the optional control property and a five-argument JSON constructor while preserving the original constructor. This package currently has no PublicAPI tracking files; the delta is recorded here. No authoring input, BPMN fingerprint, package dependency or ordinary dispatch policy changes in this increment.

Validation: **19 targeted contract cases**, **240 whole DispatchWorkflow tests**, **2610 whole Workflows Runtime tests**, zero failures/skips. Additional explicit-null wire assertions were then added and the19 targeted cases rerun green. A temporary combined mutation allowing truncated stream scalars and malformed carried pins made exactly7 targeted cases fail; restoration passed. All three incremental review lenses are clean after correcting those two malformed-wire gaps. Tests are contract-level controls, not actual host delivery or upgraded-host recovery.

T040–T045 and T029 remain open. The complete switch PR stays draft, and its final full review loop has not begun.
