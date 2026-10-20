# Research: Studio Authoring Action Copy and Toolbar Consistency

**Feature**: 992, tracking leaf #3072 under epic #3057
**Source evidence**: Elsa Foundation main source revision 3255701dcd9ed231d718aea0945dc3a6332c3866. The feature specification was committed as 78189151b7c447c324a1170f464e028595105ce0; that commit changes planning/specification records, while the source checks below are against the pinned main revision.
**Research state**: Complete for the approved bounded scope; no unresolved product or interface decision.

## Decisions

### Keep the change inside existing Studio Workflows UI

**Decision**: Change presentation and verification only in the existing Elsa.Studio.Workflows client.

**Rationale**: Workflow action, Test Run panel/status, Activity Definition workbench, draft editor, and publication review already exist in this client. The approved requirements preserve their eligibility checks, exact IDs, network operations, callback semantics, and lifecycle. No new persisted state or external contract is required.

**Alternatives considered**: A server/API or SDK contract change, or a shared product-wide action model, would add surface area without being needed to express the approved copy and geometry requirements.

### Keep Test Run labels local to the transient-test path

**Decision**: Use “Test Run” for the workflow toolbar action and setup/loading copy, and “Start Test Run” only as the generic setup submission fallback when there is no module-supplied label. Preserve supplied submission and trigger/input labels, their associations, the connected-environment/real-side-effects warning, and the existing result-details callback.

**Rationale**: WorkflowEditor conditionally passes the real TestRunStatus into WorkflowEditorToolbar. WorkflowTestDrivePanel already owns setup, warning, fields, and submission; its generic submit fallback is “Run workflow.” Lazy and inner loading states have separate visible status messages. TestRunStatus owns the current status button and direct-details callback. Keep the full outcome text, stale-draft cue, and accepted runtime issue detail while aligning the Test Run prefix capitalization. These seams permit UI-level tests without changing the runtime request.

**Alternatives considered**: Globally renaming every workflow “Run” action or actual published-workflow input dialog would cross the approved boundary and conflate transient tests with regular runs. Replacing the real status with a geometry-only mock would not prove the conditional component behavior.

### Align only equivalent publication entries

**Decision**: Use “Review & publish” on existing workflow and activity review-before-publication entries, including existing primary/menu presentations. Keep workflow “Publish” mode, remembered selection, and behavior distinct. Preserve Activity’s post-preflight “Continue publication review” step and do not add a menu.

**Rationale**: PublishActionButton represents two remembered workflow modes; ActivityDefinitionPublicationReview uses a separate prepare/review/publish flow. The entry label does not change either flow’s subsequent action. “Save now” calls the existing immediate save enqueue after clearing the pending autosave timer; the copy should retain that promise.

**Alternatives considered**: Renaming the separate “Publish” mode or the post-preflight continuation would change product semantics. Broadly normalizing all save labels is not supported by the specification.

### Preserve existing draft identity and authorization selection

**Decision**: Change the Activity Definition primary-action copy while preserving the current state selection and exact destination/request. Keep direct continuation as the preferred action for a complete confirmed list with exactly one editable active draft. Permit it without head or create permission. Permit “Create draft from current version” only with a complete confirmed zero-active list, the exact current head confirmed cloneable, and create permission. Permit “Create draft” only with a complete confirmed zero-active list, explicitly absent current head, and create permission. Otherwise retain “Open drafts” and exact identities.

**Rationale**: ActivityDefinitionWorkbench already computes the complete list only after a successful, non-fetching first page with no continuation; derives active drafts and edit permission; resolves the exact head independently; and creates using its exact version ID. The selector’s order matters: a single editable draft is handled before the head/create branch.

**Alternatives considered**: Selecting a draft from an incomplete page, treating an unreadable head as absent, or inferring clone permission from create permission would change behavior or invent authorization.

### Select layout only after real-component measurement

