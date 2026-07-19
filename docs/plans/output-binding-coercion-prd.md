# PRD: Typed Output Binding Coercion

**Status:** Proposed
**Date:** 2026-07-18
**Related decision:** [ADR 0046](../adr/0046-output-binding-coercion-uses-pinned-value-representations.md)
**Related terminology:** [Elsa glossary](../glossary/elsa.md)

## Summary

Elsa activity outputs should bind naturally to workflow variables, consuming activity inputs, and expression parameters even when the producer's representation differs from the consumer's declared type. The system should make common conversions automatic, keep ambiguous or unsafe conversions explicit, and preserve deterministic durable workflow behavior.

The initial implementation targets durable coercion with JSON and XML content decoding. The model is general-purpose: JSON/XML are converter profiles, not the definition of coercion. Other activities may produce typed values, structured data, text, binary content, durable references, or transient resources.

## Problem

Foundation currently models bindings as durable source references and target types, but does not yet provide a general source-to-target coercion mechanism.

- Activity output capture currently requires the output definition type and target workflow-variable type to match exactly in [`RuntimeOutputCaptureCompiler`](../../src/Elsa/Workflows/Publishing/Api/Services/RuntimeOutputCaptureCompiler.cs).
- Direct activity-result and variable bindings retype the value envelope but preserve the underlying payload representation in [`RuntimeActivityInputMaterializer`](../../src/Elsa/Workflows/Runtime/Services/RuntimeActivityInputMaterializer.cs).
- Expression result coercion handles some `JsonElement`-to-target cases, but a formatted JSON string is not automatically parsed before it reaches a target.
- Dynamic `Any` values have an established canonical JSON representation and expression-engine boundary through [ADR 0036](../adr/0036-dynamic-any-expression-values-materialize-as-jsonnode-with-per-engine-adapters.md).

As a result, users may need explicit parsing or conversion logic even when the intended source and destination are obvious. This is especially visible with HTTP, file, message, and serialized database outputs, but the problem is broader than content received over HTTP.

## Product goal

Allow a workflow author to connect a compatible activity output to a typed destination and have Elsa perform the correct deterministic conversion automatically, while exposing explicit control and diagnostics when the conversion is ambiguous or potentially lossy.

## Goals

1. Provide one general coercion model for output captures, direct activity-result bindings, workflow-request bindings, variable reads, literals, and expression parameters.
2. Make common durable conversions frictionless through `Auto` behavior.
3. Preserve raw text and binary values unless their source representation declares formatted content or the user explicitly selects a converter.
4. Make `Any` a canonical durable data projection, not an arbitrary CLR object container.
5. Support JSON and XML as the first formatted-content conversion profiles.
6. Allow modules and hosts to register stable, versioned conversion profiles.
7. Validate converter availability and compatibility at publication, then apply only the pinned plan at runtime.
8. Keep visual, API/JSON, code-first, and representable imported workflows semantically equivalent.
9. Prevent live services and resources from entering durable workflow state.

## Non-goals

- Automatically converting every CLR object to every other CLR type.
- Treating ordinary strings as JSON or XML based on their contents.
- Defining a universal XML-to-dynamic-object mapping.
- Passing application services, database connections, open streams, sockets, or transactions through durable workflow variables.
- Implementing arbitrary runtime converter discovery.
- Making transient in-memory activity-to-activity flow part of the initial JSON/XML implementation slice.
- Replacing the existing canonical `JsonNode`/`JsonElement` model for `Any`.

## Terminology

### Value representation

`ValueRepresentation` describes the form in which a source value exists at a value-flow boundary, independently of which activity produced it.

| Representation | Meaning | Typical examples |
|---|---|---|
| `TypedValue` | Already materialized as its declared durable type | `Customer`, `DateTime`, `Decimal` |
| `StructuredValue` | JSON-like or record-shaped durable data | `JsonNode`, dictionary, array |
| `TextValue` | Ordinary text with no decoding promise | Label, message, plain string |
| `FormattedContent` | Text or bytes whose format is declared or supplied at runtime | JSON, XML, CSV |
| `BinaryContent` | Raw bytes without a decoded semantic format | File bytes, encrypted payload |
| `DurableReference` | A reference to externally stored durable data | Blob/document/payload reference |
| `TransientResource` | An in-memory resource that cannot cross durable boundaries | Service, connection, stream, socket |

