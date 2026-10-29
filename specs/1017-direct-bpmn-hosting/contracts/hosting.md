# Supported hosting and operational contracts

Design for review. Public signatures are introduced only for demonstrated host/model/provider/API consumers and tracked in package API files. No stable universal execution API is claimed.

## Shared extension boundary

| Surface | Contract and default |
|---|---|
| Runtime material read/write | Additive versioned native/model material contract over existing artifact authority; preserve native IWorkflowExecutableStore and DTO constructors. Provider/decorator capability refuses model material unless the complete store/closure/retention path is qualified. |
| Model execution read | Provider-neutral scoped read/page queries for committed header and opaque payload/receipts. No independently writable state-store path; writes enter checkpoint participation. |
| Checkpoint participant | Optional versioned model state/input receipt/precondition data, same authoritative transaction as fence, consumed queue claim, outbox and marker. Absent data retains exact legacy fingerprints. Unsupported providers refuse before mutation. |
| Scoped model selection | Resolve from verified material or committed execution protocol. Native default delegates existing scheduler behavior. Unknown protocols/kinds fail closed. Selection precedes native start/root scheduling. |
| Lifecycle read/cleanup | One authoritative scoped native/model reader. Native terminal stop/purge remains unchanged. Model terminal state closes progression while exact owed settlement/replay inputs remain recoverable; resumption cannot delete those as native residual work. Unknown/unavailable inputs are retained, not silently purged. |
| Registration | Named Sequential initialization event, one contributor aggregator and immutable registry; unique protocol/version with startup cardinality validation. No kernel BPMN switches or generic anonymous handler bus. |
| Native work bridge | Exact committed model-work source/claim/pin/attempt validates before and atomically at native child admission. Native terminal checkpoint retains typed result delivery; cancellation-before-start and result consumption have durable proof. |

Shared public types carry no Bpmn.Model/Semantics/EF dependencies. The model owns payload interpretation and binding semantics. Implementations are internal/scoped except the existing host-lifetime mailbox bridge and immutable registry. Replacement contracts declare replacement kind; contributor catalogs name publication site, aggregate handler and implementations.

## BPMN product commands and queries

The concrete API package follows existing runtime HTTP authorization/problem/tenant-scope conventions. The routes below are the plan's design target. No caller may supply raw paths, credentials, native bookmark IDs or authority snapshots as trusted binding data.

| Operation | Required identity/input | Durable result |
|---|---|---|
| Publish immutable model material | Supported process/profile, complete binding inventory, exact native service artifact/input/output pins, authorized source | Content-addressed immutable model material and source reference; unsupported cuts refused before publication as runnable material. |
| Start | Live authorized material reference, request ID, canonical inputs | One scoped model execution; exact replay returns same identity; conflict refuses. Retained dependency starts require their committed source proof, not a legacy provenance bypass. |
| Inspect execution | Authorized scope and execution | Protocol/material pin, revision/status, typed terminal result, live work, pending delivery and version/profile limits; stable paging where collections are exposed. |
| Inspect human work | Authorized execution and exact work identity | Element/token/iteration/invocation/attempt and current human-wait state; no implied claim/assignment product. |
| Complete human work | Request ID, exact current invocation/attempt and supported result | Atomic accepted input/state/owed work, equivalent replay or explicit stale/conflicting refusal. |
| Cancel | Request ID and execution or exact supported invocation | Durable cancellation and owed propagation; distinguish acceptance from downstream pipeline settlement. Inspection exposes pending settlement. |
| Inspect incident | Authorized execution and incident | Cause, invocation/attempt and supported retry state, without foreign-scope data. |
| Retry incident | Request ID, exact current incident/invocation/failed attempt; pinned HoldForRetry policy with budget remaining | One new physical attempt for the retained active semantic work and a retained retry result. WaitingIncident is nonterminal; replay cannot create another attempt. Final semantic fault/cancellation refuses retry. |
| Deliver timer/work result | Registered internal producer with physically verified source/claim, exact destination/pin/attempt and typed payload | Retained acceptance/equivalence or proven terminal responsibility. Dispatch/Noop/Duplicate/NotFound alone is not acknowledgement. |

