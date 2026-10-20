# Tasks: WriteHttpResponse structured body (Elsa 3 parity)

**Input**: Design documents from `specs/256-http-structured-response-body/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/write-http-response.md, quickstart.md

**Tests**: risk-based xunit tests (framework §2.23; no FluentAssertions). No TDD cadence is declared, so tests sit next to the
implementation they cover.

## Phase 1: Setup

No setup: existing projects only.

## Phase 2: Foundational (blocking)

- [X] T001 Change `Body` to `JsonElement?` on `WriteHttpResponse` and `HttpResponseInstruction` in Essentials/src/Activities/Http/Activities/WriteHttpResponse.cs and Essentials/src/Activities/Http/Models/HttpResponseInstruction.cs (D-1)
- [X] T002 Amend compile-time users of the string body (test fixtures, ResponseReplayHost, integration helpers) so they build, keeping their behaviour, under Essentials/tests/Activities/Http/

## Phase 3: User Story 1 - A workflow returns an object as a JSON response (P1)

**Goal**: an object/array/number/boolean body is sent as compact JSON, with `application/json` inferred when no content type is set.

**Independent Test**: deliver an instruction whose body is `{"a":1,"B":[true]}` with no content type.

- [X] T003 [US1] Implement body text (D-2), content-type inference (D-3) and the no-body rules (D-4) in Essentials/src/Activities/Http/Services/HttpResponseInstructionDelivery.cs
- [X] T004 [P] [US1] Add WriteHttpResponseStructuredBodyTests covering object/array/number/boolean bodies, inferred `application/json`, keys as authored, compact output, non-ASCII kept, explicit JSON type through the factory, an explicit unmatched type, JSON `null`, `{}`, and 204 in Essentials/tests/Activities/Http/Tests/WriteHttpResponseStructuredBodyTests.cs
- [X] T005 [US1] Add one end-to-end test where a JavaScript body returning an object is published, executed through the HTTP endpoint and delivered as JSON in Essentials/tests/Activities/Http/IntegrationTests/
- [X] T006 [US1] Refresh the activity contract surface baseline for `Body` (D-7) in Essentials/tests/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json

## Phase 4: User Story 2 - Text bodies behave exactly as before (P1)

**Goal**: literal and JavaScript string bodies, and committed instructions with a stored string body, deliver as before.

**Independent Test**: the existing string-body tests pass; a stored `"body": "text"` instruction delivers `text`.

- [X] T007 [US2] Amend the existing string-body tests to the `JsonElement` body without changing their expectations in Essentials/tests/Activities/Http/Tests/WriteHttpResponseExecutionTests.cs and WriteHttpResponseLiveWriteTests.cs
- [X] T008 [P] [US2] Add tests for a JSON-looking literal text body (sent verbatim with `text/plain`), a text body with an explicit type, an empty string body, and a committed instruction deserialized from `"body":"text"` in Essentials/tests/Activities/Http/Tests/WriteHttpResponseStructuredBodyTests.cs

## Phase 5: User Story 3 - An Elsa 3 response with an object body converts (P1)

**Goal**: Elsa 3 `Content` is carried when Elsa 4 sends the same response, and refused with guidance otherwise.

**Independent Test**: convert WriteHttpResponse nodes for every row of the data-model converter table.

- [X] T009 [US3] Add `Elsa3ValueContract.HttpResponseBody` and use it for `WriteHttpResponse.Content` in Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3MemberRule.cs and Elsa3BuiltInActivityMap.Entries.cs (D-5)
- [X] T010 [US3] Implement the `HttpResponseBody` check (reading the sibling `contentType`) and drop a blank `ContentType` expression (D-6) in Extensions/Elsa3/src/Mapping/Conversion/Elsa3ActivityNormalizer.cs; declare a JSON member's type on lowered expressions (D-8) in Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs
- [X] T011 [P] [US3] Add Elsa3HttpResponseBodyTests (RealActivityCatalog + ConversionServices): object content with unset, blank literal, blank expression, exactly `application/json` or `text/json` converts and carries the expression with the body type as result type; literal/string content with a non-JSON type converts; object + `text/plain`, `Application/JSON`, a `charset` parameter, a `+json` type, a computed type, and `Byte[]`/`Stream` content refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`; blank expression content type is dropped, in Extensions/Elsa3/tests/Mapping/Tests/Elsa3HttpResponseBodyTests.cs
- [X] T012 [US3] Update the `Body` assertions in Extensions/Elsa3/tests/Mapping/Tests/Elsa3BuiltInConversionTests.cs and pin the rule in Extensions/Elsa3/tests/Mapping/Tests/Elsa3ActivityMapCoverageTests.cs
- [X] T013 [US3] Amend the `WriteHttpResponse.Content` (and `ContentType`) rows in specs/236-elsa3-activity-map/member-map.md and the behaviour notes in Extensions/Elsa3/src/Mapping/README.md

## Phase 6: Polish & cross-cutting

- [X] T014 Run the touched test projects listed in specs/256-http-structured-response-body/quickstart.md
- [X] T015 Throwaway JTest check (not committed): `javascript-array-methods` and `javascript-object-methods` convert end to end and the only remaining `content` refusal is the computed content type; record the result in the PR evidence
- [X] T016 Set this spec's `**Status**:` to `Implemented` with the PR link, before the PR merges (spec-lifecycle transition rule; never a separate status PR) in specs/256-http-structured-response-body/spec.md
- [X] T017 Regenerate the maps with `Elsa.Maps.Generator -- all` after ticking this file; stage only the files whose content really changed (CRLF caveat)

## Dependencies

T001 → T002 → (US1, US2). T003 precedes T004, T005 and T008. US3 (T009–T013) depends only on T001 (the catalog alias) and
can run in parallel with US1/US2. T014–T017 come last.

## Parallel example

After T003: T004 and T008 (same new file, so in sequence) can proceed while T009–T011 run in the converter.

## Implementation strategy

MVP = US1 + US2 (Elsa 4 behaviour; native authors benefit at once). US3 then unlocks the Elsa 3 conversion. All three ship
in one PR (Fixes #3050).
