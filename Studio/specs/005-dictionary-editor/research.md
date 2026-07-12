# Research: Reusable Dictionary Editor

## Decision 1: Integrate through the activity property editor pipeline

**Decision**: Add dictionary recognition and rendering at the existing activity-input property seam in `src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`. `PropertyRow` remains responsible for syntax selection, read-only state, expression transitions, inline rendering, and launching the expanded surface. A string-keyed dictionary is treated as a structured collection editor before the generic collection repeater or scalar property editor is selected.

The dictionary experience is available for the `Literal` and admitted structured `Object` modes. Other syntaxes continue to resolve their registered expression editor contributions. The dictionary editor is considered a collection editor for layout purposes so the inline syntax-picker overlay cannot cover a multi-row table. Unlike the current generic collection repeater, it is explicitly expandable in both supported modes.

**Rationale**: `PropertyRow` already owns all value/wire updates through `setRaw`, expression-mode replacement confirmation, read-only derivation, contributed editor resolution, and expanded-editor state. Integrating there preserves the existing authoring pipeline and avoids a special Headers-only path.

**Alternatives considered**:

- Register a Headers-specific property editor contribution. Rejected because the feature must work for all supported string-keyed dictionary families.
- Treat dictionaries as ordinary collections. Rejected because dictionaries need key uniqueness, key comparison metadata, object-shaped JSON, and key/value row semantics rather than element ordering controls.
- Replace the expression editor pipeline. Rejected because JavaScript, Liquid, references, and future syntaxes must retain their existing editors.

## Decision 2: Reuse and harden the existing expanded property modal

**Decision**: Extend `ExpandedPropertyEditor` in `ActivityPropertiesPanel.tsx` and its `.wf-property-editor-*` styles in `src/Elsa.Studio.Workflows/Client/src/styles.css` rather than introduce a drawer or a second dictionary-specific overlay. Extracting a reusable Workflows modal shell is appropriate if it keeps the existing property-editor appearance and call site.

As part of this feature, harden the modal to:

- trap focus within the dialog;
- focus the intended initial control;
- restore focus to the expand trigger on close;
- keep Escape and the visible Close controls consistent;
- prevent background interaction and page scrolling while modal;
- preserve guarded invalid drafts rather than silently discarding them; and
- remain correctly layered above the designer.

**Rationale**: The current modal already supplies the property title, type chip, syntax picker, description, immediate-update message, and an expanded expression-editor surface. Reusing it keeps the expanded dictionary experience consistent with text, Object, JavaScript, and Liquid editing.

**Source evidence and risk**: The current implementation is a custom fixed backdrop with `<section role="dialog" aria-modal="true">`, a window-level Escape listener, and manual fallback focus. It has no focus trap, explicit focus restoration, portal, background-scroll lock, or backdrop dismissal behavior. Its z-index is `90`, while the host's Radix `DialogHost` uses the `1200` layer. These gaps conflict with the keyboard-only and focus-restoration acceptance criteria and must be addressed rather than inherited.

**Alternatives considered**:

- Use the host `DialogHost`. Rejected as a direct implementation seam: it only supports queued alert/confirm/prompt requests, is coupled to the host singleton dialog controller, and is intentionally excluded from the public `@elsa-workflows/studio-ui` barrel.
- Add `@radix-ui/react-dialog` directly to Workflows. Viable if extracting a sound local modal shell proves too costly, but it broadens the module dependency boundary and should not be necessary merely to render this feature.
- Add a drawer. Rejected by the product decision to standardize on the current expanded modal.

## Decision 3: Use the shared tab keyboard behavior, but own linked panel IDs locally

**Decision**: Reuse `StudioTabs`/`useTablistKeyboard` behavior from `src/Elsa.Studio.Web/Client/src/app/ui/layout/Tabs.tsx` for the expanded **Table** and **JSON** tabs. Preserve its automatic activation, roving `tabIndex`, Arrow key wrapping, and Home/End behavior. Render correctly linked `role="tabpanel"` regions from the dictionary editor using IDs generated in the same component.

**Rationale**: The shared tabs already implement the WAI-ARIA keyboard interaction used elsewhere in Studio and are exported through `@elsa-workflows/studio-ui`.

**Source evidence and risk**: `StudioTabs` creates its `baseId` internally, while `StudioTabPanel` requires a caller-supplied `baseId`. A consumer cannot currently guarantee that the tab's `aria-controls` matches that helper's panel ID. The dictionary editor must not combine these two APIs as-is. It may either render a small local tablist using `useTablistKeyboard`, or the shared tabs API may be improved in a separately coordinated change.

