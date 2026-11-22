# Data model: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

No persisted data changes. The model is the activity map's member rule.

## Elsa3NullConversion (new, `Extensions/Elsa3/src/Mapping/ActivityMap`)

| Field | Meaning |
|---|---|
| `Rule` | `NoBinding`, `TypedNull`, `Emit` or `Refuse` |
| `Value` | For `Emit`: the literal Elsa 3 ran with (`""`, `[200]`) |
| `Reason` | For `Refuse`: why Elsa 4 cannot reproduce or represent it, shown in the diagnostic |
| `WhenBound` | For `Refuse`: refuse only when this other Elsa 3 member is bound; otherwise a typed null |

## Elsa3MemberRule (changed)

Gains `OnNull` (`Elsa3NullConversion?`, default null).
- Required for an input rule of kind R1 Carry or R5 Narrowed contract, unless the rule carries an Elsa 3 default it
  emits.
- Set on an R2 default flip only where its null converts otherwise than its default: SendHttpRequest.ContentType, whose
  default applies only when Content is bound.
- Unset on every other rule kind; outputs, removed inputs, restrictions, structure and graph members keep their existing
  null handling.

## Conversion of a null member

| `OnNull` | Converted node |
|---|---|
| `NoBinding` | no binding; the Elsa 4 default applies |
| `TypedNull` | `ArgumentState(key, new ArgumentValue(null, "Literal"))` |
| `Emit(v)` | `ArgumentState(key, new ArgumentValue(v, "Literal"))` |
| `Refuse(r)` | node fails with `ELS3-ACTIVITY-NULL-UNCONVERTIBLE` |
| none, R2 / R5 default | the default `ApplyDefaults` already emits |

## #3281 proposed source-shape corrections

The original OnNull model remains delivered. The follow-up adds nullable init properties `OnAbsent`, `OnBlank`
and `OnLiteralNull` to the existing member-rule body; its positional constructor stays intact. Each reuses existing
conversion actions and defaults to no correction for existing/custom maps.

Importer-local source classification distinguishes supported defaulted wrappers from a resolved Literal whose value
is null/omitted, literal blank text and explicit empty collections. It is not persisted state or a workflow policy.
Classify before default/unbound handling; preserve the original inventory and caller JSON.

Focused built-in narrowed contracts cover nonempty Flow status lists, positive literal Timer intervals and response
statuses whose raw value will not become a target default. Check explicit empty lists before an unbound shortcut.
Existing generic enum/time-span contracts retain their custom-map semantics. Add no general empty-list policy,
service or strategy. Generic portable/pinned null/missing/undefined policy data remains owned by spec1232.