Media type is optional metadata of `FormattedContent`. It is not the general coercion abstraction.

### Target types

- `Any`: any canonical JSON shape—object, array, scalar, or null.
- `JsonObject`: a canonical JSON object specifically.
- Typed alias: a registered schema- or type-described durable value such as `Customer`.
- `String`: raw text; conversion to structured data is never implied by CLR type alone.

## User experience

The normal workflow is:

```text
Activity output → workflow variable/input/parameter
                 Conversion: Auto
```

The designer may hide the conversion detail for simple bindings, but inspection must show the selected or possible conversion. Advanced users can choose a binding-level override:

```text
Auto
None
Json
Xml
Profile(id)
```

Conversion policy belongs to the binding edge, not globally to the target variable or activity type. The same target may receive JSON, XML, typed, or identity values from different producers.

## Default behavior

### Representation defaults for activity authors

| Output type | Default representation |
|---|---|
| `string` | `TextValue` |
| `JsonNode` / `Any` | `StructuredValue` |
| `byte[]` | `BinaryContent` |
| `Stream` | `TransientResource` |
| Registered DTO/record | `TypedValue` |
| Durable dictionary/list | `StructuredValue` |

Ambiguous outputs may explicitly override the default. For example, `SendHttpRequest.ResponseBody` remains a `string` but declares `FormattedContent` because it represents externally formatted payload content.

### `Auto` conversion

`Auto` permits only deterministic, unambiguous conversions:

- identity conversions;
- nullable compatibility when the value is present;
- safe numeric widening;
- recursive collection conversion when element conversion is safe;
- typed durable value to `Any` through canonical JSON;
- structured JSON to `Any` or `JsonObject`;
- structured JSON to a compatible typed alias;
- recognized formatted content to a compatible target through a unique profile.

`Auto` does not perform numeric narrowing, arbitrary string parsing, binary-to-text conversion, or lossy/ambiguous transformations.

### Format discovery

Formatted content uses producer-declared or runtime-supplied format information. Elsa does not sniff arbitrary bytes or text under `Auto`.

- A recognized format with invalid content fails the binding.
- Unknown or unspecified ordinary content bound to `Any` remains text.
- Explicit `Json`, `Xml`, or named-profile selection requires the requested conversion and fails if it cannot be applied.
- If more than one profile can satisfy `Auto`, publication reports ambiguity and requires an explicit profile.

## Initial conversion scope

### JSON

JSON formatted content supports:

- `Any`;
- `JsonObject`, with object-shape validation;
- arrays and collections;
- compatible registered typed aliases;
- canonical JSON projection of durable typed values.

### XML

XML formatted content supports:

- registered typed aliases through explicit XML deserialization profiles;
- raw `String` preservation;
- named XML-to-JSON profiles when a workflow explicitly selects a documented mapping convention.

Under `Auto`, XML bound to `Any` remains text because XML has no universal dynamic object representation.

### Other values

Primitive, typed, collection, and structured conversions use conservative built-in rules. Binary values and transient resources require explicit materialization or reference semantics. A stream may be materialized into bytes or an external payload only through a separately modeled policy; a live service or connection is not a durable workflow value.

## Functional requirements

### FR-001 — General binding coercion

The runtime shall support a shared coercion mechanism across activity-result bindings, output captures, workflow-request bindings, variable reads, literals, and expression parameters.

### FR-002 — First-class conversion plan

The canonical binding/output-capture model shall carry a typed conversion plan containing the mode, profile identifier/version where applicable, and options. Conversion behavior shall not be hidden only in free-form metadata.

### FR-003 — Source representation

Activity result projections shall declare their `ValueRepresentation` and supported formatted-content capabilities. Runtime values may refine a declared representation with facts such as actual format.

### FR-004 — Target-aware conversion

Publication shall evaluate source representation, source type, target type, and binding policy together. Exact matches shall use identity conversion; compatible mismatches shall record a conversion plan; unsupported mismatches shall produce a publication diagnostic.

### FR-005 — Publication/runtime split

Publication shall validate converter availability and compatibility and pin the selected conversion plan in the executable. Runtime shall apply only pinned converter profiles and may use runtime value facts within the published capability set.

