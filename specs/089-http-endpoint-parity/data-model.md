# Data Model: HTTP Endpoint Full Parity (089)

No new persisted document kinds. This unit reuses and extends existing shapes.

## HttpRequestModel (wire shape — unchanged in A, reused everywhere)

`src/Elsa/Activities/Http/Models/HttpRequestModel.cs` — the single serialized request shape carried as stimulus input for both start and resume:

| Field | Type | Notes |
|---|---|---|
| Path | string | Normalized endpoint-relative path |
| Method | string | Uppercase HTTP method |
| Headers | Dictionary<string, string[]> | Case-insensitive keys |
| Query | Dictionary<string, string[]> | Case-insensitive keys |
| Body | string? | Raw body; null when empty |

Sub-unit B adds `RouteData` (extracted template parameters) — carried alongside, not persisted separately; C adds parsed-content delivery derived from Body + Content-Type at middleware time.

## Well-known stimulus input key (new, A)

`WellKnownStimulusInputs.StimulusInput` (const, `Elsa.Workflows.Runtime.Core`): the workflow-input key under which the router forwards `StimulusDispatchRequest.Input` on the start path. Resume path continues to deliver input via `BookmarkResumeDispatchRequest.Input` (unchanged).

## TriggerStimulusDescriptor (extended, B)

Existing: `(StimulusType, StimulusHash, CorrelationScope?)`. Add: `Metadata: IReadOnlyDictionary<string, string>` (optional, empty default) so trigger providers can supply routing metadata without new runtime concepts.

## WorkflowTriggerBinding.Metadata (existing field, populated in B)

String map, today always empty. Keys written by the HTTP provider (namespaced to avoid collisions with future providers):

| Key | Example | Hash-relevant |
|---|---|---|
| `http:template` | `orders/{id}` | yes (via hash inputs, not via metadata) |
| `http:method` | `get` | yes (same) |
| `http:authorize` | `true` | no |
| `http:policy` | `orders-admin` | no |
| `http:requestTimeout` | `00:00:30` | no |
| `http:requestSizeLimit` | `1048576` | no |
| `http:responseMode` | `sync` | no |

Identity rule: only (template, method) participate in `StimulusHash`; options ride as metadata (elsa-core `[ExcludeFromHash]` equivalent). Delete-and-resave-per-artifact indexing (existing) keeps republish semantics correct.

## Per-shell route table (in-memory, B)

Reuses `src/Elsa/Http` `RouteTable`/`HttpRouteData`. Content = `http:template` values of all HTTP trigger bindings in the shell (+ mid-flow bookmark templates in D). Rebuilt at shell start (startup task) and on binding change; never persisted.

## BookmarkState (as-built, D)

Mid-flow `HttpEndpoint` bookmarks use the existing `BookmarkState` shape with no schema change: `StimulusType = HttpEndpointRouting.StimulusType` (`"HttpEndpoint"`), `StimulusHash = HttpEndpointStimulus.Hash(template, method)`, `ExpiresAt = null`. **One bookmark per supported method** (`bookmarkId = "http-endpoint:{activityExecutionId}:{method}"`), each carrying the SAME `Metadata` payload the trigger provider stamps on bindings — `http:template` + `http:method` + the non-identity endpoint options (`http:authorize`/`http:policy`/`http:requestTimeout`/`http:requestSizeLimit`) via `HttpEndpointStimulusOptions.ToMetadata()`. The metadata is what the route-table resolver reads (template) and the middleware reads for options on a resume-only match (D-D5). Expiry is enforced only in the `IGlobalBookmarkStimulusLookup` layer; the raw `IBookmarkStimulusIndex` type scan is unfiltered.

## HttpResponseInstruction (unchanged, E)

Remains the durable artifact recorded under well-known output `HttpResponse`. Sub-unit E additionally writes the live response from the same instruction when a request-affine `HttpContext` is present; the artifact stays the observable record in all modes.

## State transitions (sync mode, E)

```
request → [auth C] → dispatch(ambient services) → inline drain
   ├─ WriteHttpResponse executed → live response written → drain completes → exchange ends with workflow-authored response
   ├─ durable boundary reached first → drain completes (suspended) → 202 + execution id
   ├─ timeout (linked CTS) → 408 via fault handler; instance continues per runtime semantics
   └─ fault → 400/500 via fault handler
```
