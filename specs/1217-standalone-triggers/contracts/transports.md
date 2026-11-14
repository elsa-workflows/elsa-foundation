# Standalone source transport contracts

**Status**: Proposed implementation contract. Product choices are recorded on [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582); this file does not claim the adapters exist.

## HTTP ingress

The request adapter resolves one normalized resource-owned endpoint reservation and current route generation. Authorize source access before disclosing route/target conflicts. Reuse normal body-size, endpoint policy and request parsing facilities. Source payloads cannot supply tenant, invocation grant, execution identity or an alternative target.

An optional caller idempotency key is scoped to the source access/resource identity and immutable request content. Without one, a new request gets a new server identity; a network retry without the original key can represent another occurrence. The response states the delivery identity and applicable replay/retention guarantee. Reusing a retained key with different promised source content conflicts. Replay reads the winning receipt before following changed subscriptions or publication slots.

Initial input validation precedes the atomic admission boundary, and the selected resource/route/grant/publication facts are rechecked at commit. Body parsing/authorization failures accept nothing. A timeout with uncertain commit is resolved through exact scoped receipt lookup; do not assert non-acceptance merely because the client did not receive a response.

### Asynchronous fan-out

Required configuration: `responseMode = AsyncFanOut`, with explicit `admissionPolicy = RequireAll | AcceptAvailable`. RequireAll is the default. The promised set is the enabled subscriptions at the accepted association generation. Intentionally paused subscriptions are excluded; enabled-but-incompatible/unavailable subscriptions are evaluated and reported.

