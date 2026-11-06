# Jobs Studio

Optional Studio module for the [Jobs API](../Api/README.md). `JobsStudioFeature` contributes the `jobs`
module and its static assets; its client uses the shared Studio SDK/UI and the advertised
`elsa.api.jobs/1` collection links. It references no Jobs runtime, provider or workflow library.

The module contributes Jobs, Queues, Schedules, Holds and Capacity routes, job details and separate
submission/schedule/capacity creation views. Current access controls visible actions; every read and
write still goes through the server's current tenant, resource and permission checks. Resource tabs
deduplicate job/schedule detail and editor locations. Job collection return links retain filters and
opaque continuation, then restore focus to the selected row when available.

Read states distinguish initial loading, denied/missing, unavailable and empty pages. Protected input
and result payloads load explicitly with their separate permissions and can be exported only after a
fresh access check and read. Payload values and drafts are not written to browser storage. Parent
navigation recognizes the owning `elsa.native` and `elsa.bpmn` model kinds; arbitrary kinds keep readable
identities without guessed destinations.

Pause offers all four current scopes and preserves overlapping reasons. Hold release addresses one
exact hold/revision. Cancellation is cooperative; result settlement is separate from physical exit and
model consumption. Retry creates a new linked run and leaves the old result and parent unchanged.
The schedule editor defaults to one catch-up run, reviews explicit replay backlog, and preserves draft
input while a conflict requires current-revision review. Administrative reasons are bounded.

An unconfirmed write retains the original address, body and identity in its workspace for exact replay,
including after a later refusal. Navigation and unload guards keep pending work visible. The user may
explicitly forget local confirmation after checking server state and copying its operation identity;
this does not cancel accepted work. The provider audit-inspection API remains an integration gap.

The shared module build configuration emits the ESM entry and content-hashed style contract. Root
integration must include this new Client location in the Studio workspace/build selection, restore its
dependencies, compose the optional API/Studio features and retain actual published assets. This source
checkpoint performs no install, build, typecheck, discovery, component test or host/browser execution.

Isolated control source under `Client/src/__tests__` covers request replay/refusal, current permissions,
scope changes, ignored stale responses, protected drafts, four pause scopes, overlapping release,
Starting/Running and saved-result responsibility, schedule defaults/review, exact UTC input, schema/JSON
bounds, collection return/focus and safe failure states. Fixtures inject presentation state and do not
replace real API, worker, provider, identity, secret or model acceptance. Follow the
[source acceptance record](SOURCE-ACCEPTANCE.md) and canonical composed journeys before claiming readiness.
