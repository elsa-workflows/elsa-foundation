# Tasks: HTTP Endpoint Full Parity — Sub-unit B (routing upgrade)

**Input**: Design documents from `/specs/089-http-endpoint-parity/` (plan.md §B milestone row, research.md D2/D3, contracts/internal-seams.md §B, spec.md User Story 2 / FR-005..FR-010)

**Status note**: Sub-unit A landed on main via merged PR #578 (its tasks pass, previously in this file, is complete history — including the post-review revision that made the stimulus payload a first-class channel: `IExecutionExpressionState.StimulusInput`, middleware with segment-bounded base-path matching and `MaxRequestBodyBytes`). This pass covers **sub-unit B only**, branch `089b-routing-upgrade`.

**Tests**: Included (QA-gate requirement; spec US2 defines acceptance).

**Pre-release note**: The stimulus-hash identity changes in this unit (path-only → (template, method)); regenerate every hash expectation in tests, no compatibility shims (repo policy).

## Phase 1: Setup

- [X] T001 Confirm clean baseline on `089b-routing-upgrade` (branched from main incl. merged #578): `dotnet build Elsa.Server.slnx` (use `/usr/local/share/dotnet` SDK 10; default PATH lacks dotnet and `~/.dotnet` is SDK 9) — baseline built clean (0 warnings, 0 errors)

## Phase 2: Foundational (contract changes other stories build on)

- [X] T002 Extend `TriggerStimulusDescriptor` with `Metadata: IReadOnlyDictionary<string, string>` (optional ctor param, empty default, snapshot) in `src/Elsa/Workflows/Runtime/Core/Models/TriggerStimulusDescriptor.cs` — added 4th optional ctor param, ordinal snapshot via `RuntimeModelMetadata.Snapshot`
- [X] T003 Change `IActivityTriggerStimulusProvider.Describe` to return **zero-or-more** descriptors (`IReadOnlyCollection<TriggerStimulusDescriptor>`; empty = not mine) in `src/Elsa/Workflows/Runtime/Core/Contracts/IActivityTriggerStimulusProvider.cs`; update `WorkflowTriggerBindingExtractor` (`src/Elsa/Workflows/Runtime/Services/WorkflowTriggerBindingExtractor.cs`) to emit one `WorkflowTriggerBinding` per descriptor, copying `descriptor.Metadata` into `WorkflowTriggerBinding.Metadata` (today written empty); binding id must stay unique per (node, descriptor) — extend `WorkflowTriggerBinding.BuildId` input with the stimulus hash (or an ordinal) so two methods on one node don't collide — done; interface + XML docs updated (empty = not mine, one node may yield N descriptors), extractor emits N bindings copying metadata verbatim; `BuildId(artifactId, executableNodeId, stimulusHash)` (escaped `:`-joined triple, deterministic); no-provider-recognizes still throws
- [X] T004 [P] Update the non-HTTP providers to the new signature (single-descriptor collections): `src/Elsa/Activities/Scheduling/Activities/TimerTriggerStimulusProvider.cs`, `CronTriggerStimulusProvider.cs`, and any Event provider found by `grep -rl IActivityTriggerStimulusProvider src/`; update their tests — Timer/Cron/Event + Http (minimal wrap only, real T007 rework deferred) all return `[descriptor]`/`[]`; provider tests updated to `Assert.Single`/`Assert.Empty`
- [X] T005 [P] Extractor/indexer tests for multi-descriptor nodes + metadata copy in `tests/Elsa/Workflows/Runtime/Tests/` (extractor emits N bindings for N descriptors; metadata lands verbatim; republish delete-and-resave still fully supersedes) — added `Extract_EmitsOneBindingPerDescriptor_WithDistinctIds_AndCopiesMetadataVerbatim` + `MultiDescriptorProvider`; added `Save_KeepsDistinctBindings_ForMultipleHashesOnOneNode` and reworked upsert test to key on the (artifact,node,hash) id; regenerated Groundwork golden fixture (id value only; shape unchanged)

**Checkpoint**: All existing trigger tests green with the widened contract before HTTP work starts.

## Phase 3: User Story 2 — Method-aware, templated routes (Priority: P2)

**Goal**: An endpoint `orders/{id}` with methods GET,DELETE matches exactly those (template, method) pairs, exposes RouteData, and rejects ambiguous (>1 workflow) matches.

**Independent Test** (spec US2): publish templated endpoint → matching method+path starts with `id` extracted; wrong method 404s; duplicate (template, method) across two workflows → 409, nothing starts; republished path supersedes the old route.

### Implementation

- [ ] T006 [US2] Rework `HttpEndpointStimulus` (`src/Elsa/Activities/Http/Activities/HttpEndpointStimulus.cs`): `NormalizeTemplate(path)` (trim/lowercase — parameter names inside `{}` lowercase too, document that template params are case-insensitive by normalization) and `Hash(template, method)` = SHA-256 over `"{normalizedTemplate}\n{lowercasedMethod}"`; `Describe(path, methods)` returns one descriptor per method carrying metadata keys `http:template` + `http:method` (constants on this class); delete the path-only overloads (no shims)
- [ ] T007 [US2] `HttpEndpointTriggerStimulusProvider` (`src/Elsa/Activities/Http/Activities/HttpEndpointTriggerStimulusProvider.cs`): read literal `Path` (required, as today) plus literal `SupportedMethods` (optional); default when unauthored = `GET` (elsa-core parity — BREAKING for A-era publish-anything-matches behavior, called out in the PR body); emit one descriptor per (template, method). Non-literal `SupportedMethods` throws at publish (same rule as Path)
- [ ] T008 [US2] Per-shell route table fed from the binding store in `src/Elsa/Workflows/Runtime/Http/`: reimplement `HttpEndpointRoutesResolver` over `IWorkflowTriggerBindingStore` (filter `StimulusType == HttpEndpoint`, read `http:template` metadata); add `UpdateRouteTableStartupTask` (revive the commented-out startup-task slot in `WorkflowsRuntimeHttpFeature`) populating `IRouteTable` (from `Elsa.Http` — add the project/feature dependency; `RouteTable` is internal, consume via `IRouteTable` DI and ensure `HttpFeature` (or a narrower registration) is composed by `WorkflowsRuntimeHttpFeature`'s DependsOn)
- [ ] T009 [US2] Route-table freshness on publish: the trigger indexer has no post-index event — add a narrow `IWorkflowTriggerIndexObserver` seam (TryAddEnumerable, called by `WorkflowTriggerIndexer.IndexAsync` after delete-and-resave with the artifact's new bindings) in `Elsa.Workflows.Runtime.Core`, implemented by a route-table refresher in `Elsa.Workflows.Runtime.Http`; catalogue the seam in `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` (same-PR rule)
- [ ] T010 [US2] Middleware template resolution (`src/Elsa/Activities/Http/Middleware/HttpEndpointMiddleware.cs`): after the existing base-path/segment/whitespace/body-cap guards, resolve the concrete endpoint path against `IRouteTable` via `IRouteMatcher` (ASP.NET `TemplateMatcher`; add `Elsa.Http` reference to `Elsa.Activities.Http`, verify the architecture guard accepts the edge); unmatched → 404; matched → hash `(template, request-method-lowercased)`; extracted route values go on the request model (T011). **Ambiguity guard**: before dispatch, `IWorkflowTriggerBindingStore.ListByStimulusAsync` — if bindings span >1 `DefinitionId`, reply `409 Conflict` (problem summary body) and start nothing
- [ ] T011 [US2] Extend `HttpRequestModel` (`src/Elsa/Activities/Http/Models/HttpRequestModel.cs`) with `RouteData: IDictionary<string, string>` (extracted template parameters; empty when none — wire change, pre-release); populate in the middleware; surface as a `RouteData` output on `HttpEndpoint` (`src/Elsa/Activities/Http/Activities/HttpEndpoint.cs`), resolved from the stimulus model with the same validation/fallback discipline as Result (fallback = empty)
- [ ] T012 [P] [US2] Unit tests — hash/identity: trigger-time vs request-time symmetry for `(template, method)`; provider defaults (unauthored → GET), N methods → N descriptors with correct metadata; non-literal SupportedMethods throws (in `tests/Elsa/Activities/Http/Tests/HttpEndpointTriggerStimulusProviderTests.cs` + a new `HttpEndpointStimulusTests.cs`); regenerate all existing hash expectations
- [ ] T013 [P] [US2] Unit tests — middleware: template match with route values, wrong method 404, ambiguity 409 (fake binding store with two definitions), sibling/segment guards still green (in `tests/Elsa/Activities/Http/Tests/HttpEndpointMiddlewareTests.cs`; use the shared `RecordingStimulusRouter`)
- [ ] T014 [P] [US2] Unit tests — route table plumbing: resolver reads templates from binding metadata; startup task populates `IRouteTable`; index-observer refresh on republish removes superseded routes (in `tests/Elsa/Workflows/Runtime/` test projects)
- [ ] T015 [US2] Integration test (extend `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointEndToEndTests.cs` + fixture): publish `orders/{id}` GET,DELETE → `GET /workflows/http/orders/42` = 202 and the run's RouteData output contains `id=42`; `POST` same path = 404; second workflow on same (template, GET) = 409 with neither started; republished path supersedes (old 404s, new 202s)

## Phase 4: Polish & Cross-Cutting

- [ ] T016 [P] Docs: update `contracts/http-endpoint-surface.md` (409 row now live, RouteData output live), `contracts/internal-seams.md` §B (as-built: observer seam name, binding-id scheme), `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` (provider multi-descriptor semantics + observer seam), and the §E2.8 catalog note (SupportedMethods now routing-significant; RouteData output reconciles via the CLR source)
- [ ] T017 QA gate: full `dotnet build` + full `dotnet test Elsa.Server.slnx` (all projects, no subsets) + architecture guard green; regenerate/adjust any remaining hash or descriptor expectations surfaced by the run

## Dependencies

- T002 → T003 → (T004, T005, T006)
- T006 → T007 → (T008, T010, T012)
- T008 → T009 → T014; T010 → T011 → T013, T015
- T016/T017 last; T004/T005, T012/T013/T14 parallel within their windows

## Implementation Strategy

Foundational contract widening first (T002–T005 keep every existing trigger green), then HTTP identity (T006–T007), then the two consumers in parallel (route table T008–T009; middleware T010–T011), tests alongside, one PR through the QA gate.
