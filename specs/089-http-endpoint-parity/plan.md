# Implementation Plan: HTTP Endpoint Full Parity

**Branch**: `089-http-endpoint-parity` | **Date**: 2026-07-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/089-http-endpoint-parity/spec.md`; approved design plan with verified code facts at `~/.claude/plans/agile-swimming-matsumoto.md`.

## Summary

Upgrade the W16 async/202 `HttpEndpoint` baseline to full elsa-core behavioral parity in five sequenced sub-units: (A) mount the middleware through the CShells middleware seam and thread the live request into started instances via the existing seed-input channel; (B) key stimulus identity on (route template, method) and rebuild a per-shell route table from trigger-binding metadata; (C) wire the orphaned `Elsa.Workflows.Runtime.Http` authorization/fault handlers plus content parsing and limits; (D) mid-flow bookmark + StartAndResume; (E) synchronous responses by dispatching with spec-069 request-affine ambient services so `WriteHttpResponse` drives the live response. Sub-unit A is planned in full detail here; B–E at milestone level (each gets its own tasks pass when scheduled).

## Technical Context

**Language/Version**: C# / .NET 10 (repo standard, `Directory.Build.props`)

**Primary Dependencies**: ASP.NET Core (middleware, `TemplateMatcher`), CShells (`IShellFeature`, `IMiddlewareShellFeature`), existing runtime spine (`IStimulusRouter`, `IWorkflowStartDispatcher`, `IBookmarkResumeDispatcher`, actor mailbox), `src/Elsa/Http` lower module (`IRouteTable`, `IRouteMatcher`, `IHttpContentParser`/`IHttpContentFactory`)

**Storage**: No new persisted document kinds. Route data rides the existing `WorkflowTriggerBinding.Metadata` (string map, currently written empty); bookmarks/durable values unchanged.

**Testing**: xUnit; unit tests with `DefaultHttpContext` + fake router (existing `HttpEndpointMiddlewareTests` pattern); integration via `HostBuilder` + `UseTestServer` (existing `TokenEndpointFixture` pattern); QA gate = full test projects + architecture guard.

**Target Platform**: elsa-foundation server hosts composed via CShells shells (per-shell scoped DI, `shells.json`)

**Project Type**: Modular .NET workflow-engine feature spanning `Elsa.Activities.Http`, `Elsa.Workflows.Runtime`(+`.Core`), `Elsa.Workflows.Runtime.Http`, `Elsa.Http`

**Performance Goals**: No regression on the drain hot path; route-table lookup O(#templates) per request (elsa-core parity); sync mode bounded by per-endpoint RequestTimeout — no unbounded request hangs

**Constraints**: Pre-release — no back-compat shims (stimulus-hash change in B regenerates test expectations); ambient services must never enter durable command envelopes (spec-069 FR-001); no new Runtime→Design dependency (§E2.2); single-writer actor semantics preserved (no direct executor)

**Scale/Scope**: 5 sub-units / 5 PRs; ~12 files modified + ~6 created across the four modules; 22 FRs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Assessment |
|---|---|
| §E2.2 no Runtime→Design dependency | PASS — all changes live in `Elsa.Activities.Http`, `Elsa.Workflows.Runtime(.Core/.Http)`, `Elsa.Http`; no Design references introduced. Architecture guard covers regression. |
| §E2.6 executable-always-runs / artifact-only runtime | PASS — route table is rebuilt from the runtime-owned trigger-binding store, never from design-side data; resume targets stay pinned in `WorkflowExecutable`. A route-table miss yields 404, never a broken executable. |
| §E2.8 activity catalog as picker source of truth | ATTEND — new `HttpEndpoint` inputs/outputs (`RouteData`, `ParsedContent`, `Authorize`, `Policy`, `RequestTimeout`, `RequestSizeLimit`, `ResponseMode`) flow to the catalog via the CLR reconciliation source; no picker-side enumeration added. Each sub-unit adding members verifies catalog reconciliation in tests. |
| §E2.9/§E6 serialization rules (ADR 0035/0036) | PASS — binding metadata is a plain string map; `HttpRequestModel` stays the single wire shape (System.Text.Json, no polymorphic converters, no AQN). |
| Spec-069 FR-001 (durable envelopes free of live services) | PASS by design — ambient services travel only through the non-durable dispatch-options path; re-asserted as spec FR-021 with an invariant test in sub-unit E. |
| Framework §2.16.1 feature registration (MD-5) | PASS — new registrations ride existing `IShellFeature`s (`ActivitiesHttp`, `WorkflowsRuntimeHttp`) with manifest hints; no ad-hoc `IServiceCollection` extensions. |
| EXTENSION_POINTS.md catalogs | ATTEND — any new seam (`IMiddlewareShellFeature` usage, request-body parser entry point, dispatch-options passthrough) is catalogued in the owning module's EXTENSION_POINTS.md in the same PR. |

No violations requiring Complexity Tracking. Post-design re-check: unchanged — Phase 1 design introduces no new projects, no new stores, no cross-context dependencies.

## Project Structure

### Documentation (this feature)

```text
specs/089-http-endpoint-parity/
├── plan.md              # This file
├── research.md          # Phase 0 — design decisions consolidated from the approved plan
├── data-model.md        # Phase 1 — wire/state shapes touched
├── quickstart.md        # Phase 1 — end-to-end verification walkthrough
├── contracts/           # Phase 1 — HTTP surface + internal seam contracts
│   ├── http-endpoint-surface.md
│   └── internal-seams.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks — per sub-unit)
```

### Source Code (repository root)

```text
src/Elsa/Activities/Http/                 # activity module (A,B,C,D,E)
├── Activities/{HttpEndpoint,HttpEndpointStimulus,HttpEndpointTriggerStimulusProvider,WriteHttpResponse}.cs
├── Middleware/HttpEndpointMiddleware.cs
├── Middleware/HttpEndpointMiddlewareShellFeature.cs        # NEW (A)
├── Models/{HttpRequestModel,HttpResponseInstruction}.cs
└── ActivitiesHttpFeature.cs

