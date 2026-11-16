# Standalone HTTP trigger integration

`StandaloneHttpTriggerAuthoringFeature` installs metadata and configuration validation independently of runtime listeners. `ActivitiesHttpFeature` installs the producer-bound source context and HTTP middleware before embedded endpoint routing. The source uses the ordinary Runtime ingress port; it owns no delivery queue or recovery pump.

The [transport contract](../../../../../specs/1217-standalone-triggers/contracts/transports.md) records response policies, source identity, lifecycle and recovery requirements. This source integration is an implementation candidate; management API/operator examples, composed-host, provider and Studio qualification remain open.

## Composition and configuration

Select `StandaloneHttpTriggerAuthoring` for HTTP metadata/validation and the exclusive reservation planner. Select `ActivitiesHttp` for actual request intake and its existing Runtime HTTP dependencies. Management routes come from the separate `StandaloneTriggersApi` feature. Actual activation requires the selected protected resource/ingress provider family, an enabled eligible subscription and a resource invocation grant; installing metadata or middleware alone is insufficient.

The configuration fields are `method`, `path` and `sourceAccess` (`Authenticated` or `Anonymous`). In management requests, put `http` beside `configuration`: `{ "mode": "WorkflowResponse" }`, or `{ "mode": "AsyncFanOut", "admissionPolicy": "RequireAll" }`. Async also accepts `AcceptAvailable`; omitted async policy defaults to `RequireAll`. These settings are persisted together in the resource configuration by the API.

Routes are relative to `HttpEndpointOptions.BasePath` (default `/workflows/http`), under the active shell mount. The request uses the actual protected reservation generation. An activated resource retains its exclusive route across pause, and embedded publication uses the same physical namespace. Normalized source identity and source authorization are separate from the saved invocation grant. Optional `Idempotency-Key` preserves one accepted source identity; missing keys create a fresh occurrence.

## Cross-domain contributions

- `HttpTriggerTypeSource` contributes metadata to Runtime's `StandaloneTriggerTypesInitializing` through `IStandaloneTriggerTypeSource`.
- `HttpTriggerConfigurationValidator` contributes to the single `StandaloneTriggerConfigurationValidating` aggregator through `ITriggerConfigurationValidator`.
- `HttpTriggerReservationPlanner` prepares `ITriggerReservationPlanner` claims. The Runtime provider protects the reservation/resource transition and exclusive publication conflicts.
- `HttpTriggerSourceContext` supplies `IStandaloneTriggerSourceContext<StandaloneHttpTriggerSource>` from the actual persisted reservation and normalized request identity. The detached signal cannot assert tenant, grants or target pins.

HTTP transport observations and committed response instructions remain separate from source acceptance and execution outcome. The detailed contract and qualification matrix are in [spec 1217](../../../../../specs/1217-standalone-triggers/contracts/transports.md); these instructions do not claim composed-host acceptance.

`WorkflowResponse` requires exactly one enabled root and a target that advertises response support; direct BPMN does not. The middleware observes a committed `HttpResponseInstruction` through fresh ordinary operation scopes, including when another worker executes the root. No committed response before terminal execution returns `502`; observation timeout returns `504`. Accepted work continues after timeout/disconnect. Async returns `202` only after a nonempty accepted cohort, and never waits for workflow completion. `HttpEndpointOptions` supplies the 1 MiB body limit and 30-second response-observation default.

The accepted source freezes ordinary same-route waits before starting any subscribed root. A `RequireAll` refusal or zero accepted subscriptions accepts no waits and causes no resume effects. Saved waits retain their existing execution IDs and exact physical bookmark generations, use the ordinary durable resume machinery, and receive no original request services or response sink. Only the configured subscribed root owns a `WorkflowResponse` exchange. Raw replay identity excludes selected route values; those remain captured in the detached legacy request input.
