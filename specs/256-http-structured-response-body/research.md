# Research: WriteHttpResponse structured body

Elsa 3 reference: elsa-core tag `3.5.3`, `src/modules/Elsa.Http/Activities/WriteHttpResponse.cs` and
`ContentWriters/{Json,Text}ContentFactory.cs`.

## R-1 Body type

- **Decision**: `JsonElement?` on `WriteHttpResponse.Body` and `HttpResponseInstruction.Body` (owner decision 1).
- **Rationale**: `PublishEvent.Payload` already uses it. Binding keeps text bodies as text: a Literal is unwrapped to a
  CLR string and becomes a `String` element; it is never parsed as JSON (`RuntimeInputBindingCompiler.ConvertLiteral`). A
  JavaScript result is materialized by `JSON.stringify` and deserialized to the property type, so an object becomes an
  `Object` element and a string becomes a `String` element. Today an object result fails at run time when deserialized to
  `string`. The value-flow lowerer already treats `JsonElement?` as untyped.
- **Alternatives**: a separate structured input next to `Body` (rejected by the owner: two ways to set the body); `object?`
  (no precedent, and the alias `Object` loses the JSON intent).

## R-2 Compatibility of committed instructions and definitions

- **Decision**: no shim (ADR 0080 D4, confirmed by the owner).
- **Rationale**: a committed instruction stores `"body": "text"`; deserializing that into `JsonElement?` gives a `String`
  element, which is delivered exactly as before. Authored definitions keep their Literal/JavaScript bindings. The changed
  contract alters the CLR activity hash, so an existing preview database hits `ActivityVersionHashMismatchException` at
  reconciliation; preview databases are recreated (D4).

## R-3 JSON text

- **Decision**: a non-string element is re-written compactly with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.
- **Rationale**: Elsa 3 wrote `JsonSerializer.Serialize(content)` with default options over an `ExpandoObject`: compact,
  keys as authored (the naming policy does not touch dictionary keys), with non-ASCII and HTML-sensitive characters escaped.
  Owner decision 3 keeps the value and keys and accepts the escaping difference. A compact relaxed re-write gives the
  same text whether the element came straight from Jint or through a persistence round trip; for a JavaScript result it is
  byte-identical to `JSON.stringify`.
- **Alternatives**: `GetRawText()` (depends on how the element was last written, e.g. an indented Object literal);
  default-encoder serialization (byte-matches Elsa 3, but the owner declined it).

## R-4 Content type

- **Decision**: infer `text/plain` for a `String` element and `application/json` for anything else when `ContentType` is
  blank, then use the existing factory match (media type without parameters, case-insensitive).
- **Rationale**: this mirrors `DetermineContentType`. Elsa 3 had no number/boolean special case, so they are JSON as well.
  Elsa 4's factories use `RawStringContent`, which deliberately adds no `charset` parameter; Elsa 3 sent
  `application/json; charset=utf-8`. The media type is the same, and that convention is existing Elsa 4 behaviour, so it is
  not changed here. Elsa 3 matched factories by exact string and fell back to `TextContentFactory` (`ToString()`); the
  converter refuses the cases where that differs (R-5).
- **204**: Elsa 3 set the content type but wrote no body for `NoContent`; Elsa 4 does the same.

## R-5 Converter rule

- **Decision**: a new `Elsa3ValueContract.HttpResponseBody`, evaluated in `CheckContract` with access to the node's
  `contentType` property.
- **Rationale**: the rule needs the sibling value, and the normalizer's context already exposes the activity's properties.
  JTest shapes (throwaway read on 2026-10-20, 71 nodes): 44 object + literal `application/json`, 19 object + unset, 5 object +
  empty JavaScript content type, 1 object + content type from a JavaScript expression (refused), 1 variable (object) +
  `application/json`, 1 without content.
- **Exact match**: Elsa 3 picked the writer with `SupportedContentTypes.Any(c => c == contentType)`, so only exactly
  `application/json` and `text/json` serialized an object; `Application/JSON`, a `charset` parameter or a `+json` type fell
  back to `TextContentFactory` (`ToString()`). All 44 JTest literals are exactly `application/json`.
- **Refused**: object content with any other literal type (Elsa 3 sent `ToString()`, e.g. `System.Dynamic.ExpandoObject`);
  object content with a computed content type (Elsa 3's result depends on the run-time value); binary content (`Byte[]`,
  `Stream`: Elsa 3 sent `application/octet-stream`, and Elsa 4 has no binary body).

## R-6 Blank ContentType expression

- **Decision**: drop it during normalization.
- **Rationale**: Elsa 3 evaluates an empty script to `null`, so the content type was inferred. Elsa 4's JavaScript
  evaluator rejects `undefined`, so carrying the blank script would fault at run time. Dropping it reproduces the Elsa 3
  inference faithfully. This is only done for `WriteHttpResponse.ContentType`.

## R-7 Result type of a lowered expression

- **Decision**: an expression bound to a JSON member (`JsonElement`, `Object`, `Any`) declares the member's catalog type.
- **Rationale**: `RuntimeInputBindingCompiler.CompilePortableExpressionInput` refuses a portable expression whose
  `ResultType.Alias` differs from the input alias (ordinal). Lowering stamped the Elsa 3 declared alias, so after D-1 a
  converted body declared `Object` or `String` against `System.Text.Json.JsonElement?` and could not publish. A JSON member
  accepts any value, so declaring its type loses nothing; for a typed member the Elsa 3 type stays, so publication keeps
  refusing a genuine mismatch rather than failing at run time.
- **Alternatives**: stamping the member type for every input (would move a real type mismatch from publish to run
  time); special-casing `WriteHttpResponse.Body` (the same gap applies to every JSON member, e.g. `PublishEvent.Payload`).
