# Runtime enrollment settlement S1 — source increment

Part of #2892. This is the scoped Runtime-owned acceptance/abort protocol approved by the owner on 6 November, following conditional correctness review. The [programme checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6694749184) records active implementation. This increment supplies concrete source; it does not advertise a supported native composition or complete T005/T015/T016.

## Consumer and authority boundary

Runtime EF owns `IWorkflowPreparationEfEnrollmentSettlementFactory`, the bound `IWorkflowPreparationEfEnrollmentSettlement`, and explicitly EF-specific results/participant/lease/frame facets. Bind uses the actual selected source-reference writer and configured canonical issuer. A provider lacking the optional facet is Unsupported; its absence is never a negative enrollment receipt. Factory registration and positive native admission remain absent in this source increment.

Publishing's own domain-facing cooperation port belongs to Publishing.Core. Its internal EF bridge consumes the Runtime EF seam with the actual configured issuer. Provider-neutral Publishing orchestration gains no EF reference. The internal Runtime `WorkflowPreparationOwnerInvocation` reuses the existing same-domain EF friend and opaque owner session inside one genuine canonical callback. Callback lifetime permits genuine native Join before current-owner validation; committed results require joined current-owner checks. Failures during later callback/issuer release preserve Unknown after a participant reports Bound/Accepted/Aborted. Runtime.Core's Complete, token and session source/signatures remain unchanged; no new friend or public session constructor is added.

`BindTargetAsync` is a prelaunch obligation of the selected native provider and actual owner issuer. It must establish actual target continuity and durably record the canonical original intent/association before *any* independent Retain launch. The source contains no implementation of that durable producer yet: a string/record/Bound result alone supplies no native authority. Unimplemented or changed/unknown target profiles remain unsupported/pending. Existing native-R2 registration and SQLite lifecycle proposals are not adopted.

## Native transaction mechanics

`WorkflowPreparationEnrollmentTransaction` requires the selected participant's authenticated private active frame, the genuine fresh Runtime context, and the exact owned `EfSharedTransaction` connection/transaction. It owns commit/disposal for that supplied transaction. A participant must dispose its transaction if construction fails before ownership enters the operation. The frame's actual implementation must prove protocol readiness, all-writer/old-transaction exclusion, actual target continuity and genuine header/root custody; interface implementation alone proves none of these.

The permanent unique barrier key is the existing checked `(scope, OwnerAddress, OperationId)` identity. Intent, target, ledger incarnation and prepared material are immutable bindings, never additional key namespaces. Logical owner revision/state are fresh canonical expectations. The checked codec rejects corrupt wire/projections and mismatched identities. No TTL, material foreign key or delete path is introduced.

Acceptance inserts the unique Accepted barrier before the actual native callback stages its genuine planned header/root protection. It validates the persisted real header and protection in that same transaction before committing. Aborted refuses subsequent acceptance. A statement/uniqueness failure propagates and the operation rolls back; it never infers which outcome won or increases retry budgets. All actual acceptance paths still need integration before any provider can report capability.

Settlement reads real header evidence before considering an abort. A matching retained/terminal predecessor header with no barrier takes the verified-custody genuine-header path; it creates no intent, barrier, header or token. Accepted barriers require the matching genuine header. Aborted plus a header, Accepted without its header, or conflicting bindings refuse and preserve data.

Only a genuinely joined, currently rejected canonical owner with its original committed enrollment binding can insert Aborted. Absence without that binding is Pending. The unique native write, never a snapshot read of absence, must win against prior acceptance. The result escapes only after confirmed physical commit and successful cleanup. Commit-attempt/cleanup uncertainty throws the existing outcome-unknown exception. Accepted returns the real current/terminal snapshot for existing token-required Complete. Aborted returns its permanent barrier incarnation, original binding, and no header/token/selection or Core retention-success status.

Publishing must record the actual native settlement under fresh owner CAS before rejected source-pin release. A lost acknowledgment retries the original owner/key/intent; it cannot recompile, replace the operation, or restore a never-armed exemption. Published source-pin transfer and genuine-header completion remain distinct.

## Explicit incomplete gates

1. Actual durable prelaunch target association and continuity; no target A/B, clone or opened-file identity shortcut.
2. Genuine native frame/registration and all acceptance writer integration, including exclusion/settlement of old transactions.
3. Additive family mapping, readability/versioning, migrations and preservation. The entity/configuration is deliberately **not mapped** in RuntimeDbContext; no schema version or support is allocated here.
4. Publishing consumer/issuer durable intent/outcome CAS and cleanup ordering.
5. Exact native late-commit, lost-reply, abort-before-send, predecessor, target-switch, restart and stale-writer controls for every advertised topology/provider; protected delivery and resulting-main verification.

Codec/session unit controls are narrow source contracts, not durable provider or upgraded-host recovery evidence. No native support, complete retention, Studio, ETL production acceptance, new #3804 prerequisite, formal PR review round/reset, or successful required CI is inferred from this increment.
