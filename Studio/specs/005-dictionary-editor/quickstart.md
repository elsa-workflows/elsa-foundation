# Quickstart: Dictionary Editor Validation

## Prerequisites

- Install repository dependencies with `pnpm install`.
- Run commands from the repository root.
- Use a Chromium-capable environment for the Playwright checks.

## Focused Validation

Run the dictionary helper, component, and property-panel integration tests:

```bash
pnpm --filter @elsa-workflows/studio-workflows test -- \
  src/__tests__/activityProperties.test.ts \
  src/__tests__/dictionaryValueEditor.test.tsx \
  src/__tests__/dictionaryExpressionEditor.test.tsx \
  src/__tests__/activityPropertyGroups.test.tsx
```

Expected outcome:

- Supported string-keyed dictionary families are recognized and unrelated or non-string-key types are rejected.
- Invalid table and JSON drafts preserve the last valid dictionary value.
- Add, edit, remove, eight-second Undo, filtering, tab guards, typed values, read-only behavior, and focus restoration pass.
- Literal and structured Object modes use the dictionary editor while other expression syntaxes keep their existing editors.

## Full Checks

```bash
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm lint
pnpm test:browser -- tests/browser/dictionary-editor.spec.ts
```

Expected outcome:

- The complete Workflows test suite and TypeScript checks pass.
- ESLint and CSS token linting pass without raw color literals.
- Existing non-dictionary property editors and expression syntaxes remain unchanged.
- Dictionary browser scenarios pass in Chromium.

## Browser Scenarios

### Wide inspector and expanded editor

1. Open the dictionary browser fixture at a standard desktop viewport.
2. Add at least six entries and verify that inline mode shows five stored rows, the remaining count, and the expanded-editor action.
3. Open the expanded editor and verify that Table is initially selected and uses a two-column key/value layout.
4. Filter by a key and a simple value, then clear the filter and confirm insertion order is unchanged.
5. Switch to JSON, enter valid strict object JSON, and verify Table reflects it.
6. Enter malformed JSON, a duplicate key, and an incompatible value.

Expected outcome: valid edits apply immediately; invalid edits show diagnostics and leave the last valid dictionary unchanged. Format JSON and Copy JSON work without unexpected value changes.

### Narrow inspector

1. Resize the inspector to its minimum supported width.
2. Add and edit entries inline, including a row with validation feedback.
3. Remove an entry and inspect the Undo notice.

Expected outcome: key and value controls stack without horizontal scrolling or obscured labels/actions, and the notice remains visible without covering rows.

### Keyboard-only workflow

1. Starting from an empty dictionary, use **Add entry** and create three entries without a pointer.
2. Press Enter from key to value and from a single-line value to the next key; verify the last valid row can create a new row.
3. Create a blank or duplicate draft and try to switch tabs; explicitly discard it.
4. Remove an entry, activate Undo, and verify the exact entry and position return.
5. Close the expanded dialog with Escape and confirm sensible focus restoration.

Expected outcome: all controls expose accessible names and visible focus; invalid drafts remain editable; multiline and complex value editors retain their normal Enter behavior; Undo is keyboard reachable and politely announced.
