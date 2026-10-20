# Feature Specification: WriteHttpResponse structured body (Elsa 3 parity)

**Feature Branch**: `agent/claude/3050-http-structured-body`

**Created**: 2026-10-20

**Status**: In progress

**Issue**: [#3050](https://github.com/elsa-workflows/elsa-foundation/issues/3050) (epic [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052))

**Input**: The largest class-c row of the 2026-10-19 JTest conversion measurement (comment on #2707): 70 WriteHttpResponse
nodes in 23 of the 36 definitions return an object body that Elsa 4 cannot send.

**Depends on**:
- Spec 236 (#2706, merged): the activity map, its member decisions ([member-map.md](../236-elsa3-activity-map/member-map.md)) and the coverage test.
- Spec 247 (#3028, merged) and spec 248 (#3054, merged): JavaScript host functions and the faithful converter fixes.
  Without them, the expected JTest definitions are still blocked by other diagnostics.

**Principle**: faithful or refuse. This is an Elsa 4 behaviour change in the Http module, followed by a converter update.
It is not a converter workaround: wrapping the value in `JSON.stringify` cannot reproduce Elsa 3's content-type inference.

**Owner decisions (2026-10-20, @j03y-nxxbz)**:
1. **Body contract**: the response body becomes a structured (JSON) value, on the activity input and on the committed
   response instruction alike. ADR 0080 pre-GA cleanup applies: no compatibility shim, preview databases are recreated and
   the activity contract baseline is updated.
2. **Content-type inference**: when no content type is set, match Elsa 3: `text/plain` for a text body,
   `application/json` for any other JSON value.
3. **Serialization**: a structured body is written as the JSON text the workflow produced: compact, keys as authored, no
   renaming. It is JSON-equivalent to Elsa 3; only the escaping of non-ASCII and HTML-sensitive characters may differ.
4. **Converter edge cases**: refuse with guidance where Elsa 4 would send a different response (see User Story 3).

## User Scenarios & Testing *(mandatory)*

The actors are an Elsa 4 workflow author who returns an HTTP response from a workflow, and an operator who converts Elsa 3
workflow definitions (JSON) to Elsa 4 through the collection import or, later, engine-start reconciliation (spec 235).

### User Story 1 - A workflow returns an object as a JSON response (Priority: P1)

An author binds the WriteHttpResponse body to an expression that returns an object (or an array, number or boolean). The
caller receives that value as JSON. When the author sets no content type, the response is labelled `application/json`.

**Why this priority**: Today an object body faults at run time, because the body only accepts text. 69 of the 71 JTest
response nodes return an object.

**Independent Test**: Run a workflow whose response body expression returns `{ "a": 1, "B": [true] }` with no content type,
and check that the response is `{"a":1,"B":[true]}` with content type `application/json`.

**Acceptance Scenarios**:

1. **Given** a body that evaluates to an object and no content type, **When** the response is delivered, **Then** the body is the object's compact JSON with its keys exactly as authored, and the content type is `application/json`.
2. **Given** a body that evaluates to an array, a number or a boolean and no content type, **When** the response is delivered, **Then** the body is its JSON text and the content type is `application/json`.
3. **Given** a body that evaluates to an object and the content type `application/json` (or `text/json`), **When** the response is delivered, **Then** the body is the same JSON text and the content type is the one the author set.
4. **Given** a body that evaluates to an object and an explicit content type that some registered content writer handles (for example `application/xml`), **When** the response is delivered, **Then** that writer receives the JSON text, as it receives text today.
5. **Given** a structured body, **When** the run is inspected, **Then** the committed response instruction keeps the body as structured JSON, not as an escaped string.

---

### User Story 2 - Text bodies behave exactly as before (Priority: P1)

An author who already returns text (a literal, a JavaScript string, a variable holding text) sees no change: the text is
sent unchanged, and `text/plain` is still the default content type.

**Why this priority**: Existing native Elsa 4 workflows, the Studio HTTP starter, the e2e fixtures and committed response
instructions all carry text bodies. They must keep working without being edited.

**Independent Test**: Run the existing WriteHttpResponse tests with text bodies unchanged in intent; deliver a committed
instruction whose stored body is a JSON string.

**Acceptance Scenarios**:

1. **Given** a literal body `hello` and no content type, **When** the response is delivered, **Then** the body is `hello` and the content type is `text/plain`.
2. **Given** a literal body whose text happens to be JSON (`{"message":"Hello"}`), **When** the response is delivered, **Then** the body is that exact text (not re-encoded, not quoted) and the content type follows the author's setting, or `text/plain` when unset.
3. **Given** a text body and an explicit content type, **When** the response is delivered, **Then** the text is sent unchanged with that content type, through the matching content writer when there is one.
4. **Given** no body (absent or null) or an empty text body, **When** the response is delivered, **Then** no body is written, as today.
5. **Given** status code 204, **When** the response is delivered, **Then** no body is written, whatever the body holds.

---

### User Story 3 - An Elsa 3 response with an object body converts (Priority: P1)

An operator converts an Elsa 3 definition whose WriteHttpResponse `Content` returns an object. When Elsa 4 sends the same
body and content type, the node converts without a diagnostic and the content is carried as it was authored. When Elsa 4
would send something different, the converter refuses with a stable diagnostic and guidance.

**Why this priority**: 70 nodes in 23 of the 36 JTest definitions; the largest single conversion blocker.

**Independent Test**: Convert Elsa 3 WriteHttpResponse nodes covering each content-type case through the real activity
catalog and check the diagnostics and the carried body.

**Acceptance Scenarios**:

1. **Given** Elsa 3 content declared as an object (expression or variable) and no content type, a blank literal content type, or a content type expression with an empty script, **When** it is converted, **Then** no diagnostic is reported and the Elsa 4 body carries the same expression.
2. **Given** Elsa 3 content declared as an object and the literal content type exactly `application/json` or `text/json`, **When** it is converted, **Then** no diagnostic is reported and the body is carried.
3. **Given** Elsa 3 content that is a literal or is declared as a string, **When** it is converted, **Then** it is carried whatever the content type, as before.
4. **Given** Elsa 3 content declared as an object and any other literal content type (for example `text/plain`, `Application/JSON`, `application/json; charset=utf-8` or `application/problem+json`), **When** it is converted, **Then** it is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, and the guidance says to return a string or set the content type to exactly `application/json`. (Elsa 3 matched its JSON writer by exact media type and otherwise sent the object's type name as text.)
5. **Given** Elsa 3 content declared as an object and a content type computed by a non-empty expression, **When** it is converted, **Then** it is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, and the guidance says to set a literal JSON content type. (Elsa 3's result depends on the run-time value.)
6. **Given** Elsa 3 content declared as another non-JSON type (for example a byte array or a stream), **When** it is converted, **Then** it is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. (Elsa 3 sent it as `application/octet-stream`; Elsa 4 has no binary response body.)

---

### Edge Cases

- Elsa 4 delivery matches content writers ignoring media-type parameters and letter case, as it already does. Elsa 3 matched them exactly, so the converter carries an object body only for the exact JSON types (User Story 3).
- A converted JavaScript body declares the Elsa 4 body type as its result type, because publication requires the expression's result type to equal the input type verbatim.
- A body expression that returns a JavaScript string whose text is JSON stays text: `text/plain` when unset, and it is never quoted again.
- A JSON `null` body is "no body", not the text `null`.
- An empty object `{}` or empty array `[]` body is a real body and is sent as `{}` or `[]` with `application/json`.
- An Elsa 3 converted definition whose content is a literal (always text in Elsa 3) is unaffected by the content type.
- Elsa 3 escaped non-ASCII and HTML-sensitive characters in JSON (`é` became `é`); Elsa 4 sends them as authored. The JSON value is the same.
- The Elsa 3 status-code expression refusal (a JavaScript status code returning an enum name) is unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The WriteHttpResponse body MUST accept any JSON value: text, object, array, number, boolean or null.
- **FR-002**: The committed response instruction MUST keep the body as the same JSON value, so an inspected run shows structure rather than an escaped string.
- **FR-003**: A text body MUST be sent as its text, unchanged, for every content type.
- **FR-004**: A non-text body MUST be sent as its compact JSON text, with keys and values as the workflow produced them; no naming policy is applied.
- **FR-005**: When the content type is unset or blank, delivery MUST use `text/plain` for a text body and `application/json` for any other non-null body.
- **FR-006**: When the content type is set and a registered content writer handles its media type, delivery MUST pass that writer the text from FR-003 or FR-004.
- **FR-007**: A null or absent body, an empty text body, and any body on a 204 response MUST write no body.
- **FR-008**: Existing text bodies (literals, text expressions, variables holding text) and committed instructions whose stored body is text MUST deliver the same response as before this change.
- **FR-009**: The Elsa 3 converter MUST carry WriteHttpResponse `Content` to the Elsa 4 body without a diagnostic when the content is a literal or declared as a string, or when the content type is unset, blank (literal or empty expression) or exactly `application/json` or `text/json`; a converted body expression MUST declare the Elsa 4 body type so the definition publishes.
- **FR-010**: The converter MUST refuse with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` and guidance when non-text content meets a literal non-JSON content type or a content type computed by a non-empty expression, and when the content is declared as a binary type.
- **FR-011**: The spec 236 member map row `WriteHttpResponse.Content`, the converter README and the map coverage test MUST describe the new rule in the same change.
- **FR-012**: The activity contract baseline MUST record the body's new type; ADR 0080 pre-GA cleanup applies, so no compatibility shim or data migration is added.

### Key Entities

- **Response body**: the JSON value the author binds; text or structured.
- **Response instruction**: the committed status code, headers, body and content type that the HTTP transport delivers.
- **Content-type rule (converter)**: the conditions under which an Elsa 3 object body sends the same response in Elsa 4.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Elsa 3 WriteHttpResponse whose content is an object converts without a diagnostic, and the Elsa 4 response carries the same JSON value and content type that Elsa 3 sent.
- **SC-002**: On the Elsa 3 JTest suite (throwaway check, never committed), the 2 definitions `javascript-array-methods` and `javascript-object-methods` newly convert end to end, and response-body refusals remain only for the cases in User Story 3 scenarios 4-6.
- **SC-003**: 100% of the existing text-body WriteHttpResponse scenarios deliver the same body and content type as before.
- **SC-004**: Every decision branch of FR-005, FR-007, FR-009 and FR-010 is covered by a test.

## Assumptions

- Elsa 3 reference: elsa-core 3.5.3 `WriteHttpResponse.WriteResponseAsync` / `DetermineContentType` and `JsonContentFactory` (default serializer options; a string is written as is).
- A literal body binds as text, never parsed as JSON; this is current Elsa 4 literal binding and is what keeps existing bodies compatible.
- Preview databases are recreated (ADR 0080 D4), so a changed activity contract needs no version bump or upgrader.
- Out of scope: binary response bodies, a content type with no body (Elsa 4 keeps setting it), and other JTest blockers (JavaScript limits #3026/#3012, class-b refusals, #1152 rows).
