# Tasks: HTTP Endpoint Full Parity — Sub-unit A (host wiring + start-input delivery)

**Input**: Design documents from `/specs/089-http-endpoint-parity/` (plan.md §"Sub-unit A — detailed design", research.md D1, contracts/internal-seams.md §A, quickstart.md §A)

**Scope note**: This tasks pass covers **sub-unit A only** (spec User Story 1, P1). Sub-units B–E get their own tasks passes when scheduled; do not pre-implement their surface here.

**Tests**: Included — the repo's QA gate requires unit + integration coverage per PR, and spec SC-001/US1 define the acceptance behavior.

## Phase 1: Setup

- [ ] T001 Confirm clean build baseline on branch `089-http-endpoint-parity`: `dotnet build` at repo root; note any pre-existing failures before touching code

## Phase 2: Foundational (blocking prerequisites)

- [ ] T002 Add `WellKnownStimulusInputs` static class with `StimulusInput` const (the workflow-input key for router-forwarded stimulus input) in `src/Elsa/Workflows/Runtime/Core/Models/WellKnownStimulusInputs.cs`, XML-doc'd as the start-path counterpart of `BookmarkResumeDispatchRequest.Input`

**Checkpoint**: Const compiles and is referenceable from both `Elsa.Workflows.Runtime` and `Elsa.Activities.Http`

## Phase 3: User Story 1 — Webhook receives the real request (Priority: P1)

**Goal**: A started workflow observes the live HTTP request (body/headers/query/method), and the endpoint middleware is mounted in the shell pipeline with zero host-code edits.

**Independent Test**: Host-level test (quickstart §A): publish workflow with HttpEndpoint trigger → POST JSON → 202 + execution id → workflow state contains the posted body/headers/query; non-matching path 404s; out-of-base-path requests pass through.

### Implementation

- [ ] T003 [US1] Forward `request.Input` in `StimulusRouter.StartMatchingTriggersAsync` (`src/Elsa/Workflows/Runtime/Services/StimulusRouter.cs`): build `WorkflowExecutionStartDispatchRequest` with `inputs` = `{ [WellKnownStimulusInputs.StimulusInput] = request.Input }` when input is non-null/non-empty; timer/cron and inputless stimuli dispatch exactly as today (no empty-entry noise). Check `WorkflowExecutionStartDispatchRequest`'s ctor input type and adapt (JsonElement vs object map) without changing the durable envelope shape
- [ ] T004 [P] [US1] Extend `tests/Elsa/Workflows/Runtime/Tests/StimulusRouterTests.cs`: start dispatch carries the stimulus input under the well-known key; inputless dispatch carries none; resume-path input forwarding unchanged (regression pin)
- [ ] T005 [US1] Rework `HttpEndpoint.Execute` in `src/Elsa/Activities/Http/Activities/HttpEndpoint.cs`: resolve `HttpRequestModel` from the seeded workflow input (workflow-input projection on `IActivityExecutionContext`, key `WellKnownStimulusInputs.StimulusInput`); fall back to the authored-route model when absent (direct-run path); delete the authored-route stopgap block and rewrite the class remarks ("start-input delivery is pending" paragraph goes away)
- [ ] T006 [P] [US1] Extend HttpEndpoint execution tests in `tests/Elsa/Activities/Http/Tests/` (new `HttpEndpointExecutionTests.cs` alongside the existing `WriteHttpResponseExecutionTests.cs` pattern): Result reflects seeded live request when present; falls back to authored route when absent
- [ ] T007 [US1] Create `HttpEndpointMiddlewareShellFeature` in `src/Elsa/Activities/Http/Middleware/HttpEndpointMiddlewareShellFeature.cs` implementing the CShells middleware seam (`IMiddlewareShellFeature`: `Configure(app) → app.UseMiddleware<HttpEndpointMiddleware>()`); set `Order` so it runs after authentication contributions (inspect the CShells FastEndpoints feature's ordering as reference); register it from `ActivitiesHttpFeature.ConfigureServices` in `src/Elsa/Activities/Http/ActivitiesHttpFeature.cs`
- [ ] T008 [P] [US1] Extend `tests/Elsa/Activities/Http/Tests/ActivitiesHttpFeatureTests.cs`: feature registration includes the middleware shell feature; ordering constant is after-auth
- [ ] T009 [US1] Add host-level integration test project/fixture `tests/Elsa/Activities/Http/IntegrationTests/` (HostBuilder + `UseTestServer`, modeled on `tests/Elsa/Foundation/Identity/Tests/Api/TokenEndpointFixture.cs`; in-memory stores): compose a shell with `ActivitiesHttp`, publish a workflow with an `HttpEndpoint` trigger, POST JSON → assert 202 + started id, workflow durable state contains live body/headers/query/method; unmatched path under base path → 404; request outside base path → passes through to a sentinel terminal middleware

## Phase 4: Polish & Cross-Cutting

- [ ] T010 [P] Update `src/Elsa/Activities/Http` docs (EXTENSION_POINTS/README notes): middleware now mounted via the shell feature; start-input delivery landed. Note the behavioral change on the router in `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` (`IStimulusRouter` start path forwards stimulus input)
- [ ] T011 QA gate: `dotnet build` + full `dotnet test` (all test projects, no subsets) + architecture-guard suite green; fix regressions surfaced by the router change (e.g. scheduling pump tests asserting start-dispatch shape)

## Dependencies

- T002 blocks T003 and T005 (both reference the const)
- T003 blocks T009 (integration test needs live input flowing); T005, T007 block T009 likewise
- T004/T006/T008 are parallel to each other and to the next implementation task ([P] — different files)
- T010 parallel to T011 prep; T011 is last

## Parallel Example

After T002: run T003 and T007 in parallel (different modules); once each lands, its paired test task (T004, T008) runs parallel to the other's implementation. T005+T006 follow T003 (input must flow before the activity can read it in integration, though the unit test T006 can stub the projection and start right after T005).

## Implementation Strategy

MVP = Phase 3 complete (US1 end-to-end green via T009), then Polish. This whole tasks file is one PR (sub-unit A) through the QA gate; B–E follow as separate tasks passes/PRs per plan.md's milestone table.