**Alternatives considered**:

- Use unadorned buttons without tab roles. Rejected because it loses established keyboard and accessibility behavior.
- Use the shared `StudioTabPanel` without matching IDs. Rejected because it creates invalid ARIA relationships.

## Decision 4: Reuse StudioCodeEditor for raw JSON

**Decision**: Render the raw dictionary tab with `StudioCodeEditor` from `@elsa-workflows/studio-code-editor`, a JSON language adapter, an `elsa://` document URI, explicit `ariaLabel`, read-only propagation, and `StudioCodeDiagnostic` entries. Use `ObjectExpandedEditor` in `src/Elsa.Studio.Workflows/Client/src/objectExpressionEditor.tsx` as the interaction precedent: editor text is local draft state, valid values emit through `onChange`, and invalid text remains private.

Dictionary JSON parsing is stricter than the existing Object editor:

- the top level must be a JSON object, never an array or primitive;
- strict JSON only, without comments or trailing commas;
- no legacy string-value normalization;
- duplicate keys must be detected before conversion to a JavaScript object, using the configured ordinal or ordinal-ignore-case comparison; and
- known value types are validated without silent coercion.

The toolbar owns explicit **Format JSON** and **Copy JSON** actions. Formatting is never automatic while typing or switching tabs.

**Rationale**: StudioCodeEditor already supplies the CodeMirror-backed editing experience, lazy fallback editor, JSON language loading, read-only mode, accessible label, and diagnostic presentation. A second textarea-based JSON editor would regress consistency and diagnostics.

**Source evidence and risk**: `JSON.parse` silently overwrites duplicate object keys, so the existing `parseStructuredJson` helper is insufficient. The code-editor API also has no imperative focus-to-diagnostic contract. The dictionary error summary therefore needs local focus targets for row errors and an explicit editor focus strategy for JSON errors; any request to focus a precise CodeMirror range would require a code-editor API enhancement.

**Alternatives considered**:

- Reuse `ObjectExpandedEditor` directly. Rejected because it accepts arrays, parses legacy JSON strings, lacks dictionary value-type validation, and cannot detect duplicate keys.
- Use a plain textarea. Rejected because StudioCodeEditor is already the shared code-editing substrate.

## Decision 5: Store private drafts by stable editing-session identity

**Decision**: Introduce a dictionary draft store scoped to the active workflow-editing session and addressed by stable activity identity plus property name. It owns incomplete table rows, invalid complex-value drafts, invalid raw JSON text, and the last successfully used tab. Inline and expanded surfaces read and update the same draft record.

Clear a record when the user explicitly discards it or when the property's external value is replaced by an operation that invalidates the draft. Clear the session when the workflow unloads. Drafts survive selection changes and modal close/reopen within that session.

**Rationale**: The product behavior requires unfinished table rows and invalid JSON to survive moving between inline and expanded surfaces and selecting another activity, while never mutating the last valid dictionary.

**Source evidence and risk**: `objectExpressionEditor.tsx` uses a `WeakMap<object, Map<string, string>>` keyed by the activity object. That works for a single invalid JSON draft because invalid edits do not emit an activity update. It is insufficient for a dictionary where one valid row can be committed while other rows remain incomplete: property updates immutably replace the activity object, causing drafts attached only to the old object reference to disappear. A stable activity/property key with explicit lifecycle is required.

**Alternatives considered**:

- Key drafts only by the activity object as Object editing does. Rejected because immutable valid updates lose concurrent table drafts.
- Store invalid rows in the persisted workflow value. Rejected because invalid or duplicate keys must never replace the last valid dictionary or block workflow save/autosave.
- Keep draft state only inside the modal. Rejected because inline and expanded surfaces must share it and drafts must survive modal closure.

## Decision 6: Add a shared, domain-neutral ActionNotice primitive

**Decision**: Add a reusable action notice under the shared UI feedback seam, conceptually `src/Elsa.Studio.Web/Client/src/app/ui/feedback/ActionNotice.tsx`, and export it from `app/ui/shared.ts` for `@elsa-workflows/studio-ui` consumers. Its API accepts a message, optional action label/callback, dismiss behavior, and placement class. The primitive owns:

- an eight-second lifetime;
- pausing the timer on hover and keyboard focus;
- polite live-region announcement;
- keyboard-reachable action and dismiss controls;
- replacement by the next notice; and
- safe timeout cleanup.