### FR-006 — Expression parity

Expression parameters shall use the same coercion semantics as activity inputs and workflow-variable captures. A formatted JSON parameter may arrive in Jint as a structured `args` value under `Auto`; raw text remains available through `None`.

### FR-007 — Canonical dynamic values

Values entering `Any` shall be normalized to the canonical JSON representation. `Any` shall not retain arbitrary CLR object identity or assembly-qualified type information.

### FR-008 — Durable boundary

Only persistence-safe values or explicitly modeled durable references may be captured into durable workflow variables. `TransientResource` values shall not cross a durable boundary.

### FR-009 — External payloads

Coercion shall work across `DurableReference` values by resolving through the existing external-payload seam, applying the pinned conversion, and applying the destination storage policy.

### FR-010 — Strict failures

Invalid recognized content, explicit conversion failures, unsupported targets, ambiguous profile selection, and schema-incompatible nested values shall fail with diagnostics. No recognized conversion may silently fall back to the original representation.

### FR-011 — Compatibility

Published executables shall retain their pinned representation and conversion behavior. Representation changes that alter value semantics shall require a new activity contract version.

### FR-012 — Inspection

Binding inspection and diagnostics shall expose source type, source representation, target type, conversion policy, selected/possible profile, profile version, and durable/transient classification.

### FR-013 — Converter extensibility

Elsa, modules, and hosts may register named conversion profiles. Each profile shall declare stable identity, version, supported source representations, supported target aliases, options, persistence safety, and deterministic behavior.

## Safety and quality requirements

- JSON/XML parsing shall be bounded by payload size, depth, and node limits.
- XML parsing shall disable DTDs and external entity resolution.
- Converters shall not access network, filesystem, workflow services, or ambient mutable state.
- Arbitrary reflection and assembly-qualified type activation shall be forbidden.
- Conversion shall happen once at the durable output or pinned-input boundary.
- Retries and resumes shall reuse the converted value rather than reevaluating it.
- Sensitivity, encryption, retention, and redaction policies shall propagate to converted values.
- Conversion and durable publication shall be atomic; failed conversion shall not publish a partial variable.
- The converted target value is authoritative. Raw source retention is an explicit separate binding choice.

## Transient flow boundary

Transient activity-to-activity flow is a separate explicitly modeled capability. A transient value may be passed only when execution-lifetime rules guarantee that it remains in memory and cannot cross a checkpoint, suspension, retry, migration, or worker boundary. It cannot be captured into a durable workflow variable.

Application services should normally be injected independently into each activity rather than returned as workflow data. Streams, connections, and other resources require explicit transient or durable-reference semantics.

## Acceptance baseline

1. JSON content output binds to an `Any` variable and is readable through Jint property access.
2. JSON content binds to `JsonObject`, including object-shape validation.
3. JSON content binds to a compatible registered typed alias.
4. XML content binds to a compatible registered typed alias.
5. XML content bound to `Any` remains text under `Auto`.
6. Ordinary text bound to `Any` remains a string.
7. A typed DTO bound to `Any` becomes canonical JSON.
8. Direct activity-result input and output-variable capture produce equivalent values.
9. Expression parameters receive the same coerced values as activity inputs.
10. `None`, `Json`, `Xml`, and named-profile overrides work as specified.
11. Invalid, ambiguous, unsupported, and lossy conversions produce diagnostics.
12. Live services and streams cannot be captured into durable variables.
13. Existing published executables retain their original behavior after representation/contract changes.
14. Visual, API/JSON, code-first, and representable imported workflows compile to equivalent bindings.

## Open technical design follow-ups

These items do not block the PRD but must be resolved before implementation planning:

- Exact `ValueRepresentation` and conversion-plan types and owning modules.
- How runtime format facts are carried from activity completion into projection envelopes.
- Built-in XML profile implementation and typed-alias serializer policy.
- External-payload conversion and destination-storage interaction details.
- Designer/API shape for showing inferred conversions and selecting profiles.
- Exact transient-flow scheduling and checkpoint constraints.
- Versioning/upcasting requirements for persisted contracts and bindings.
- Performance benchmarks for large payload parsing and recursive structured conversion.
