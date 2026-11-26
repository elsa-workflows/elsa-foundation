# Feature Specification: Dictionary Entry Editor

**Feature Branch**: Not created (current worktree is detached)

**Created**: 2026-07-12

**Status**: Ready for planning

**Input**: Replace the ambiguous single-line experience for dictionary-valued activity inputs with a reusable, accessible key/value editor and an expanded table/raw-JSON workflow.

## User Scenarios & Testing

### User Story 1 - Edit dictionary entries inline (Priority: P1)

A workflow author can view and edit a string-keyed dictionary as explicit key/value rows in the activity properties inspector instead of manipulating an empty or stringified field.

**Why this priority**: Adding, changing, and removing entries is the primary workflow and must be understandable without knowing the serialized representation.

**Independent Test**: Open a supported dictionary property, add and edit several rows with keyboard and pointer input, remove one, undo the removal, and verify the resulting dictionary.

**Acceptance Scenarios**:

1. **Given** an empty supported dictionary, **When** the author selects Add entry and supplies a unique nonblank key and valid value, **Then** the entry is committed and remains visible.
2. **Given** an existing dictionary, **When** the author changes a key or value to another valid value, **Then** the dictionary updates immediately without changing the order of other entries.
3. **Given** an entry is removed, **When** the author activates Undo within eight seconds, **Then** the exact entry returns to its original position.
4. **Given** more than five entries, **When** the property is viewed inline, **Then** no more than five stored rows are shown and the remaining count plus an expanded-editor action is available.
5. **Given** a narrow inspector, **When** rows are displayed, **Then** key and value controls stack without obscuring labels, validation, or row actions.

---

### User Story 2 - Resolve invalid entry drafts safely (Priority: P1)

A workflow author can temporarily create incomplete or duplicate rows while typing without corrupting the last valid dictionary value or losing unfinished work unexpectedly.

**Why this priority**: Key editing naturally passes through blank and duplicate states; committing those states would lose or overwrite data.

**Independent Test**: Create blank, whitespace-only, duplicate, and type-invalid rows, navigate between inline and expanded surfaces, and verify that errors remain editable while the last valid value is preserved.

**Acceptance Scenarios**:

1. **Given** a new blank row, **When** focus leaves the row, **Then** the row remains as an invalid draft and the stored dictionary remains unchanged.
2. **Given** duplicate keys under the configured comparison mode, **When** either key is edited, **Then** both conflicts are identified and neither conflicting change is committed.
3. **Given** an incomplete inline draft, **When** the expanded editor opens, **Then** the same draft appears there.
4. **Given** an incomplete Table draft, **When** the author attempts to switch to JSON, **Then** the author must fix or explicitly discard the draft first.
5. **Given** an external operation replaces the property value, **When** the editor observes the replacement, **Then** stale private drafts are discarded rather than applied over the new value.

---

### User Story 3 - Work with large dictionaries in an expanded editor (Priority: P2)

A workflow author can open the existing expanded property dialog to edit the full dictionary in a spacious table and filter entries without changing their stored order.

**Why this priority**: Inline editing is optimized for small dictionaries; larger dictionaries need room and findability.

**Independent Test**: Open a dictionary containing more than five entries, filter it, add and edit entries, clear the filter, and verify that insertion order is unchanged.

**Acceptance Scenarios**:

1. **Given** a dictionary property, **When** the expanded editor opens for the first time, **Then** Table is selected and all committed entries are available.
2. **Given** a filter term, **When** it is entered, **Then** matching keys and simple displayed values remain visible without reordering or modifying the dictionary.
3. **Given** invalid or incomplete drafts, **When** a filter is active, **Then** those drafts remain visible regardless of the filter.
4. **Given** a filter is active, **When** Add entry is activated, **Then** the filter clears and the new row receives focus.
5. **Given** the author previously selected a valid tab, **When** the dialog reopens during the session, **Then** that tab is restored; unresolved invalid drafts reopen on their owning tab.

---

### User Story 4 - Edit and inspect raw JSON (Priority: P2)

A workflow author can switch the expanded editor to strict raw JSON for bulk or advanced editing while retaining typed validation and safe draft behavior.

**Why this priority**: Raw JSON is the efficient representation for bulk dictionary changes and must be a first-class part of the expanded workflow.

**Independent Test**: Edit valid and invalid objects in JSON, exercise formatting and copying, switch tabs, close and reopen the dialog, and verify that only valid compatible objects update the dictionary.

**Acceptance Scenarios**:

1. **Given** the JSON tab opens, **When** no private draft exists, **Then** it shows the dictionary as two-space pretty-printed strict JSON while preserving entry order.
2. **Given** a valid compatible JSON object, **When** it is edited, **Then** the dictionary updates immediately and Table reflects the change.
3. **Given** malformed JSON, a non-object root, duplicate keys, or an incompatible value, **When** it is entered, **Then** diagnostics identify the problem and the last valid dictionary remains unchanged.
4. **Given** an invalid JSON draft, **When** the author tries to switch to Table, **Then** the author must fix or explicitly discard the draft.
5. **Given** an invalid JSON draft, **When** the dialog closes and later reopens in the same workflow-editing session, **Then** the draft and diagnostics remain available without blocking workflow save or autosave.
6. **Given** JSON content, **When** Format JSON or Copy JSON is activated, **Then** formatting is explicit and copy success is announced without changing the dictionary unexpectedly.

---

### User Story 5 - Edit typed and complex values accessibly (Priority: P2)

A workflow author can use the appropriate existing control for scalar or contributed value types and can expand complex values for per-entry JSON editing without nested dialogs.

**Why this priority**: A reusable dictionary editor must not reduce every value to text or display complex values as meaningless strings.

**Independent Test**: Exercise string, numeric, Boolean, enum/option, contributed, nullable, and complex values in editable and read-only states using keyboard-only navigation and assistive semantics.

**Acceptance Scenarios**:

1. **Given** a scalar or editor-backed value type, **When** a row is shown, **Then** the matching typed value editor is used.
2. **Given** a complex value type inline, **When** the row is shown, **Then** a summary and expanded-editor affordance appear instead of a stringified object.
3. **Given** a complex value in the expanded dialog, **When** its detail region opens, **Then** the author can edit that value as JSON within the row and receive diagnostics without opening another modal.
4. **Given** an invalid complex-value draft, **When** the author attempts to switch the whole dictionary to JSON, **Then** the author must fix or discard the per-entry draft first.
5. **Given** a read-only property, **When** it is inspected, **Then** filtering, tab switching, selecting, formatting, and copying remain available while all mutations are disabled.

### Edge Cases

- Null or unset dictionary values present as an empty dictionary; stored dictionaries are plain JSON objects from the first release and require no legacy migration.
- All-whitespace keys are invalid; otherwise key whitespace is preserved and never trimmed silently.
- Duplicate comparison defaults to exact ordinal comparison and can be configured as case-insensitive.
- Duplicate keys in raw JSON are rejected rather than silently using the last occurrence.
- Null values are rejected for known non-nullable value types and accepted for reference or structurally unknown complex types.
- Arrays and primitive JSON roots are rejected.
- Strict JSON excludes comments and trailing commas.
- Multiline and complex value controls retain Enter for content; spreadsheet-like Enter movement applies only to key and single-line value controls.
- Drafts survive activity selection changes during the current workflow-editing session but are discarded when the workflow unloads or the external property value is replaced.
- Removing an entry while filtering restores it at its original position when undone.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST recognize supported string-keyed dictionary interfaces and concrete families while excluding arbitrary-key and unrelated two-argument generic types.
- **FR-002**: The initial supported families MUST include mutable, read-only, sorted, concurrent, immutable, and immutable-sorted dictionary variants.
- **FR-003**: The editor MUST persist dictionary values as plain JSON objects and represent an empty dictionary as an empty object.
- **FR-004**: Inline editing MUST provide labeled key and value controls, Add entry, Remove, validation, Undo, a five-row stored-entry limit, remaining-entry count, and an expanded-editor action.
- **FR-005**: The editor MUST preserve insertion order for display and serialization and MUST NOT offer reordering or temporary sorting.
- **FR-006**: A key MUST be nonblank and unique under the configured comparison mode before its row can alter the stored dictionary.
- **FR-007**: The editor MUST retain incomplete and invalid table content privately while preserving the last valid dictionary.
- **FR-008**: Valid key and value changes MUST update the dictionary immediately.
- **FR-009**: The expanded editor MUST reuse the product's existing modal property-editor interaction and provide Table and JSON tabs.
- **FR-010**: Expanded Table MUST support client-side filtering across keys and simple displayed values without changing order or data.
- **FR-011**: JSON editing MUST accept strict object-root JSON only and MUST diagnose syntax errors, duplicate keys, unsupported roots, and known value-type mismatches before committing.
- **FR-012**: Raw JSON MUST support explicit formatting and copying while preserving the author's in-progress formatting between explicit format actions.
- **FR-013**: Invalid JSON MUST remain a private session draft, survive dialog close/reopen, and MUST NOT block saving the last valid workflow.
- **FR-014**: Switching between Table and JSON MUST require invalid drafts on the source tab to be fixed or explicitly discarded.
- **FR-015**: Scalar and contributed value types MUST use their matching value editor; complex values MUST use a summary inline and an expandable per-entry JSON editor in the expanded dialog.
- **FR-016**: Known non-nullable value types MUST reject null and type-incompatible JSON values without silent coercion.
- **FR-017**: The dictionary configuration MUST support custom key/value labels, placeholders, and exact or case-insensitive key comparison; defaults MUST be Key, Value, and exact comparison.
- **FR-018**: The editor MUST apply to Literal and structured Object value modes and defer all other expression syntaxes to their existing editors.
- **FR-019**: Removing an entry MUST be immediately reversible for eight seconds through a reusable, domain-neutral undo notice; focus or hover MUST pause its timer.
- **FR-020**: Undo MUST restore the exact entry at its original position and MUST be dismissed when another undoable action or an incompatible subsequent edit makes the inverse unsafe.
- **FR-021**: The undo notice MUST be non-modal, keyboard reachable, announced politely, and placed at the bottom of the active inspector or expanded dialog without obscuring rows.
- **FR-022**: Keyboard entry MUST move from key to value and from a single-line value to the next row, create a row from the last valid row, focus newly added rows, and restore sensible focus after removal.
- **FR-023**: Narrow inline rows MUST stack key and value controls; the expanded dialog MUST retain a two-column table.
- **FR-024**: Read-only mode MUST retain inspection, filtering, tab, selection, formatting, and copying capabilities while disabling mutations.
- **FR-025**: Validation MUST appear at affected rows and in expanded-tab error counts; the expanded editor MUST offer a route to focus the first error.
- **FR-026**: Invalid drafts MUST remain visible while filtering, and adding an entry while filtering MUST clear the filter before focusing the new row.
- **FR-027**: The first expanded open MUST select Table, subsequent opens MUST remember the last valid tab for the property, and unresolved drafts MUST reopen on their owning tab.
- **FR-028**: The feature MUST NOT add arbitrary key types, custom domain validation, entry limits, virtualization, sensitive-value guessing/masking, legacy parsing, or a second workflow-history system.