The caller owns semantic reversibility. Dictionary removal supplies an inverse operation that reinserts the exact key/value entry at its original display position. The notice is anchored by its active surface: inside the expanded modal when open, otherwise at the bottom of the inspector viewport. Copy success uses the same presentation without an Undo action.

**Rationale**: No reusable toast, snackbar, or undo-notice primitive exists. Existing `.wf-status-line` feedback is page-level and persistent; global draft history is a separate concern. A presentation-only notice can support future reversible actions without becoming a second workflow history system.

**Alternatives considered**:

- Couple dictionary removal to `useDraftHistory`. Rejected because global Ctrl/Cmd+Z remains authoritative and the notice needs a precise, short-lived inverse operation.
- Build the notice only inside the dictionary editor. Rejected because reuse for similar future actions is an explicit requirement.
- Add a global notification center. Rejected because the required feedback is surface-local and a global queue/state system is unnecessary scope.

## Decision 7: Extend token-governed Workflows styling and shared primitives

**Decision**: Add dictionary-specific styles to `src/Elsa.Studio.Workflows/Client/src/styles.css`, adjacent to the existing property-row, expanded-property-editor, and Object JSON editor styles. Use `--studio-*` tokens or the module's existing `--wf-*` aliases only. Reuse `StudioButton` and `StudioSearchInput` from `@elsa-workflows/studio-ui` for common controls.

The inline editor renders at most five committed entries plus current draft rows. At narrow inspector widths, key and value fields stack, their visible labels remain present, and remove stays at the upper right. The expanded modal retains a two-column key/value table. Filtering changes visibility only; it never changes insertion order. Invalid and incomplete rows remain visible under filtering.

The existing modal selectors (`.wf-property-editor-*`), property form selectors (`.wf-property-row*`), Object code-editor selectors (`.wf-object-json-editor*`), and black-glass overlay treatment provide the visual recipes to extend. Add explicit responsive dictionary rules because current media queries do not adapt property rows.

**Rationale**: Keeping module layout in the Workflows stylesheet preserves theme behavior, CSS lint enforcement, and the current inspector/modal look. Shared primitives keep button, search, focus, and material/glass recipes consistent.

**Alternatives considered**:

- Use raw colors or a dictionary-private token namespace. Rejected by the project's design-token contract and CSS lint rules.
- Put all dictionary layout into the host stylesheet. Rejected because the feature and its layout are owned by the Workflows module.
- Add virtualization. Rejected for this iteration because it complicates variable-height value editors, draft visibility, validation, and focus restoration without measured need.

## Decision 8: Reuse the robust clipboard fallback

**Decision**: Reuse or extract the behavior of `copyTextToClipboard` from `src/Elsa.Studio.Workflows/Client/src/workflow-editor/editorHelpers.ts` for **Copy JSON**. It uses `navigator.clipboard.writeText` when available and falls back to a temporary read-only textarea plus `document.execCommand("copy")`.

**Rationale**: Several modules call the optional Clipboard API directly, but this helper already handles browsers or test environments where it is unavailable. Copy success is announced through ActionNotice; copy failure remains an actionable error and must not report false success.

**Alternatives considered**:

- Call `navigator.clipboard?.writeText` and do nothing when unavailable. Rejected because it silently fails.
- Implement a second dictionary-specific fallback. Rejected as unnecessary duplication.

## Decision 9: Build accessibility into row and validation semantics

**Decision**: Give every editable row stable control IDs, visible key/value labels, and row-local diagnostic IDs referenced with `aria-describedby`. Remove buttons identify the relevant row/key. Newly added rows focus their key field. Enter advances through eligible single-line key/value controls, while multiline and complex editors keep native Enter behavior. After removal, focus moves to the nearest remaining row or **Add entry**.

The expanded dialog shows error counts in tab labels and an error summary that focuses the first actionable problem. Routine persistent validation text is described to controls; live regions announce newly occurring actions and state changes without repeatedly asserting every render. Read-only mode preserves navigation, filtering, tab switching, selection, and copying while disabling mutations.

**Rationale**: These behaviors satisfy the agreed keyboard-only workflow and prevent validation or row mutation from stranding focus. They also complement, rather than duplicate, StudioCodeEditor's diagnostic status region and the shared tab keyboard pattern.

**Alternatives considered**:

- Rely on placeholder text as labels. Rejected because placeholders are not durable accessible names.
- Move focus to the top of the editor after every mutation. Rejected because it disrupts efficient row editing.
- Mark all validation containers as assertive alerts. Rejected because repeated live announcements would be noisy while typing.
