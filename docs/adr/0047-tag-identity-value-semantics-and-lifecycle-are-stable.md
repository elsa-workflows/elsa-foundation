# Tag Identity, Value Semantics, And Lifecycle Are Stable

Status: accepted (2026-07-19; ratified through the workflow-tagging design grilling session)

## Context

Tags are used in saved filters, API clients, source declarations, automation, and audit history.
Treating a display name or free-text value as identity makes cosmetic renames destructive.
Allowing value mode or cardinality to change after assignments exist makes the meaning of existing
data ambiguous. Hard deletion similarly breaks historical queries and saved views.

Elsa also needs both GitHub-like markers and Azure-like key/value classification without forcing
every useful value into a predeclared catalog.

## Decision

Every tag definition has a stable opaque identity and an immutable, tenant-unique canonical key.
Display name, description, and color are mutable presentation metadata. Canonical keys are
lowercase portable identifiers; the `elsa.` prefix is reserved for host-provisioned system
definitions. A semantic key change creates a new definition and uses an explicit migration rather
than masquerading as a rename.

A definition declares one value mode:

- `Marker` carries no value.
- `Controlled` references a controlled tag value by stable identity.
- `FreeText` carries display text plus an application-produced normalized comparison key.

Controlled values also have stable opaque identities, immutable canonical keys, and mutable display
metadata. Free-text comparison is exact on its normalized key: surrounding whitespace is removed,
Unicode is normalized, and casing is insignificant, while entered display text is preserved.
Substring and fuzzy value matching are not part of the first version.

Cardinality is independent of value mode and is either `Single` or `Multiple`; marker definitions
are inherently single-valued. Value mode and cardinality become immutable when the first assignment
is created. Definition and controlled-value colors are decorative, with value color taking
presentation precedence over definition color.

Definitions and controlled values have `Active` and `Deprecated` lifecycle states. Deprecated
items remain readable, filterable, auditable, and valid on existing assignments, but cannot be
newly assigned. The first version does not hard-delete catalog identities. Replacement and
retirement are explicit migrations.

## Consequences

Saved views and API clients reference stable identities rather than display strings. Cosmetic
changes do not rewrite assignments. Implementations must persist normalized keys explicitly so
comparison behavior does not vary by database provider.

Users cannot repurpose an established definition by changing its mode, cardinality, or canonical
key. This deliberate friction protects automation and reporting from silent semantic changes.
High-cardinality free-text tags remain available, but they receive bounded suggestions rather than
an unbounded managed value list.
