# Data Model: WriteHttpResponse structured body

## WriteHttpResponse (activity inputs)

| Input | Type | Change |
|---|---|---|
| `StatusCode` | `int` (default 200) | unchanged |
| `Body` | `JsonElement?` | was `string?`. Any JSON value; a Literal binds as a `String` element |
| `ContentType` | `string?` | unchanged; blank means "infer" |
| `Headers` | `IDictionary<string, string[]>?` | unchanged |

## HttpResponseInstruction (committed completion)

| Field | Path | Type | Change |
|---|---|---|---|
| `StatusCode` | `statusCode` | `int` | unchanged |
| `Headers` | `headers` | `IDictionary<string, string[]>` | unchanged |
| `Body` | `body` | `JsonElement?` | was `string?`; a stored string still reads back as a `String` element |
| `ContentType` | `contentType` | `string?` | unchanged; holds what the author set (not the inferred type) |

## Delivery rules

| Body | ContentType | Status | Response body | Response content type |
|---|---|---|---|---|
| absent / null / JSON `null` / `""` | any | any | none | the explicit type if set, else none |
| any | any | 204 | none | the explicit type if set, else none |
| `String` element | blank | ≠204 | the string | `text/plain` |
| other element | blank | ≠204 | compact JSON | `application/json` |
| `String` element | set | ≠204 | the string, through the matched factory | the factory's type, else as set |
| other element | set | ≠204 | compact JSON, through the matched factory | the factory's type, else as set |

## Converter rule `HttpResponseBody` (Elsa 3 `Content` → Elsa 4 `Body`)

| Content | ContentType | Result |
|---|---|---|
| unbound | any | nothing to carry |
| literal or declared `String` | any | carried |
| declared `Byte[]` / `Stream` | any | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` |
| JSON-like declared type | unbound, blank literal, blank expression, or exactly `application/json` / `text/json` | carried |
| JSON-like declared type | any other literal media type | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` |
| JSON-like declared type | non-blank expression | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` |
| any other declared type (`Int32`, `Boolean`, …) | as JSON-like | as JSON-like (Elsa 3 sent the JSON; Elsa 4 does the same) |

A blank `ContentType` expression is removed from the converted node.
