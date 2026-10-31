# Specialist execution, resources and recovery contract

**Status**: Proposed. Owning requirements FR-003–FR-019/FR-022–FR-025; see [state model](../data-model.md).
No C# signature or unqualified provider behavior is frozen by this document.

## Immutable invocation and authority

The native facade receives the published runtime contract/descriptor and committed input snapshot.
It reads dynamic user member values through Runtime Core's execution/input snapshot/value ports,
not reflected CLR properties generated per specialist. The activation strategy has the correct
hydration policy for that dynamic contract. The worker has a fixed empty business-input contract;
its opaque request reference travels through persisted native scheduling metadata.
No runtime dependency on draft/catalog persistence is introduced.

The facade's custom strategy uses no reflected hydration for dynamic user members; read the committed
Runtime Core input snapshot through normal value/protection ports. Its compiled worker child needs
no dynamic business-input override. `ScheduleChildActivity` can carry an opaque request reference in
native scheduling metadata; the worker resolves persisted child/provenance through Runtime Core and
verifies its exact specialist parent/artifact/journal association. The reference is not authority.
This uses existing scheduling/state ports, not an assumed child-input injection API. An internal worker
placed directly under an unrelated parent must refuse before model dispatch.

Resolve tenant/owner/run-as from trusted host/runtime authority, never JSON business inputs or model
messages. Missing or unsupported authority fails closed. Conversation ownership and model connection
ownership are explicit related checks; Connections v1 supports tenant-owned connections only. A handle,
provider session ID, workflow ID or post-commit delivery context alone is not a grant.

Stable logical operation identity, immutable artifact/contract, canonical input/policy fingerprint,
conversation/base revision and admission generation accompany every request/result/receipt. Reusing an
operation with different arguments conflicts. A matching accepted replay returns its original result
without restoring that operation's older session over later conversation history.

## Model connection and capabilities

Concrete provider leaves contribute model connection type/client/capability sources through specialist
Core and existing Connections contracts. One named registry/aggregation path owns discovery; the runtime
does not depend on a concrete provider feature. Construct clients at point of use with authorized resolved
credentials and host policy, and dispose the operation lease. A selected specialist model is independent
of Weaver's one active assistant harness.

Deliver durable connection documents/auth state and preserve existing TryAdd/version-CAS Save/conditional
Delete/secret-reference semantics. Persisting Secrets alone is insufficient. Validate and record the
provider/model/capability revision/source for structured output, tools/approval, streaming, continuation,
sampling/effort, output/context limits, modalities and usage. Unknown and configured-but-unverified support
are distinct from authoritative/tested support. Validate schema constructs against the provider's actual
supported subset; never silently downgrade a published output mode or discard unsupported settings.

The first named specialist providers are Anthropic and Azure OpenAI via their official client bindings
and MAF, each qualified separately. Exact deployment/model identities come from the authorized connection;
do not hardcode a fashionable model name or claim an entire provider catalog is supported. Connection
tests disclose whether they call a model. No production test consumes paid access without authorization.

## One admitted MAF turn

The facade stages an exact protected request and commits background child responsibility. The worker
leaf resolves the request under trusted scope, admits/replays the journal operation, writes the durable
provider-dispatch boundary, then calls the actual pinned MAF agent/client. System instructions and JSON
business data remain distinct messages; tools/permissions are supplied by host policy, not prompt text.

Validate materialized JSON inputs against the compiled input schema. Validate returned JSON, required
properties, types, constraints and undeclared fields against the exact compiled result schema before
acceptance. Provider format support is not validation. Do not coerce malformed output into success or
ask a second model call to repair it outside the declared budget/policy. Refusal, invalid/truncated JSON
and schema mismatch become normalized specialist faults with safe diagnostics; no invalid output is
committed as the business result.

Record observable result, model/capability/resource identities, permitted messages/calls, timing and
usage. The adapter's versioned session codec must whitelist necessary continuation/session fields;
do not blindly persist raw provider objects, credentials, reasoning text or hidden chain-of-thought.
Protect necessary opaque continuation state and do not render it as reasoning. Validate trusted scope
and codec identity before restore; SDK deserialization alone establishes neither.

Audit and configure SDK/HTTP retries. They must not automatically issue another potentially effective
operation after an ambiguous dispatch boundary. Safe pre-dispatch transport retries, authoritative
continuation and connector idempotency are separate explicit capabilities with separate evidence.
Count physical model dispatches through the provider transport/MAF client boundary, not merely one
outer RunAsync call. SDK-internal model/tool loops must pass the same reservations and native
approval/effect gates; a function callback cannot bypass them. Disable opaque generative-request
retries unless the adapter proves an authoritative idempotent continuation. Test response loss using
the actual provider SDK against a protocol fixture, including a request received before disconnection.

## Acceptance, background work and cancellation

The existing native scheduler/background store owns work admission, worker claim renewal/fencing,
receipt recording, checkpoint acceptance and process continuation. Register the exact concrete
BackgroundExecutionSafe activity/result aliases, descriptor schema and supported readers; compile
SupportsBackgroundExecution and Background placement. The worker is an ordinary leaf, not structural,
stateful or a checkpoint participant, and returns Complete/Fault/Cancel only.

