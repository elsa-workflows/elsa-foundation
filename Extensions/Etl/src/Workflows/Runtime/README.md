# ETL native workflow adapter

`EtlWorkflowsRuntime` contributes `RunEtl`, checkpoint enrichment, durable admission/cancellation handlers and a terminal-result delivery adapter. It depends on ETL runtime and ordinary workflow resumption. It references runtime contracts only; it does not query the ETL design store or specialize the workflow kernel.

The activity's `Pipeline` input is a canonical published selection from `EtlPublishedSelection`. Its literal definition/version/full content hash is covered by the existing workflow executable hash. The executable node retains the complete bounded artifact under `etl.pipeline.v1`; activation verifies it against that literal. Trusted parent state and the selected persistence scope supply the tenant/partition address.

`RunEtl` suspends through the normal typed activity contract. Its immutable private document contains one invocation, including the actual resume-target identity resolved from the pinned executable, and no rows, progress or worker claims. Source publication declares the local target; placed identities may be opaque. Earlier direct-target requests retain a missing/null-field reader fallback, without a rolling-downgrade or historical placed-state migration claim. The adapter captures the actual persisted registration and committed bookmark before emitting admission. Cancellation can capture that registration before bookmark creation; after native trigger consumption it recovers the actual identity from the durable consumed receipt. Both paths use ordinary native checkpoint/outbox commits. Admission resolves host bindings and records responsibility; ETL workers perform the file work separately.

Terminal delivery creates an explicitly scoped native operation and uses the exact saved bookmark, payload alias/schema, provider and delivery identity. A mailbox status does not acknowledge ETL delivery. The adapter reads durable native state to demonstrate consumption or parent/activity termination; missing state by itself leaves delivery owed. Native success/business error use normal authored outcomes, while fault and cancellation use native transitions. Composite activities retain their own continuation semantics.

The complete acceptance gate remains [spec 255](../../../../../specs/255-finite-file-etl/tasks.md). The [native checkpoint](../../../../../specs/255-finite-file-etl/evidence/native-subtree-cancellation.md) proves actual durable Sequence, Flowchart and CompensationScope continuation plus whole-workflow/placed-subtree cancellation in both admission orderings. Full persisted authoring/publication, API/Studio integration of terminal materialization, two-process recovery and the complete product journey remain acceptance gates. Runtime fixtures do not substitute for those gates. Physical effects remain at least once; compensation is explicitly authored.

## Native inspection reference

`EtlWorkflowsRuntime` contributes the existing Runtime activity-state view for `RunEtlState` as
`etl.run-reference/1`. After Runtime authorizes the run/activity inspection, this projection checks
the persisted invocation against the native execution's tenant, partition, pin and node. It exposes
only the ETL `runId` and result-address workflow/activity/node/registration/resume-target identities.
The actual registration comes from the live wait or its consumed typed receipt, including after
completion-payload retirement. Connector bindings, pipeline contents and captured values are never
part of this view, including for callers who may inspect sensitive values.

The reference does not assert that ETL admission has already completed. Opening it uses the ETL
API's own authorization and availability checks. The projection reads existing state and adds no
persisted field, dispatch mechanism or Runtime API dependency.

## Cross-domain contributions

`EtlWorkflowsRuntime` contributes `RunEtlStateViewProjector` to the Runtime [`IActivityStateViewProjector`](../../../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md) port. It projects live `RunEtlState` and, through the port's optional cleared-state members, a completed `RunEtl` whose private state was cleaned up. The cleared-state read verifies the retained scoped invocation, worker and pipeline pins, terminal outcome and consumed typed receipt before exposing only run and result-address identities. Projectors are singletons, so it resolves the scoped ETL store per read; without ETL storage the fallback refuses and live projection still works.