Check authorization before lookup/disclosure of retained outcomes. Command inspection authorizes the actual execution, or an outcome-free pending command identity while admission is owed, before looking up the command. If admission commits between these reads and the lookup returns a retained result, authorize the actual execution before disclosing it. A retained result without its execution remains unavailable. Malformed/unsupported new work is refused before acceptance. Temporary store/material unavailability leaves previously accepted work owed and retryable. Accepted work is not silently repinned to current definitions. Domain errors include execution/material/element/invocation context and preserve infrastructure cause; caller cancellation remains cancellation.

Service bindings pin FinalFault by default or explicit bounded HoldForRetry. HoldForRetry records the native child's actual technical failure while preserving the interpreter's pre-failure active work/state; it does not first terminalize and then revive BPMN. Timer/parent interruption can retire that held work. Budget exhaustion becomes final fault. Require actual direct and RunBpmn retry-after-restart proof, one new attempt, unchanged prior fault evidence and rejection of late old results. A business-error result retains its modeled catch/propagation classification.

### HTTP shape

| Method / route | Request or query | Response |
|---|---|---|
| `POST /bpmn/artifacts` | Supported canonical process/profile and complete binding/input/output contracts; authorized source reference | Immutable artifact ID/hash/incarnation and profile. |
| `POST /bpmn/executions` | Request ID, exact live artifact pin/reference, inputs | Execution ID plus durable command identity/status; original start 201, matching replay 200. |
| `GET /bpmn/executions/{id}` | Authorized execution ID | Committed execution/material/protocol/status/revision and terminal result or pending settlement. |
| `GET /bpmn/executions/{id}/work` | Bounded page size and opaque continuation | Stable ordered work identities, attempts and human/timer/service status. |
| `POST /bpmn/executions/{id}/work/{invocation}/complete` | Request ID, exact attempt ID, supported result | Durable accepted command outcome, or 202 with command identity while durably queued. |
| `POST /bpmn/executions/{id}/cancel` | Request ID, cancellation reason | Accepted cancellation outcome or durable pending command/settlement identity. |
| `GET /bpmn/executions/{id}/incidents` | Bounded page size and opaque continuation | Scoped incident ID/cause/invocation/attempt and retryability. |
| `POST /bpmn/executions/{id}/retry-service` | Request ID, exact incident ID, invocation ID and failed attempt ID | One new attempt and retained retry outcome, or durable pending command identity. |
| `GET /bpmn/executions/{id}/commands/{request}` | Authorized execution/request identity | Durable accepted-input outcome, pending durable queue admission, or absence; never an actor-cache inference. |

Equivalent command replay returns its stored outcome. A 202 proves durable admission only; clients inspect the retained command result for semantic consumption. Conflicting identity/content or obsolete invocation returns a contextual 409 without mutation; malformed input is 400, unsupported new profile/binding 422 and temporary authoritative-store unavailability 503. Use existing unauthenticated/permission/not-found conventions without disclosing foreign-scope existence. Response status alone never retires an internal owed delivery: the producer requires the exact durable receipt/terminal proof defined above.

The current service policy is definition format 3 with explicit `faultPolicy` on its one service binding: `{formatVersion:1,disposition:"FinalFault"|"HoldForRetry",maxAttempts:1..32}`. The budget includes the initial physical attempt; FinalFault requires 1. Formats 1/2 retain their existing wire and format 2 retains default FinalFault. Policy state formats 7/8 retain failed native results and the exact pre-failure interpreter snapshot; formats 9/10 add cancellation settlement. Older drivers refuse these frames. Incident inspection reports Open, Retried, Retired or Final with the original failed worker/attempt, its ordinal and pinned budget. The retry command requires `bpmn:retry-service` and current execution authorization before dispatch or replay. [Actual scoped Identity journeys](../evidence/pipeline/retry-authorization/README.md) qualify permission and tenant boundaries before/after replay and cold restart; ordering regressions separately verify inspection authorizes before lookup. Remaining interruption/race acceptance requires its own evidence.