The journal atomically accepts the validated result/session/usage/receipt under its own current fence.
The leaf returns an opaque receipt handle. The facade validates its immutable association and emits
typed JSON through existing `IRuntimeActivityCheckpointParticipant.PrepareCompletionCheckpointAsync`,
returning `RuntimeActivityCompletionCheckpointPreparation` with the typed completion transition.
A structural Complete alone carries an outcome and otherwise yields ActivityUnit; it is not typed
result transport. The facade is the checkpoint participant; the background leaf must not be one.

The initial native protection profile supports sensitivity/redaction, with RequiresEncryption=false.
Journal payload encryption is separate. Existing ActivityCompletionProjector/input materialization
replace encryption-required values with unrecoverable Withheld markers; the facade does not fix that.
Refuse native RequiresEncryption inputs/results/captures and secret-bound business inputs during
authoring/publication/placement where knowable, and before model dispatch for late-bound withheld/
incompatible input. Never strip a policy or resolve a withheld secret manually. Model credentials
remain authorized point-of-use Connections data. Prove sensitive redacted flow and explicit
unsupported-policy negatives on the real host. Broader native encryption/secret-input support needs
a separately delivered/qualified runtime capability; journal encryption/opaque handles do not promise it.

Journal acceptance and native process application are separate. Crash after journal acceptance causes
receipt/result replay, never another model request. Native process cancellation commits the existing
background cancellation and a specialist cancellation outbox intent together. Durable exact-source
handlers settle the journal separately and retain pre-admission cancellation tombstones. The journal
first writer decides whether a turn accepted or cancelled; native checkpoints decide whether a result
applies to the process. Surface completed/not-applied when those decisions differ. No time-of-check
worker read is advertised as atomic cross-store fencing.

After ProviderDispatched without accepted output, expiry/restart enters OutcomeUnknown and blocks a
fresh provider request. A nonretryable normalized fault/incident makes this actionable in Elsa.
Recovery requires an exact authoritative continuation/idempotency path or a deliberate authorized
settlement/fresh-conversation action. Cancelling an uncertain operation never proves that a remote
effect was absent. Revocation is checked at the relevant point of use; in-flight provider cancellation
is best effort and must be reported honestly.

## Explicit continuity and retention

Fresh invocation is default. Explicit continuation supplies conversation ID and expected revision;
the module derives tenant/owner/version/codec authority. Concurrent stale requests conflict; there
is no hidden queue that reinterprets them against a later history revision. A new specialist version
starts fresh; older conversations remain tied to retained readers and may be inspected under policy.

Accepted journal revision follows turn acceptance, independently of later process application.
Unaccepted/unknown history is never fed into a new turn. Host-configured retention preserves active
pins and replay/cancellation tombstones. Purging content cannot cause an old OperationKey to invoke
the model again. Fork/recovery records source provenance and explicit fresh/history policy; first
release version changes are fresh-only rather than implicit history migration.

## Tools, knowledge and exact approval

Integrate production decision/approval/effect/budget work through [#2912](https://github.com/elsa-workflows/elsa-foundation/issues/2912).
Do not fork another generic collaboration scheduler or reparent its work. A tool catalog is separate
from the activity graph; knowledge bindings carry scope and versioned observations, not flow edges.

An admitted model turn may propose a tool call. Native composition persists the exact proposal,
checks tool/target/scope permissions and required approval, then dispatches the effect as a separate
operation. MAF's in-memory approval request is not the durable permission record; the background
model leaf cannot suspend while waiting for a human. Approval identifies canonical tool arguments,
invocation/operation, target and authorization/resource revisions. Changed arguments invalidate it.
Denied/revoked approval yields no protected dispatch.

Provide one real workflow-tool path and one real MCP path. Record approved arguments, effect identity,
receipt and reconciliation result. Connector-specific idempotency or authoritative readback determines
effect recovery; no universal exactly-once claim. Knowledge retrieval/memory writes are separate
scoped operations; accepted decisions retain the exact allowed observations/references rather than
silently re-querying changed resources during replay.

## Budgets and native teams

Host policy supplies finite defaults and upper bounds. Initial recommendation: 8 model calls, 8 tool
calls, 3 refinement rounds and a 5-minute turn/team deadline; model-specific output/context limits
remain capability-constrained. Caller overrides may lower limits only. Persist reservations before
dispatch and reconcile known actual usage; unknown usage remains unknown and retains conservative
reservation. Token hard limits are claimed only with a provider/input-accounting mechanism that
actually enforces them. Exceeding an enforceable limit prevents further dispatch and creates an
explicit nonretryable exhausted domain outcome/incident, not an incomplete successful business result.

Initial reusable team patterns: sequential specialists; concurrent complete-cohort review; bounded
refinement. Native composition owns participant/message IDs, intended cohort membership, barriers,
bounded feedback, cancellation and one logical parent result. Private/shared conversation, knowledge
and memory policies are explicit and distinct. No MAF Workflows checkpoint runtime or custom graph
editor is needed for these patterns. A future separate adapter needs a concrete unmet requirement
and one proven checkpoint/retry/effect owner.

## Normalized failure families

Use domain failures with inner infrastructure exceptions, exact safe identifiers and retryability:
authoring/conflict; permission/connection unavailable; capability/schema invalid; conversation
scope/revision conflict; cancelled/superseded fence; provider refusal/transport uncertainty;
result invalid; budget exhausted; receipt conflict; retained content/codec/key unavailable.
Failure details support native incidents/operator actions and structured tests. Secrets/raw provider
payloads do not become exception text. An internal missing runtime module/reader remains a bug.
