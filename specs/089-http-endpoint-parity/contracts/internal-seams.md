# Contract: Internal Seams Touched (089)

Changes to module-internal contracts; each is catalogued in the owning module's EXTENSION_POINTS.md in the PR that lands it.

## A — start-input + host wiring

- `IStimulusRouter` (behavioral): start path now forwards `StimulusDispatchRequest.Input` into `WorkflowExecutionStartDispatchRequest.Inputs[WellKnownStimulusInputs.StimulusInput]`. Signature unchanged; non-input stimuli unaffected.
- `HttpEndpointMiddlewareShellFeature : IMiddlewareShellFeature` (new, `Elsa.Activities.Http`): mounts `HttpEndpointMiddleware`; `Order` after authentication contributions.

## B — routing

- `TriggerStimulusDescriptor` +`Metadata: IReadOnlyDictionary<string,string>` (optional; providers may omit). `WorkflowTriggerBindingExtractor` copies it verbatim into `WorkflowTriggerBinding.Metadata`.
- `IActivityTriggerStimulusProvider` (behavioral): a provider MAY return multiple descriptors per node (one per (template, method)); extractor accepts one-or-many.
- `IHttpEndpointRoutesResolver` (`Elsa.Workflows.Runtime.Http`): reimplemented over the binding store; feeds the revived `UpdateRouteTableStartupTask` + binding-change handler that maintain the per-shell `IRouteTable`.

## C — parsing/auth/faults

- New request-body parse entry point in `Elsa.Http` reusing the prioritized `IHttpContentParser` set (response-side contract untouched).
- `IHttpEndpointAuthorizationHandler`, `IHttpEndpointFaultHandler` (existing, unwired): become load-bearing from the middleware; contexts unchanged.

## D — mid-flow resume

- `HttpEndpoint` gains `[ResumeTarget]` following the `Delay` pattern. Preferred: context-side accessor exposing the resume stimulus input on `IActivityExecutionContext` (small runtime-core addition — the one runtime-internals touch; fallback: `JsonElement`-parameter resume method).
- Middleware dispatch mode `StartOnly → StartAndResume` (router contract already supports it; self-resume protection unchanged).

## E — sync responses (spec-069 seam)

- `StimulusDispatchRequest` + start/resume dispatch requests gain an optional non-durable dispatch-options passthrough carrying `WorkflowExecutionCommandDispatchOptions.AmbientServices` to the actor enqueue. INVARIANT (spec-069 FR-001, tested): ambient services never serialize into `WorkflowExecutionCommandEnvelope` or any persisted state.
- `WriteHttpResponse` (behavioral): when ambient services expose `HttpContext`, writes the live response via `IHttpContentFactory`; always records `HttpResponseInstruction`.