## Initial direct profile

The profile is independently versioned in immutable model material; never replace an existing admitted profile in place.

| Construct/binding | Initial direct/RunBpmn profile |
|---|---|
| Plain start/end | Supported; one explicit process selection, no fabricated native root. |
| Parallel fork/join | Supported and qualified across partial completion/restart. |
| Exclusive/default and outcome routing | Only explicit supported literal/outcome/default cases; retained executable expression conditions remain refused. |
| Human task/wait | Explicit model binding with supported inspect/complete command and exact attempt; task administration/claim/delegation remains outside this task. |
| Duration timer and interrupting boundary | Explicit retained timer binding; qualify human/worker races and restart. Direct date/cycle/recurring starts are outside the initial profile. Existing native recurring-start support is unchanged. |
| Named signal/message delivery needed by the witness | Exact model wait/recipient identity; first-delivery broadcast snapshot and rearm proof required before enabling. No unqualified widening of existing native cohorts. |
| Service task | Explicit pinned published native workflow work binding; real RunEtl/typed result witness. No dummy ETL native address. |
| Business error / fault / cancellation | Matched/unmatched business-error disposition, technical incident/retry and actual cancellation propagation, with typed result contracts. |
| Other tasks | Require an explicitly supported binding. Importer's native activity mapping is not direct-host readiness. |
| Inclusive/event-based gateways, calls/subprocesses, MI, compensation, transactions and additional event families | Existing library/legacy embedded capabilities remain available within their qualified profiles; new direct profile refuses them unless explicitly added and qualified in this task's matrix. No broad conformance claim. |

Legacy `BpmnProcess` remains `elsa.bpmn.structure` 1.0.0 with its existing `activities`, `childNodeId`, `listenerNodeId`, bindings and v1/v2 saved-state readers. It consumes the shared evaluation component but keeps native work-ledger/parent mappings. New `RunBpmn` references a model artifact and uses the same model lifecycle as direct start; it does not redefine legacy BpmnProcess publication or silently migrate active runs.

## XML/BPMNDI and Studio boundary

Retain the existing interchange contract/degradation reporting: first DI plane's shape bounds/edge waypoints; authored pools/lanes/message flows/diagram; one-process export. Do not claim full DI labels/model fidelity, multi-process collaboration reassembly, vendor/script/document metadata preservation or byte-exact XML. Condition expressions remain retained for export and refused for unsupported execution; scripts need explicit host binding. Date/cycle wait import remains refused. #2736 import ownership is not taken over.

The task proves the existing Studio open/edit/export/publish/run/inspect journey on the changed host and the frozen authoring fingerprint. It does not complete first-class direct-host Studio authoring/operations under #2907. Runtime-only recovery must work without any Design store or Studio layout.

## Qualification and activation

Runtime-only, Design-only and combined deployment shapes remain valid. New model hosting is an explicit feature/profile capability; legacy native placement remains unchanged. Provider qualification covers selected physical stores and decorators, not merely a service name. Actual model hosting/upgrade evidence qualifies the built-in SQLite profile only until separate provider/placement proofs exist. Common state/material EF behavior and migrations receive all affected first-party provider checks, reported separately.

No new admission if installed protocol/material/state readers or required native-worker bindings are missing. Already accepted supported material remains runnable; transient unavailable deployment dependencies retain owed work and signal a deployment defect, not permanent acceptance refusal. Capability-off rollback retains readers/workers and pending results; destructive schema downgrade is not a rollback method.
