# Data Model: Dictionary Entry Editor

## Dictionary type

`DictionaryTypeInfo` describes a recognized string-keyed CLR dictionary.

- `kind`: recognized family name.
- `keyTypeName`: always a supported string type for this feature.
- `valueTypeName`: recovered generic value type, or unknown when the descriptor omits it.
- `isValueNullable`: known nullable value-type state when recoverable.

Recognition is explicit. Unsupported keys, missing generic arguments, and unrelated two-argument types produce no dictionary type.

## Dictionary configuration

Optional UI metadata augments the descriptor:

- `keyLabel` / `valueLabel`: visible control labels; defaults are Key and Value.
- `keyPlaceholder` / `valuePlaceholder`: optional authoring hints.
- `keyComparison`: `ordinal` by default or `ordinalIgnoreCase`.

The first release has no custom regex rules, entry caps, sensitivity inference, or domain-specific semantics.

## Committed dictionary

The workflow-owned value is a plain JSON object. Property order is treated as insertion order for authoring and serialization. Empty is `{}`. Only valid, unique, compatible rows can alter this value.

## Editing row

An editor row contains:

- `id`: stable session identity independent of the editable key.
- `key`: current key text.
- `value`: current typed value or last valid value associated with a private complex draft.
- `originIndex`: original committed position where relevant to precise Undo.
- `status`: `committed`, `draft`, or `invalid`.
- `errors`: key/value diagnostics with stable ids for descriptions and error focus.

Rows are converted back to an object only when every participating key/value is valid. Incomplete draft rows may coexist with committed rows without entering the workflow value.

## Table draft

Session-only state for incomplete, duplicate, or type-invalid rows:

- stable row list and diagnostics;
- focused row/cell hints where needed for surface transitions;
- baseline external-value signature;
- pending complex-value JSON drafts.

The draft survives inline/expanded transitions and activity selection changes. It is cleared on workflow unload, explicit discard, or an external value replacement that no longer matches the baseline.

## JSON draft

Session-only raw dictionary text:

- `text`: exact author formatting;
- `diagnostics`: syntax, root, duplicate-key, nullability, and value-type failures;
- `baselineSignature`: committed value from which the draft began.

A valid compatible object commits immediately and clears invalid status. Invalid text persists during the session and never blocks saving the committed workflow.

## Editor session

Session state is keyed by stable workflow/activity/property identity rather than activity object reference because valid workflow edits immutably replace activity objects.

- table draft;
- raw JSON draft;
- per-entry complex JSON drafts;
- last active expanded tab;
- external committed-value signature.

### State transitions

```text
Committed value
  -> add/edit row -> Valid rows -> emit new committed value
  -> add/edit row -> Invalid table draft -> fix -> emit new committed value
                                      \-> discard -> Committed value
  -> edit raw JSON -> Valid object -> emit new committed value
  -> edit raw JSON -> Invalid JSON draft -> fix -> emit new committed value
                                    \-> discard -> Committed value
  -> external replacement -> clear private drafts -> New committed value
```

## Undo notice

A shared notice contains a message, optional action label/callback, eight-second remaining lifetime, paused/dismissed state, and replacement identity. The dictionary supplies an inverse that reinserts the exact removed entry at its original index. The notice does not own workflow history.
