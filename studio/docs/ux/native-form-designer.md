# Native Task Form Designer

The reusable task form editor is a visual form-definition tool, not a workflow response form. Its reference is the form-editor screenshot attached to the originating Studio issue; recipient rendering is shared with `BasicTaskForm` so authored fields preview the same controls used by task recipients.

## Acceptance

- Authors can add text, long text, number, yes/no, date, and choice fields from a discoverable palette. The palette reports the number of fields in the form and the 100-field maximum, explicitly indicating when the limit is reached.
- The canvas supports selection, pointer and keyboard drag sorting, explicit move controls, duplication, and removal. Stable editor row identities are separate from persisted response keys.
- The properties panel edits labels, response keys, field type, required/read-only state, help text, and field-specific validation. Applicable advanced controls are collapsed initially and titled Validation or Choices; field types without such controls have no empty disclosure.
- Empty or duplicate keys, missing labels, invalid choice lists, reversed numeric bounds, invalid character limits, and more than 100 fields are explained and prevent publication.
- Recipient preview uses the same `BasicTaskForm` renderer as tasks. Preview responses are temporary and never enter the immutable form publication payload.
- The layout uses the documented `--studio-*` token contract, has accessible names for field actions, and stacks palette, canvas, and properties at narrow widths.
- Existing form permissions, immutable version creation, task pinning, server-side validation, and version-conflict recovery remain authoritative and unchanged.

## Local verification

The UserTasks component tests cover field kinds, generated keys, selection stability, validation, field counts and limits, applicable advanced controls, and preview-data isolation from publication. Playwright coverage exercises the real designer component for recipient preview, keyboard and pointer reordering, named landmarks, compact palette icons, field-specific properties, narrow-screen overflow, and screenshots.

The local Vite fixture validates the designer component but is not a composed Elsa host. Formal UX acceptance and independent visual review remain separate from this fixture and require evidence from a clean committed revision as described by [Studio UX acceptance](../../../docs/contributing/studio-ux-acceptance.md).