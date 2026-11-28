# Editing an Activity Definition

Opening a designed Activity Definition from the Library or its definition URL opens its editor. The existing Workflow Definition editor and the [shared module controls](../module-ui.md) are the interaction references.

The everyday surface shows the activity, its save/publication status, its authoring views and Test Run/Publish actions. History reveals drafts, versions and usage. Advanced reveals technical identity and provider details. Opening an activity does not create a draft or publish changes.

## Acceptance

- Resume the only active editable draft, including when published drafts occupy earlier pages. Multiple active drafts require a deliberate selection with readable labels.
- When no active draft exists, show the current published activity in the editor. Create a draft only after an authorized edit, and retain that first edit through autosave and reload.
- Keep technical details and lifecycle management collapsed by default; preserve direct draft, version and upgrade links and provide a route back from management to the editor.
- Explain read-only and unavailable states without choosing a different draft or overwriting server content. Retain save failures, conflicts, recovery and navigation protection.
- Verify collection return, browser Back/Forward, keyboard disclosure controls, the graph and contract views, and light/dark layouts at desktop and phone widths.
- Preserve a first edit when its clone request completes in a hidden workspace tab. Reopening a previously cloned draft after visiting another draft must load its current saved revision.

Source-owned activities use a matching editor in read-only mode. When no editor matches the exact version's provider and schema, they retain their existing inspection surface.

## Local verification

The focused unit suites cover entry resolution, permissions, first-edit cloning and retry, workspace reactivation, current-revision reload, management navigation, and block commands. Browser coverage includes progressive disclosure, keyboard navigation, collection history, existing authoring and publication journeys, and light/dark responsive layouts.

A task-specific composed preview uses the built Workflows module with existing Workbench and Studio host binaries and disposable stores. Its desktop-light and phone-dark journeys create an activity, return to its collection, reopen its editor, publish it, edit the published contract, and reload the saved change through real APIs. Opening the published document performs no draft creation; the first edit creates exactly one draft.

Local rendered evidence and independent visual review support this implementation. Formal UX acceptance remains incomplete until evidence is produced from a clean committed revision as required by [Studio UX acceptance](../../../docs/contributing/studio-ux-acceptance.md).
