# Data Model: Groundwork Physicalization And Performance

## Physicalization Policy

Declares whether a storage unit uses portable storage, optimized provider physicalization, or specialized provider ownership.

Fields:

- `Kind`: Portable, Optimized, or Specialized.

Validation:

- Portable remains the default.
- Optimized units can be projected from eligible declared indexes.
- Specialized remains declarative only in G7; it is not implemented by the document store.

## Physicalized Field Plan

Provider-neutral plan entry describing one projected field.

Fields:

- `Name`: Stable provider-owned field name derived from the declared index identity.
- `Path`: JSON content path used to extract the projection value.
- `ValueKind`: Declared index value kind.
- `IsUnique`: Whether the projected value must be unique.
- `IsSortable`: Whether the projected value may support sort-oriented provider indexes later.

Validation:

- Only single-field declared indexes are eligible in G7.
- Missing values are excluded from projections.

## Optimized Projection Structure

Provider-owned physical structure used to answer eligible equality queries.

Relational shape:

- One projection table per optimized storage unit.
- Document identity and version columns.
- One nullable text column per physicalized field.
- Provider-owned indexes over projected field columns.

MongoDB shape:

- `physicalized` subdocument containing projected field values.
- Provider-native indexes over `physicalized.<field>`.

Validation:

- Projection updates commit atomically with the document write where the provider supports transactions.
- Stale expected versions do not update projections.
