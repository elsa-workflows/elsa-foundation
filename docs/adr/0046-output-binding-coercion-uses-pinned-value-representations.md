# Output Binding Coercion Uses Pinned Value Representations

Status: proposed (output-capture slice accepted — see Decision status)

Product requirements are captured in [Typed Output Binding Coercion PRD](../plans/output-binding-coercion-prd.md).

## Decision status

The broader coercion model remains **proposed**. The **output-capture slice** — that author-time activity
output → workflow-variable captures are compiled, engine-owned binding edges resolved through the shared
coercion seam (this ADR's paragraphs 3-4) rather than activity-code writes — is **accepted** for
implementation. This scoped acceptance unblocks extending the existing capture pipeline from reusable-activity
boundaries to ordinary activities; see [Author-time activity output → variable capture](../plans/activity-output-capture-authoring.md).
Acceptance of this slice does not ratify the wider `ValueRepresentation` taxonomy, `Auto` conversion policy,
or profile-registration decisions, which stay proposed pending the specs 060/061 reconciliation called out in
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md)
extends the accepted slice with declaration-relative, producer-visible container-frame targets; it
does not broaden the proposed coercion taxonomy.

Elsa output bindings will support general-purpose, target-aware coercion without treating every CLR object as workflow data. Activity result projections declare a `ValueRepresentation` such as `TypedValue`, `StructuredValue`, `TextValue`, `FormattedContent`, `BinaryContent`, `DurableReference`, or `TransientResource`; publication validates and pins the allowed conversion profile, while runtime applies that profile using runtime facts. `Any` remains a canonical durable JSON projection, not a general-purpose CLR object container.

The initial implementation scope is durable coercion with JSON and XML content decoding. `Auto` permits only deterministic, schema-guided conversions such as identity, safe numeric widening, recursive collection conversion, typed-value/JSON projection, and recognized formatted-content decoding. Ambiguous, lossy, ordinary-text, binary, or live-resource conversions require explicit configuration or are rejected. Transient activity-to-activity flow is a separate explicitly modeled path and is not captured into durable workflow variables.

Converters are Elsa-built-in or registered named profiles with stable identifiers and versions. Publication verifies their availability and records them in the executable; runtime does not discover arbitrary converters. The same coercion seam is used for output capture and direct activity-result input materialization.

`Auto` recognizes only producer-declared or runtime-supplied formats; it never sniffs arbitrary bytes or text. A recognized conversion failure is terminal, while unknown ordinary content targeting `Any` remains text. `Any` accepts any canonical JSON shape and `JsonObject` is a distinct object-shaped target. Typed, durable values may project into `Any` as canonical JSON, but live services, streams, connections, and other transient resources cannot enter durable workflow state. Conversion policy is owned by each binding edge and applies uniformly to activity results, workflow requests, variable reads, literals, expression parameters, and output captures. Existing published executables retain their pinned behavior when activity representation contracts evolve.
