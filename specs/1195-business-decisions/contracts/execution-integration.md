# Decisions and the shared execution model

The owner raised this boundary on 2 November 2026 during #3602 design. Current source includes the hosting delivered in [#3410](https://github.com/elsa-workflows/elsa-foundation/pull/3410) and Studio/operations delivered in [#3603](https://github.com/elsa-workflows/elsa-foundation/pull/3603); both were verified merged. Their broader program #2892 is not declared complete here. Canonical hosting design: [spec 1017 hosting contract](../../1017-direct-bpmn-hosting/contracts/hosting.md).

## First-release consumption

| Shape | Decision responsibility | Existing host responsibility |
| --- | --- | --- |
| Standalone .NET | Verify frozen artifact, evaluate bounded pure table, return typed result/trace. | Calling application's lifetime, authorization and persistence choices. No Elsa workflow host dependency. |
| Native workflow | Ordinary EvaluateDecision activity invokes the same evaluator on frozen publication content. | Execution identity/authority, scheduling, input/retry semantics, checkpoint and activity completion. |
| BPMN | A normal bound task invokes the native activity; gateway routes on the result. | Existing BPMN driver/interpreter and native-work bridge retain model identity, accepted work, checkpoint, recovery and delivery. |
| Studio case test | Freeze exact candidate/case snapshots in memory, evaluate synchronously, write one completed comparison receipt. | Authenticated request lifetime and storage atomicity; no newly accepted durable job. |

An independent domain defines ownership of DMN assets/semantics; it does not select a separate execution coordinator. Its Design persistence stores drafts, immutable versions, saved cases and completed test receipts, not a competing execution-state/accepted-work ledger.

## Existing first-class hosting seam

If a later concrete requirement calls for independently durable Decisions runs, use these existing contracts:

- `RuntimeModelMaterial` in `Essentials/src/Workflows/Runtime/Core/Models/RuntimeExecutionMaterial.cs`: exact protocol/profile, definition, declared input/output contracts, immutable dependencies and runtime requirements.
- `IWorkflowExecutableStore.SaveModelAsync` / `FindMaterialAsync`: existing material authority with exact identity/content and physical incarnation/retention.
- `IRuntimeExecutionProtocolSource` and `IRuntimeExecutionProtocolDriver`: model-owned interpreter registration/validation/execution through named initialization and scoped dispatch.
- `RuntimeModelExecutionCommand` / `RuntimeModelExecutionContext`: exact material pin, input identity, trusted execution authority, tenant and queue work.
- `RuntimeModelExecutionState`, `RuntimeModelInputReceipt` and `RuntimeModelStateChange`: opaque model payload, revision/lifecycle and accepted-input result.
- `RuntimeCheckpointCommit.ModelState`: model state/input receipt participates in the existing fenced transaction with consumed input and outbox, rather than a separate commit authority.
- Existing Runtime API/Studio authorized inspection contributions: structural identities/statuses by default; value access is independently authorized.

The current selector verifies exact pins/incarnation/scope/tenant/authority before selecting a registered protocol. A Decisions adapter would supply its semantics, not duplicate those mechanics or add kernel model-name switches. Direct evaluation remains independently packaged; such an adapter is optional and is not a prerequisite for #3602's direct/native/BPMN acceptance.

## Invocation authority and protected workflow values

Publication validates the author's source/version access and eligibility; it does not grant perpetual execution authority. At activity invocation/retry, use trusted `WorkflowExecutionState.Authority` and tenant to resolve the host's actual execution subject, then check current Decisions Evaluate and any required protected-value grants. `SystemIdentity`, `RootInitiator` and metadata are attribution/partition facts, not a permission snapshot. Audit RootInitiator is not automatically the execution principal.

For supported persisted-user execution identities, use existing `ICurrentSubjectPermissionAuthorizer` in Foundation Identity Core. It rechecks the current provider's user, tenant membership and roles without an ambient HTTP principal. An explicitly configured host policy must resolve service identities/delegation; never fall back to the active browser user, arbitrary authority metadata or an allow-all default. Denied is an explicit invocation refusal; Unsupported/missing authorization support is unavailable/fail-closed. A later retry rechecks current authority. Previously committed result replay does not rerun evaluation, while every disclosure still checks current read permissions.

Carry decision sensitivity through existing `ActivityContract.Result.Policy`, projection policies and `ValueEnvelope`/`ValueProtectionPolicy`, including outputs, workflow bindings, traces and evidence capture. `IActivityInspectionContextAsync` gates structure, sensitive inspection and payload resolution independently. Contribute Decisions-specific resource denial via existing `IPermissionResourceHandler` for protected decision-dependent executable/source/value reads; return denial when missing the domain grant and otherwise defer so ordinary workflow grants still apply. Definition/source reads must not expose embedded protected rule literals through metadata. The delivered `DecisionsWorkflowsApi` contribution gates native `WorkflowExecutionState` value/payload reads and direct BPMN `RuntimeModelExecutionState` reads against their exact immutable dependency closure. Ordinary executable inspection DTOs omit node metadata. The owning Publishing API authorizes the actual produced closure through the canonical resource authorization service immediately before delivery to any selected HTTP export target, including transitive decision source, so a target choice or source change cannot bypass the gate. Direct host invocation of export targets retains the host's existing authority boundary. Its library visibility default preserves tenant/action inventory only when this is the sole resource contribution; explicit host policies take precedence and other contributions require their own visibility policy.

If an admitted path cannot preserve the required protection through expressions, output bindings or inspection, refuse that protected publication/binding combination until supported; do not label it public or assert support from the Decisions API check alone. Qualification must exercise ordinary workflow execute/read grants without Decisions Evaluate/Source/SensitiveValues, revocation before resumed invocation, supported service identity and unsupported authority, across native and BPMN callers. Direct identity-free .NET applications remain responsible for their own authorization.

## Concrete design correction

The initial plan avoided a new scheduler but did not explicitly reconcile the now-delivered generic model ports. It also over-specified resumable test jobs. The revised plan makes bounded tests synchronous with completed receipts and explicit no-completion/conflict behavior after interruption. This meets saved-case/history requirements without inventing a second execution coordinator. Any future durable admission/recovery uses the shared model host and its qualified providers.