### Key Entities

- **Dictionary value**: The last valid ordered set of unique string keys and typed values that belongs to the workflow draft.
- **Dictionary entry**: A key, typed value, and stable editing identity used to preserve row focus and insertion order.
- **Table draft**: Session-only incomplete or invalid row content that has not altered the dictionary value.
- **JSON draft**: Session-only raw text and diagnostics retained when the text is not a valid compatible dictionary object.
- **Dictionary configuration**: Optional labels, placeholders, and key-comparison behavior supplied with the property description.
- **Undo notice**: A reusable transient action message containing an exact inverse operation, lifetime, accessibility state, and dismissal rules.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A user can create a three-entry string dictionary from empty using only the keyboard in under 45 seconds without touching raw JSON.
- **SC-002**: Across all tested invalid-key, invalid-value, and invalid-JSON cases, zero invalid edits overwrite or discard the last valid dictionary.
- **SC-003**: A user can locate and edit one entry in a 100-entry dictionary through the expanded filter in under 20 seconds.
- **SC-004**: Every mutation and inspection control is operable with keyboard-only navigation and exposes an accessible name, visible focus, disabled state, and validation feedback.
- **SC-005**: Inline editing remains usable without horizontal scrolling at the minimum supported inspector width, and expanded editing exposes all controls at standard desktop viewport sizes.
- **SC-006**: Removal Undo restores the exact key, value, and position in every tested inline, expanded, filtered, and typed-value scenario.
- **SC-007**: Existing non-dictionary property editors and non-value expression syntaxes retain their current behavior in regression coverage.

## Assumptions

- Dictionary values are introduced as plain JSON objects; there is no legacy serialized dictionary format to migrate.
- String keys are the only supported keys in this iteration because object-root JSON cannot faithfully represent arbitrary key types.
- Exact ordinal key comparison is the safe generic default; properties such as HTTP headers opt into case-insensitive comparison through their supplied configuration.
- Existing property and expression editor contributions remain the source of truth for typed values and non-dictionary syntaxes.
- Existing workflow save/autosave and global undo/redo remain authoritative; the undo notice is a reusable presentation and inverse-action primitive, not another history stack.
- Domain-specific validation, secret handling, bulk import/export outside raw JSON, sorting, reordering, and extreme-scale virtualization are follow-up concerns.
