# Specialist JSON profile

`SpecialistJsonSchema` registers the internal shared validator used by Design and Runtime. It has no
Design dependency. Its pinned engine is JsonSchema.Net 9.2.0 and its explicit dialect is draft 2020-12.

`elsa.specialist.json/1` accepts `type`, `properties`, `required`, `additionalProperties`, `items`,
`enum`, `const`, numeric bounds/`multipleOf`, string/collection/object length bounds, annotations and
bounded acyclic local JSON-pointer references. Local definitions are validated and expanded before
engine construction. Remote references, custom dialects, unknown keywords, regex, combinators,
dynamic references and validation siblings on references are refused. Annotation defaults do not
apply business defaults.

Limits are 65,536 schema bytes/2,048 nodes, 1,048,576 value bytes/16,384 nodes and depth 32. Duplicate
object members and unbounded/nonfinite numeric lexemes are rejected. Diagnostics do not echo values.
Each preparation uses an isolated engine registry; no process-wide schema cache or fetch hook changes.

The generic activity contract owns required/nullability/type declarations. Constraint keys are opaque
member reference keys; public `Name` values become JSON field names. A constraint may refine a public
type, never widen it. Runtime validation also checks the declared native conversion (for example Guid,
DateTimeOffset and bounded integers); a schema-valid string alone does not establish that conversion.

Expanding the profile or a provider's supported wire subset requires explicit compatibility review
and executable qualification. Current shared validation does not establish live-provider support.
