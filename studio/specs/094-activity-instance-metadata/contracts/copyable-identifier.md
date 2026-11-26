# Contract: CopyableIdentifier

## Inputs

- `label`: visible and accessible semantic label, such as "Node ID"
- `value`: exact copy value
- optional class/style hooks that do not change behavior

## Rendering

- Render the label explicitly.
- Render the full value when it fits.
- Under constraint, use middle truncation for visual display.
- Make the exact full value available through title/tooltip and accessible naming.
- Never copy the truncated display string.

## Interaction

- Use a native keyboard-operable button.
- On success, briefly replace the copy glyph with a check glyph and announce
  "`{label} copied`" in a local polite live region.
- On failure, keep feedback local and announce "`Could not copy {label}`".
- Do not emit a global toast.

## Reuse

The Workflow Inspector uses the primitive for Node ID, Activity Type, and Activity Version ID.
Existing workflow identifier surfaces may migrate to it when touched, without changing their
domain-specific labels or surrounding actions.
