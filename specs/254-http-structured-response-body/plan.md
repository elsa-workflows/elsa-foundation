# Implementation Plan: WriteHttpResponse structured body (Elsa 3 parity)

**Branch**: `agent/claude/3050-http-structured-body` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/254-http-structured-response-body/spec.md`

## Summary

`WriteHttpResponse.Body` and `HttpResponseInstruction.Body` change from `string?` to `JsonElement?` (owner decision 1,
`PublishEvent.Payload` precedent). `HttpResponseInstructionDelivery` turns the element into response text (a string element
as its text, anything else as compact JSON), infers the content type when none is set (Elsa 3's
`DetermineContentType`), routes that type through the existing content-factory match, and writes no body on 204. The
Elsa 3 converter replaces the `StringOnly` narrowing of `Content` with a new `HttpResponseBody` contract that also
reads the sibling `ContentType`. The spec 236 member map, the Mapping README, the coverage test and the activity contract
baseline change in the same unit.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: none added (`System.Text.Json`, already used)

**Storage**: the committed activity completion carries the instruction; no schema change. A stored string body still deserializes (R-2)

**Testing**: xunit only (no FluentAssertions). `Essentials/tests/Activities/Http/Tests` (unit), `Essentials/tests/Activities/Http/IntegrationTests` (end to end through publish and the endpoint), `Extensions/Elsa3/tests/Mapping/Tests` with `RealActivityCatalog` and `ConversionServices`, and the activity contract surface snapshot in `Essentials/tests/Activities/Design`

**Target Platform**: `Elsa.Activities.Http` module and the `Elsa3.Mapping` library

**Project Type**: two libraries and their test projects

**Performance Goals**: N/A (a structured body is written once; a string body takes no extra pass)

**Constraints**: faithful or refuse; ADR 0080 pre-GA cleanup (no shim); build and test only the touched projects (shared machine)

**Scale/Scope**: about 60 production lines across 5 files; 2 new test classes, amended existing tests, one baseline refresh

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| ADR 0080 D4 pre-GA cleanup | PASS | Owner confirmed on 2026-10-20: the body contract changes in place, preview databases are recreated, and there is no shim or upgrader |
| §E2.7 Elsa 3 stays behind the import boundary | PASS | The Elsa 3 rule lives in `Extensions/Elsa3/src/Mapping`. The Http module gains Elsa 3-compatible behaviour, with no reference to Elsa 3 |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning | PASS | Versions are computed. This is a public API type change on `WriteHttpResponse.Body` and `HttpResponseInstruction.Body`, accepted before GA (ADR 0080 D4) |
| §2.6.2 replacement contracts | PASS | None added or changed. `IHttpContentFactory` keeps its signature and still receives text |
| §2.17 duplication beats dependency | PASS | The JSON media-type test in the converter is a few lines and is not shared with the Http module |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: an activity input, a member rule with an `Elsa3ValueContract` |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping README and the spec 236 member map. No extension point is added or changed, so `EXTENSION_POINTS.md` stays unchanged; the maps are refreshed only for content that really changed |
| §2.23.1–§2.23.3 tests and visibility | PASS | Risk-based tests per decision branch (SC-004). No new public types |
| §2.23.5 exception boundaries | PASS | Nothing new throws on input |
| Decision record | ACTION | The spec 236 member map row `WriteHttpResponse.Content` is amended under the owner's 2026-10-20 decisions |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Body is `JsonElement?` on the input and on the instruction | `WriteHttpResponse.Body` and `HttpResponseInstruction.Body` become `JsonElement?`. Absent, explicit null and a JSON `null` element are all "no body". `[RefusesSecretBinding(Body)]` stays (R-1) |
| D-2 | Body text | A `String` element gives its string. Any other kind is re-written compactly with the relaxed encoder, so keys, values and non-ASCII text come out as the workflow produced them; this is what Jint's `JSON.stringify` already emits (R-3) |
| D-3 | Content-type inference | When `ContentType` is null or blank: `text/plain` for a `String` element, `application/json` otherwise (Elsa 3 `DetermineContentType`). The inferred or explicit type then takes the existing path: matched content factory, else written raw with that type (R-4) |
| D-4 | No body | A null/absent body, an empty string, and status 204 write no body. An explicit content type is still set, as today |
| D-5 | Converter contract `HttpResponseBody` | Replaces `StringOnly` on `WriteHttpResponse.Content`. It carries when the content is a literal or declared `String`, or when its declared type is not binary and the sibling `ContentType` is unbound, a blank literal, an expression with a blank script, or exactly `application/json` or `text/json` (Elsa 3 matched its JSON writer by exact string). Otherwise `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` with guidance (R-5) |
| D-6 | A blank ContentType expression is dropped | Elsa 3 evaluates an empty script to null, so no content type; Elsa 4's JavaScript evaluator rejects `undefined`. The converter drops the blank expression so Elsa 4 infers the type as well (R-6) |
| D-8 | A lowered expression bound to a JSON member declares the member type | Publication requires a portable expression's result type to equal the input alias verbatim; lowering stamped the Elsa 3 alias (`Object`, `String`), which would refuse every converted JavaScript body once `Body` is `JsonElement?`. A JSON member (`JsonElement`, `Object`, `Any`) accepts any value, so its expressions declare the member type; typed members keep the Elsa 3 type, so publication still refuses a real mismatch (R-7) |
| D-7 | Contract baseline refreshed | `activity-contract-surface.baseline.json` records `System.Text.Json.JsonElement?` for `Body` |

## Project Structure

### Documentation (this feature)

```text
specs/254-http-structured-response-body/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/write-http-response.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Activities/Http/
├── Activities/WriteHttpResponse.cs                  # D-1
├── Models/HttpResponseInstruction.cs                # D-1
└── Services/HttpResponseInstructionDelivery.cs      # D-2, D-3, D-4
Essentials/tests/Activities/Http/Tests/
├── WriteHttpResponseStructuredBodyTests.cs          # new: D-2..D-4 decision branches
├── WriteHttpResponseLiveWriteTests.cs               # string bodies keep their behaviour
├── WriteHttpResponseExecutionTests.cs               # amended to JsonElement
└── HttpResponseInstructionFixture.cs, HttpEndpointMiddlewareTests.cs (amended)
Essentials/tests/Activities/Http/IntegrationTests/   # one end-to-end object body through publish + endpoint
Essentials/tests/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json   # D-7
Extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3MemberRule.cs                   # D-5 enum value
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs   # D-5 rule
├── Conversion/Elsa3ActivityNormalizer.cs            # D-5, D-6
├── Services/Elsa3ValueFlowLowerer.cs                # D-8
└── README.md
Extensions/Elsa3/tests/Mapping/Tests/
├── Elsa3HttpResponseBodyTests.cs                    # new: D-5/D-6 branches
├── Elsa3BuiltInConversionTests.cs                   # Body assertions
└── Elsa3ActivityMapCoverageTests.cs                 # pins the rule
specs/236-elsa3-activity-map/member-map.md           # WriteHttpResponse.Content row
```

Other compile-time users of `HttpResponseInstruction.Body` (test fixtures such as `ResponseReplayHost`) are amended so that
they compile; their behaviour is unchanged.

**Structure Decision**: existing projects only.

## Complexity Tracking

None.