| Condition | Response contract |
|---|---|
| All promised recipients durably accepted | 202; outcome Accepted, delivery ID, accepted/refused counts and authorized status location. |
| Some accepted under AcceptAvailable | 202; outcome PartiallyAccepted and explicit accepted/refused per-subscription outcomes. This policy is author-selected and visible; it is not silently substituted for RequireAll. |
| Any refuses under RequireAll | Refusal; no recipient accepted or started. Return stable safe failure codes/findings under source policy. |
| No accepted recipients under either policy | Failure; never a successful empty 202. |
| Invalid source payload/mapping value | 400 validation result (or the existing parser's size/content status where applicable); no accepted work unless AcceptAvailable explicitly accepts another valid recipient and reports the refused one. |
| No serving target, paused ingress, revoked grant or missing durable composition | Visible unavailable/refused outcome; no success acknowledgement. Distinguish operator configuration failure from transient admission/storage failure. |
| Retained idempotency key conflicts | 409; no new effects. |
| Durable store commit fails | Appropriate failed/unavailable response; never convert failure into 202. If commit outcome is uncertain, exact lookup determines the retained result. |

The exact safe error code/status mapping is shared by API and real HTTP tests. Source/public projections expose permitted subscription identifiers and stable codes; internal workflow names, grant subjects and payload details require ordinary management authorization.

202 means the source and accepted recipient responsibility are committed and recoverable through the normal runtime. A later queue-capacity or execution dispatch failure does not retroactively make intake a rejection; that recipient recovers independently. The response does not claim Queued, Started or Completed unless those facts were independently observed.

No live exchange/response sink is handed to async subscribers. A typed response instruction committed by a target is an execution result; it cannot write to this request. Modules cannot acquire the exchange by emitting another response-owner claim. The trigger itself owns acknowledgement delivery.

### Workflow-owned response

Required configuration: `responseMode = WorkflowResponse` and exactly one enabled, eligible response-capable target. The selected target still follows its serving slot at fresh admission. A second target/owner or unsupported model/profile refuses activation; there is no implicit switch to async.

The trigger accepts one exact target and observes its committed typed response instruction through the ordinary runtime stores. Prefer the existing inline drain where supported, then observe the exact retained execution across fresh scopes until response, terminal missing-response outcome, deadline or client disconnect. This must also work when a peer worker runs the target; do not advertise multi-node workflow-response support based only on the local inline path.

The request owns the transport object for its bounded lifetime; neither workflow activity scope nor persisted source contains HttpContext. Reuse the canonical instruction/content writer. The first eligible committed response instruction from this sole owner supplies the response according to its ordinary ordering; other workflow roots cannot compete.

| Outcome | Behavior |
|---|---|
| Response instruction committed before deadline | Deliver it and record the instruction/exchange observation separately from workflow completion. |
| Workflow finishes without a response instruction | 502 with a stable missing-response diagnostic and delivery identity; retain actual workflow outcome. |
| Deadline expires before a response instruction | 504 with a stable timeout outcome where the connection permits; accepted target continues. |
| Client disconnects or response write fails | Record transport failure/unknown delivery separately. Accepted work continues; do not report response success from workflow completion. |
| Repeat with retained idempotency key | Reuse the same accepted target; return the retained instruction to this new exchange when available under the same access/content checks, rather than start another execution. Replaying response bytes is a new transport observation. |

Timeout is explicit configuration with a bounded host default/maximum. Request cancellation only stops reading/waiting/writing for that exchange; it does not erase a committed source or cancel the execution. A dispatch failure before durable acceptance is a refusal, while failure after acceptance remains owed/visible. No missing-response path returns a misleading implicit 202 in workflow-response mode.

## HTTP ownership lifecycle

A paused never-activated resource only proposes a route. First successful activation acquires its common exclusive reservation. Pause retains it and serves visible unavailability. A reviewed route edit conditionally exchanges old/new claims with the resource revision; conflict preserves the previous claim/configuration. Deletion releases the claim while retaining accepted delivery/execution facts. Embedded publication and standalone activation perform conflict checks in both directions at the protected ownership boundary.

Include resource owner/configuration/mode generation in the existing route manifest/fingerprint. Multi-node convergence may not continue serving the old generation after a successful protected transition. Waiting embedded bookmarks retain their existing instance resumption semantics and are not converted into standalone subscribers.

## Event

An Event is a broadcast, not durable-message competing consumption. Match resource source configuration and enabled subscriptions under the original source/tenant. A committed workflow-origin Event extends its original saved cohort with typed standalone subscription recipients; retries reuse it. External Event intake uses a true standalone source in the same delivery composition. Local/resume-only Events remain resume-only.

Capture compatible recipients and explicit refused outcomes independently. One incompatible or temporarily unavailable subscriber does not withhold other eligible Event starts. Recovery does not discover new subscriptions or resume waits created by its own new starts. Preserve the pre-effect wait snapshot and embedded cardinality/start behavior.

## Timer and Cron

One resource owns one interval/Cron cursor and absolute due occurrence, independent of its subscribers' publications. The existing recurring pump claims the resource-owner arm and submits that occurrence to normal source admission. Atomic admission/settlement or deterministic exact lookup ensures a crash after admission cannot produce another recipient set or duplicate start.

The occurrence key includes the stable scoped resource identity, schedule generation and absolute due instant, never the target artifact. The frozen source contains the complete evaluated association set and exact accepted targets/inputs. Failed recipients do not replay successful ones; source handoff/settlement and next-firing advancement remain inspectable separately.

Use existing ISO-8601/invariant interval parsing and five/six-field Cron format. Cron timezone defaults UTC and is explicit in configuration/preview; interval duration is elapsed time and has no local timezone. Use the pinned Cronos 0.13.0 timezone calculation rather than a separate DST implementation. Its gap policy advances a nonexistent scheduled local time to the next valid instant; a fixed daily time does not fire twice in the repeated hour, while interval-based Cron expressions retain their repeated-hour periodic occurrences. Verify these cases using absolute instants and the installed timezone database.

After unplanned downtime, fire the persisted due occurrence once, then advance to the first occurrence strictly beyond the wake instant. Intentional pause resumes at the next future occurrence. A configuration/timezone change fences the old unaccepted generation and selects the new generation's next future occurrence; an accepted old occurrence remains owed. Advancing clocks may cause one due catch-up; moving clocks backward cannot recreate a settled key. Do not turn any of these changes into an unbounded backlog replay.

When a complete evaluation finds zero admissible subscribers, do not accept an empty source or settle/advance the due cursor. Retain one overdue occurrence and a bounded retry time, with WaitingForEligibleSubscribers evidence. After eligibility returns, capture the then-current targets and mappings for that same due occurrence and advance beyond the acceptance wake instant. Unknown/unreadable subscription, publication or persistence state is AdmissionUnavailable rather than known-zero; it also retains the cursor and cannot acknowledge acceptance. Once any recipients have been accepted, that firing has a final complete accepted/refused set: restored subscribers are not retroactively added, and successful recipients are not rerun. Preserve these outcomes through restart and display overdue versus future firing truthfully.

## Qualification

Prove both HTTP modes and both async policies using real requests, including mixed and zero admission, idempotent replay, changed-content conflict, failed/uncertain store commit, deadline/disconnection and peer-worker response. Qualify route conflicts both ways and stale multi-node generations. Deliver real Events alongside embedded starts/waits/local controls. Await actual schedule firings, then test controlled due/update/clock/timezone/lease/restart races against the selected physical provider. A manual fire-now button or browser reload is not schedule/restart evidence.