**Decision**: Establish browser geometry for actual components before deciding how to satisfy the fixed-viewport 1 CSS pixel requirement. Use WorkflowEditorToolbar with the actual conditional TestRunStatus, and the real Activity Definition authoring toolbar with real publication-status states. Compare action positions for the same definition and viewport; verify all actions remain visible through 320–1440 pixels.

**Rationale**: The existing workflow toolbar browser test covers save statuses and four viewport widths, but its fixture does not pass runStatus. In the real toolbar, conditional runStatus is rendered between the publication and Run controls. The Activity toolbar also contains the publication status generated by its review component. Their content may affect positions; a baseline is necessary before selecting a bounded layout change.

**Alternatives considered**: Choosing a fixed slot, CSS position, width, or toolbar architecture without measuring would assume the source of any shift and violate the root QA instruction. Measuring a fake status button would miss the real status component’s content and callback.

## Source-backed seams

All source pointers below refer to revision 3255701dcd9ed231d718aea0945dc3a6332c3866.

- WorkflowEditorToolbar accepts optional runStatus and renders it among the toolbar actions; its save status already has a status role and fixed sizing. See [WorkflowEditorToolbar.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorToolbar.tsx#L18) and [WorkflowEditorToolbar.css](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditorToolbar.css#L60).
- WorkflowEditor conditionally supplies TestRunStatus and its callback opens the Runtime panel. See [WorkflowEditor.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx#L1152).
- TestRunStatus is the actual conditional result/status button, carries the complete existing status text, and calls onOpenDetails. It currently says “Test run rejected” / “Test run dispatched” or the accepted runtime issue label; review capitalization against the approved Test Run terminology without suppressing state details. See [editorPanels.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorPanels.tsx#L100).
- The real workflow setup owns its heading, connected-environment warning, loading editor fallback, fields, and submit fallback. The lazy setup wrapper has a separate loading fallback. Preserve module-provided submit labels and the published-workflow run dialog boundary. See [WorkflowTestDrivePanel.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowTestDrivePanel.tsx#L231) and [lazyTestDriveUi.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/lazyTestDriveUi.tsx#L9).
- Workflow publication mode is stored as a remembered “review” or “publish” selection; the primary and menu labels must change only for the review entry. See [PublishActionButton.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/PublishActionButton.tsx#L34).
- ActivityDefinitionWorkbench derives its action from a complete confirmed draft list, exact editable draft, current head, and explicit allowed actions; the selected branch navigates by exact draft ID or clones by exact sourceVersionId. See [ActivityDefinitionWorkbench.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionWorkbench.tsx#L166).
- ActivityDefinitionDraftEditor composes Review & publish, Test Run, and Save now in its authoring toolbar. Save now clears the scheduled timer and enqueues the current draft immediately. See [ActivityDefinitionDraftEditor.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionDraftEditor.tsx#L305) and [ActivityDefinitionPublicationReview.tsx](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityDefinitionPublicationReview.tsx#L358).
- Component tests already cover workflow toolbar commands, remembered Publish mode, transient test setup, draft identity and authorization paths, publication review, and authoring copy. Extend those tests at their existing seams rather than changing production exports.
- Browser fixture [src.tsx](../../Studio/tests/browser/src.tsx#L133) currently mounts the actual toolbar but omits runStatus. Its [workflow-toolbar.spec.ts](../../Studio/tests/browser/workflow-toolbar.spec.ts#L5) checks save-status geometry at the required widths. [activity-definitions.spec.ts](../../Studio/tests/browser/activity-definitions.spec.ts#L215) exercises the actual Activity Definition Test Run and the routed authoring page.

## Validation baseline provenance

Root reports a pre-change Workflows client Vitest baseline at source revision 3255701dcd9ed231d718aea0945dc3a6332c3866: 2124 passed across 167 files, zero skips, 56.74 seconds. The external execution log is [/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3072-unit-baseline.log](/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3072-unit-baseline.log). This is a unit-suite baseline only. New copy regressions, actual conditional-status geometry, themes, and the expanded browser matrix remain unmeasured and must be verified by root before delivery.

