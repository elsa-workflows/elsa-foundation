# Research & Design Decisions: HTTP Endpoint Full Parity (089)

All decisions were resolved during the pre-spec design phase (approved plan: `~/.claude/plans/agile-swimming-matsumoto.md`, exploration of elsa-core `src/modules/Elsa.Http` + this repo's runtime spine). The one deferred verification (D4a) was completed during this planning pass. No NEEDS CLARIFICATION markers remain.

## D1 — Start-input threading carrier

- **Decision**: Forward `StimulusDispatchRequest.Input` from `StimulusRouter.StartMatchingTriggersAsync` into `WorkflowExecutionStartDispatchRequest.Inputs` under a well-known key; `HttpEndpoint` reads it from the workflow-input projection, falling back to the authored-route model when absent (direct-run path).
- **Rationale**: The seed-input channel is verified end-to-end (`Inputs` → `WorkflowExecutionStartCommandPayload.Inputs` → `WorkflowStartSchedulerWorkHandler` → `seedInputs` → durable state → `WorkflowInputs` projection). The gap is literally the omitted argument at the router's start call; the resume path already threads `request.Input`.
- **Alternatives considered**: A dedicated "trigger input" runtime concept — rejected as new plumbing duplicating an existing durable channel.

## D2 — Route templates + methods: binding metadata vs parallel catalog store

- **Decision**: Carry route template, method, and non-identity options in the existing `WorkflowTriggerBinding.Metadata` (string map, currently written empty by `WorkflowTriggerBindingExtractor`); extend `TriggerStimulusDescriptor` so providers can supply metadata. Per-shell route table is in-memory, rebuilt from the binding store at startup and on binding change.
- **Rationale**: No new persisted doc kind (no §E6 manifest/golden-fixture work); mirrors elsa-core's "in-memory route table rebuilt from trigger store"; delete-and-resave-per-artifact indexing already gives correct republish semantics.
- **Alternatives considered**: Parallel durable route-catalog store — rejected: duplicate lifecycle, no durability benefit, new doc kind + fixtures for data derivable from bindings.

## D3 — Stimulus-hash contents vs route-table normalization split

- **Decision**: Hash = SHA-256 over (normalized route **template**, lowercased method); one trigger binding per (template, method) pair. The route table owns concrete-path→template resolution (ASP.NET `TemplateMatcher` via existing `IRouteMatcher`) *before* hashing; publish-time and request-time hashing stay symmetric.
- **Rationale**: Identical identity model to elsa-core's `HttpEndpointBookmarkPayload` (path+method hashed, options excluded); keeps stimulus routing opaque to HTTP specifics.
- **Alternatives considered**: Hash on concrete path (status quo) — cannot support templates; encode methods as one binding with a method list — breaks per-method lookup symmetry and elsa-core parity.

## D4 — Synchronous response mechanism

- **Decision**: Sync mode dispatches with `WorkflowExecutionCommandDispatchOptions.AmbientServices` = the request-scoped provider (spec-069 request-affine seam). The inline drain runs the workflow on the caller's async flow; `WriteHttpResponse` writes the live response (status/headers/body via `IHttpContentFactory` set) when ambient services expose an `HttpContext`, and always records the durable `HttpResponseInstruction`. Per-endpoint `ResponseMode Sync|Async`; degrade to 202 on suspend-before-response or non-local execution. Ambient services never enter durable envelopes (spec-069 FR-001; invariant test in sub-unit E).
- **Rationale**: Reuses the seam spec 069 built expressly for "Elsa 4's required synchronous HTTP execution capability" while preserving single-writer mailbox semantics (its FR-007); elsa-core-equivalent semantics with zero new correlator machinery. User-selected over the alternative below.
- **Alternatives considered**: Keyed waiter registry (TCS per WorkflowExecutionId) signalled via post-commit intent + durable-output poll fallback — workable and multi-node-extensible, but more machinery, a response-copy hop, and signal/registration races; superseded once the request-affine seam was confirmed implemented.

### D4a — Verification (completed this pass)

`InProcessWorkflowExecutionActorProvider.EnqueueAsync` acquires the mailbox, calls `_commandProcessor.ProcessAsync(envelope, options, ct)`; `WorkflowSchedulerCommandRouter.ProcessAsync` enqueues the scheduler work item, attaches `options.AmbientServices` to the drain request, and **awaits `_drainCoordinator.DrainAsync` inline** — the dispatch returns only after the drain completes. Sub-unit E must still assert drain-to-quiescence for the execution (drain policy contract) in its integration test.

## D5 — Mid-workflow bookmark + resume-callback shape

- **Superseded by spec 095**: The earlier context-mutating bookmark and `[ResumeTarget]` proposal is not the as-built model. Under the [value-flow redesign](../095-value-flow-redesign/spec.md), `HttpEndpoint` derives from `StatefulTriggerActivity<HttpEndpointResult, HttpEndpointState, HttpRequestModel>`: its first invocation returns a typed suspension containing immutable persisted state and typed trigger registrations, and resume receives an `ActivityResumeContext<HttpEndpointState, HttpRequestModel>` without mutating an activity execution context.
- **As built**: Resume projects the HTTP request into one typed `HttpEndpointResult` and completes atomically. `Request`, `RouteData`, and `ParsedContent` are read-only projections of that result; they are not independently published or captured outputs.

## D6 — Request-body parsing reuse

- **Decision**: Add a thin request-body entry point over the existing prioritized `IHttpContentParser` strategy set in `src/Elsa/Http` rather than new parser implementations.
- **Rationale**: The five parsers (json/xml/text/html/file) are `HttpResponseMessage`-shaped (SendHttpRequest response path); the selection strategy and priorities are what we want to share. Adapter must not regress the response-side path.
- **Alternatives considered**: Duplicate inbound parsers in `Elsa.Activities.Http` — rejected (DRY, drift risk).

## Explicit descopes (recorded in spec Assumptions)

Multipart upload validation; quiescence 503 gate; correlation-id/instance-id selectors; distributed sync transport (202 degrade instead); missing-HTTP-context on `WriteHttpResponse` records artifact without faulting (deliberately softer than elsa-core).
