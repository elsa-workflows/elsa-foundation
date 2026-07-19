# Output Binding Coercion Uses Pinned Value Representations

Status: proposed

Product requirements are captured in [Typed Output Binding Coercion PRD](../plans/output-binding-coercion-prd.md).

Elsa output bindings will support general-purpose, target-aware coercion without treating every CLR object as workflow data. Activity result projections declare a `ValueRepresentation` such as `TypedValue`, `StructuredValue`, `TextValue`, `FormattedContent`, `BinaryContent`, `DurableReference`, or `TransientResource`; publication validates and pins the allowed conversion profile, while runtime applies that profile using runtime facts. `Any` remains a canonical durable JSON projection, not a general-purpose CLR object container.

The initial implementation scope is durable coercion with JSON and XML content decoding. `Auto` permits only deterministic, schema-guided conversions such as identity, safe numeric widening, recursive collection conversion, typed-value/JSON projection, and recognized formatted-content decoding. Ambiguous, lossy, ordinary-text, binary, or live-resource conversions require explicit configuration or are rejected. Transient activity-to-activity flow is a separate explicitly modeled path and is not captured into durable workflow variables.

Converters are Elsa-built-in or registered named profiles with stable identifiers and versions. Publication verifies their availability and records them in the executable; runtime does not discover arbitrary converters. The same coercion seam is used for output capture and direct activity-result input materialization.

`Auto` recognizes only producer-declared or runtime-supplied formats; it never sniffs arbitrary bytes or text. A recognized conversion failure is terminal, while unknown ordinary content targeting `Any` remains text. `Any` accepts any canonical JSON shape and `JsonObject` is a distinct object-shaped target. Typed, durable values may project into `Any` as canonical JSON, but live services, streams, connections, and other transient resources cannot enter durable workflow state. Conversion policy is owned by each binding edge and applies uniformly to activity results, workflow requests, variable reads, literals, expression parameters, and output captures. Existing published executables retain their pinned behavior when activity representation contracts evolve.
