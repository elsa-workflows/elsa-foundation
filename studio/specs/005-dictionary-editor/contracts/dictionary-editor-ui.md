# UI Contract: Dictionary Entry Editor

## Descriptor metadata

Activity input UI specifications may expose an optional dictionary configuration:

```ts
interface StudioDictionaryInputUISpecification {
  keyLabel?: string;
  valueLabel?: string;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  keyComparison?: "ordinal" | "ordinalIgnoreCase";
}
```

Unknown configuration fields remain forward-compatible. Missing or invalid values fall back safely to Key, Value, no placeholders, and ordinal comparison.

## Editor admission

The dictionary editor claims a descriptor only when the CLR type belongs to the explicit supported dictionary family, the generic key is a supported string type, and the current expression mode is Literal or structured Object. Other syntaxes and unsupported types retain existing editor resolution.

## Value contract

- Input/output committed values are plain JSON objects.
- Empty/unset displays as `{}`.
- Row order is preserved when objects are rebuilt.
- Invalid private drafts never pass through `onChange`.
- Valid edits call `onChange` immediately with a newly allocated object.
- No legacy string or entry-array representation is parsed.

## Inline surface

- Shows an empty state or up to five committed rows plus current private drafts.
- Provides Add, Remove, Undo, validation, remaining count, and expanded action.
- Delegates scalar/contributed values through a synthetic value descriptor.
- Summarizes complex values and routes editing to the expanded surface.
- Stacks key/value controls at narrow inspector widths.

## Expanded surface

- Uses the existing property modal shell with focus isolation, initial focus, Escape, and focus restoration.
- Provides linked Table and JSON tabs with Arrow/Home/End keyboard behavior.
- Table offers filtering but no sorting or reordering.
- JSON uses strict object-root parsing, duplicate preservation/detection before conversion, explicit Format and Copy, and typed value diagnostics.
- Invalid source-tab drafts require Fix or Discard before switching.
- Complex values expand within their Table row rather than opening a nested modal.

## Shared action notice

The shared primitive accepts content and an optional caller-owned action:

```ts
interface StudioActionNoticeProps {
  message: string;
  actionLabel?: string;
  onAction?(): void;
  durationMs?: number;
  onDismiss?(): void;
}
```

It owns polite announcement, keyboard reachability, timeout, hover/focus pause, replacement, and dismissal. Its container owns placement. It has no workflow or dictionary dependency.

## Accessibility contract

- Every cell has a visible or accessible label tied to property and row context.
- Errors are connected to their fields; the expanded summary focuses the first invalid control.
- Add focuses the new key; Enter advances through eligible single-line cells; removal restores a nearby focus target.
- Timed notices pause while hovered or focused.
- Read-only mode disables mutations while retaining filtering, tab navigation, formatting, selection, and copying.
- Modal content prevents background interaction and returns focus to the invoking control.