src/Elsa/Workflows/Runtime/               # runtime spine touches
├── Services/StimulusRouter.cs                              # (A) start-input forward
├── Services/WorkflowTriggerBindingExtractor.cs             # (B) metadata copy
└── Core/Models/TriggerStimulusDescriptor.cs                # (B) metadata field

src/Elsa/Workflows/Runtime/Http/          # endpoint policy module (B,C)
├── WorkflowsRuntimeHttpFeature.cs                          # (B) route-table startup task revived
├── Services/{HttpEndpointRoutesResolver,HttpEndpointFaultHandler,*AuthorizationHandler}.cs
└── Tasks/UpdateRouteTableStartupTask.cs                    # NEW (B)

src/Elsa/Http/                            # lower HTTP module (reused: RouteTable, RouteMatcher, parsers)
└── Services/RequestBodyParser adapter                      # NEW (C)

tests/Elsa/Activities/Http/Tests/         # unit tests (all sub-units)
tests/Elsa/Workflows/Runtime/Tests/       # router/extractor tests (A,B)
tests/Elsa/Activities/Http/IntegrationTests/                # NEW host-level tests (A,D,E)
```

**Structure Decision**: All work lands in the four existing modules above plus their test projects; the only new source files are the middleware shell feature (A), the route-table startup task + binding-change handler (B), the request-body parser adapter (C), and integration-test fixtures. No new projects, preserving the §E2.2 layering the architecture guard pins.

## Sub-unit A — detailed design (this unit's implementation scope)

1. **Start-input forward** (`StimulusRouter.StartMatchingTriggersAsync`): construct `WorkflowExecutionStartDispatchRequest` with `inputs` carrying `request.Input` under `WellKnownStimulusInputs.StimulusInput` (new const in `Elsa.Workflows.Runtime.Core`). The resume path already forwards `request.Input` — after this change both paths deliver the same payload shape. Non-HTTP stimuli (timer/cron dispatch with no input) pass `null`/empty inputs exactly as today.
2. **`HttpEndpoint.Execute`**: resolve the seeded input via the workflow-input projection available on `IActivityExecutionContext`; deserialize to `HttpRequestModel`; set `Result`. Delete the authored-route projection block and its "start-input delivery is pending" remarks. When no stimulus input is present (e.g. definition executed directly via the run API), fall back to the authored-route model — preserves the direct-run path.
3. **Middleware mounting** (as-built: `ActivitiesHttpFeature` itself implements `IMiddlewareShellFeature.UseMiddleware(app, env)` → `app.UseMiddleware<HttpEndpointMiddleware>()` — no separate feature class, no extra shells.json opt-in). Ordering: resolved by CShells 0.0.29-preview.145 (cshells PR #124) — `IMiddlewareShellFeature.Order` exists and CShells sorts middleware features by it; sub-unit C sets an explicit order when auth-sensitive middleware joins the shell pipeline. Requests outside `HttpEndpointOptions.BasePath` pass through with segment-bounded matching; empty/root base path disables the middleware; `MaxRequestBodyBytes` (streaming-enforced 413) bounds the now-durable stimulus payload.
4. **Docs**: update `src/Elsa/Activities/Http` EXTENSION_POINTS/README notes; W16 "start-input delivery" follow-up marked landed in the program-goal bucket on PR merge.

## Sub-units B–E — milestone scope (planned per-unit at their tasks pass)

| Unit | Milestone definition of done | Key design refs (research.md) |
|---|---|---|
| B | Stimulus identity = (template, lowercased method); N bindings for N methods; binding metadata carries template/method/options; per-shell route table rebuilt at startup + on binding change; middleware resolves path→template before hashing; ambiguity rejected; `RouteData` output | D2, D3 |
| C | `ParsedContent` via request-body adapter over prioritized parsers; 401 via authorization handler; 408/400/500 via fault handler; streaming size-limit + linked-CTS timeout | D6 |
| D | Mid-flow bookmark (Delay pattern) keyed by same stimulus identity; middleware `StartAndResume`; resume delivers live request through declared outputs | D5 |
| E | `ResponseMode Sync|Async`; sync dispatch carries ambient services (spec-069 seam); `WriteHttpResponse` writes live response when context present, always records artifact; 202 degrade on suspend-first/distributed; envelope-purity invariant test | D4 |

## Complexity Tracking

No constitution violations to justify.
