# Preparation retention contract semantics — plan R3

**Status**: Proposed C# signatures are [runtime-preparation-api.cs](runtime-preparation-api.cs) and [ef-participation-api.cs](ef-participation-api.cs). They are design artifacts, not production edits/compiled ABI. [Issuer integration](issuer-integration.md) names the actual consumer and provider work.

## Public and provider boundaries

- IWorkflowPreparationRetention is the caller facade. Retain/Complete accept data commands only; callers cannot supply an issuer, lease, EnsureCurrent callback, session or transaction.
- Existing canonical SourcePublicationCoordinatorFactory binds the actual configured EfSourcePublicationStore and actual Workflow/Agent adapter to an issuer; Runtime factory Bind qualifies that configuration. Bind is host/provider configuration, not an input endpoint or new owner registry.
- Existing source-reference reader/writer gains the typed preparation query/staged write interfaces. Legacy defaults throw NotSupported or return false admission; never return empty/absent as a fallback.
- Writer methods StageRetain/StageComplete only stage within an actual participant frame. They do not call the old public own-transaction root writer.
- Outcomes distinguish Retained/AlreadyRetained, Released/AlreadyReleased, known Pending, Conflict and Unsupported. Precommit provider failures carry WorkflowPreparationException with original cause. A commit attempt with lost outcome throws WorkflowPreparationOutcomeUnknownException; it is reconciled, not treated as known failure.

Confirmation is non-null only for confirmed Retained/AlreadyRetained/Released/AlreadyReleased; no success is returned before native commit. Pending means an authoritative current fence exists but no complete terminal evidence exists. Conflict covers expected-token/current-owner/prepared-winner/immutable-selection mismatch. Unsupported covers an unqualified actual provider/issuer/topology/schema composition. Invalid input throws ArgumentException before mutation; cancellation known before commit remains OperationCanceledException. Commit-attempt uncertainty takes precedence over cancellation/cleanup, preserving the primary cause; post-commit guard-release failure is also Unknown rather than a claimed rollback. Codes use the existing bounded provider/domain error-code convention; they never include authored content or credentials.

Typed selectors/pins reuse canonical identities, with separate native/model and template variants. OwnerAddress is the canonical length-framed hash of the actual issuing SourceScope (Partition, TenantId, Kind, Authority) plus SourceId; Runtime scope is separate and must match the actual selected stores. OwnerStamp binds immutable request/snapshot/source/compiler values, actual winning PreparedProfile/PreparedContentFingerprint and expected canonical Revision+State/incarnation. Those DTOs are evidence, not issuing authority.

PreparedContentFingerprint remains the owner's existing ComputePreparation(Profile, Content), covering the winning persisted prepared envelope. Runtime Selection.Fingerprint is a separate canonical hash of the bound source/prepared values, ordered typed selected roots and the complete closure's stable material identities/content hashes/incarnations. It EXCLUDES WorkflowPreparationTemplatePin.Revision and every mutable per-attempt CAS/lease observation. Never hash/compare the whole pin record or serialized selection as immutable identity. Never equate these hashes, replace either with the other, or rewrite the authored snapshot/source fingerprint. Inspect is an untrusted observation; under Join, compare its roots/prepared binding with actual typed-adapter authority and recompute this stable projection before any retention write.

TemplatePin.Revision remains an accepted historical observation in the unchanged API/persisted selection. Every attempt independently reads and checks the fresh native template/hash-claim coordination observations under its actual fence; it must not require current revision to equal that historical field. Refresh only the attempt-local CAS observations, not accepted immutable pins, Selection.Fingerprint or the accepted header token. Legitimate Agent publication may increment template revision with unchanged identity/hash/row and hash-claim incarnations; AlreadyRetained and initial transfer still accept the original selection/token. Content or physical-incarnation replacement still refuses, and a revision race during the current attempt still fails/retries the native CAS.

## Opaque authority and registration

Only the actual configured issuer calls back with its private provider lease. Existing Publishing EF registration supplies the selected canonical EfSourcePublicationStore, its operation-scoped issuer and bound actual typed adapter through SourcePublicationCoordinatorFactory. Runtime's native provider binding admits that exact registered issuer/store/material/source/template tuple; an arbitrary host/request interface implementation is not an admitted native integration. No generic owner-policy registry is added.

Runtime's scoped coordinator mints WorkflowPreparationOwnerSession inside that configured callback, with an internal constructor and actual callback-lifetime guard. Core grants same-domain Runtime construction access; Publishing EF cannot mint sessions. No public constructor or serialization path exists.

Native participant validates: exact configured issuer and participant/source/material/template instances; actual scope and schema; its private active frame; the minted session/invocation identity; fresh shared context membership; actual Connection/Transaction references; and callback lifetime. Stage calls RequireActiveContext on the actual selected participant, not a caller object's EnsureActive/PhysicalStoreIdentity claims. Forged, expired, cross-callback or mismatched frames refuse before effects.

CanRetainPreparations is preflight only. Native Join and final CAS enforce authority. Unknown/decorated/mixed tuples are supported only after proving every forwarded boundary; an arbitrary no-op issuer/EnsureCurrent implementation is not admitted.

## Decision timing

OwnerSession.Decision is a live lease-backed read guarded by the Runtime callback. The actual issuer lease refuses Decision/SelectedRoots before successful Join. SAME mode Join first stages the canonical operation guard in the participant-owned transaction, then verifies canonical prepared/terminal data using actual readers. Independent mode additionally verifies its already-held native guard.

Only then are selected roots and verified decision available. Session never copies an early unfenced receipt. StageComplete rejects unjoined authority, wrong issuer/session/frame or mismatched exact selection. Missing Agent Design publication, receipt or source/material evidence yields Pending/Unknown and retains.

SelectedRoots are decoded/validated by the actual bound Workflow or Agent adapter from the WINNING persisted SourcePublicationPreparation, not a Runtime SourceKind switch, caller DTO or the losing producer's local envelope. Join verifies the winner's ContentFingerprint/Profile and material under the real operation guard; a winner/binding mismatch conflicts before Runtime root writes. Preserve original source/producer attribution.

## Revision progression and stable replay

The actual canonical progression includes Captured0 → Reviewed1 → Reserved2. Domain publication can commit while the operation is still Reserved2; RecordReceipt and AssociateRelease subsequently advance the revision and reach Published. A current expectation is checked against the current native row on each fresh mutation. AcceptedOwner is historical: AlreadyRetained/Complete compare immutable operation identity, physical ledger incarnation, request/source/prepared binding and selection, not whole-stamp equality with that old revision/state.

Legal current states are operation-specific: Retain creation permits Captured/Reviewed/Reserved while the same canonical preparation remains eligible. AlreadyRetained may also confirm an existing Retained header after a legitimate advance to Published; Published cannot create a missing preparation root, and Rejected cannot retain. Initial published transfer accepts Reserved with verified actual domain commit, or later receipt-recorded/Published states, only after exact canonical publication evidence and actual Published closure protection are verified. Actual Rejected state is the only rejection authority. Impossible regressions, stale expected current revisions and changed immutable/prepared winners conflict.

Decision.VerifiedAt records the historical current fence used to verify the result. CanonicalDecisionId/DecisionFingerprint identify the immutable published receipt (or immutable rejection identity) plus operation incarnation, prepared binding and stable Runtime selection fingerprint. They EXCLUDE mutable verification revision/state/FenceRevision, template CAS revisions, all other mutable coordination observations and wall-clock observation time. Thus the same real receipt at Reserved2 and later Published, including legitimate template-fence revision advancement, yields the same decision fingerprint. Missing receipt never becomes rejection.

Join binds a terminal decision's SelectionFingerprint to the existing checked preparation header and the actual adapter-selected roots/source/prepared binding; it cannot accept a caller's fingerprint as terminal authority. StageComplete compares it with that same immutable header selection under the native metadata fence. Published decision hashing uses the actual immutable receipt fields; rejection identity derives from the actual operation incarnation and durable Rejected decision, not an invented publisher receipt. Runtime recomputation includes command Id/actual scope, all immutable source/prepared bindings, ordered typed roots and the stable identity/content/incarnation projection of every closure pin, excluding mutable revision observations; use existing canonical hashing/identity helpers and owned copies.

On the Retained → closed CAS, persist ClosedFrom (the exact accepted header token) and the immutable decision in the same commit. A lost-ack replay matching Id/header incarnation/selection and ClosedFrom or the current terminal token returns AlreadyReleased from checked terminal metadata, even if the owner legally advanced or all material/reference members have been collected. It requires no new publication, live material grants or live anchor. A different incarnation/selection, future token or conflicting terminal decision refuses. Retain against any terminal row cannot resurrect it. A stale owner expectation cannot perform a new write; reading an already recorded immutable terminal result creates no new authority.

## Exact nested execution

For native/model roots:

    await rootLeases.ExecuteFencedAsync(roots, uniqueInvocation, async (grants, leaseCt) =>
        await configuredIssuer.ExecuteFencedAsync(expectedOwner, actualParticipant,
            async (lease, ownerCt) => {
                // Coordinator mints opaque active session from this configured callback.
                // Participant joins actual issuer, stages and commits within this scope.
                result = await actualParticipant.ExecuteAsync(session,
                    (commit, commitCt) => references.StageRetainPreparationAsync(
                        command, grants, commit, commitCt), ownerCt);
            }, leaseCt), callerCt);

This illustrates lifetime, not a second implementation body. Completion uses the same shape with StageComplete. The participant calls session.Lease.JoinAsync(nativeFrame) after opening the admitted owned boundary and before material mutation locks; it checks EnsureCurrent after waits and before commit. Entire commit, rollback/disposal and issuer guard release finish before the root callback returns. The manager may then stop renewal/release its grants. Never keep grants for a later call.

Template-only skips the root manager's empty-set overload and runs issuer→participant→native template/hash-claim fences directly. Confirmed immutable terminal replay reads metadata without material leases or current material existence.

## SAME physical tuple

Select this mode before starting any owner write transaction. Actual Runtime participant uses existing EfSharedTransaction.BeginAsync over configured module contexts. It creates fresh contexts on one actual connection/transaction and owns the only commit/disposal. Its current helper requires the SAME relational provider and exact nonempty connection string, one instance per concrete context type, and each type's public constructor accepting its own options. Check these constraints before beginning; mismatches never fall through to independent mode. Enlist an identical configured instance once; reject unrelated duplicate types the helper cannot resolve. Verify actual native identity as well; textual equality alone does not qualify an arbitrary decoration/handle.

Issuer Join gets the actual fresh OwnerContext and stages canonical row CAS in that transaction. It cannot Begin/Commit/Rollback/Dispose the boundary. Do not call existing EfSourcePublicationStore.Write or SaveWithRootWriteLeasesAsync inside it. Dedicated stage helpers may use BeginOperationAsync non-owning handles; failed/uncommitted participation marks the owner rollback-only.

Actual lock order: canonical operation coordination first, then deterministic artifact coordination/lease rows, template/hash claims, publication source facts and preparation metadata row. Discover graph/material before acquiring partial mutation locks. All competing canonical transitions participate in the same physical owner guard.

No ambient/enlisted caller transaction is adopted. Configured contexts with pending writes or unrelated transactions refuse before mutation.

## INDEPENDENT physical tuples

After actual independence proof, issuer opens/holds its own canonical ledger guard and invokes the Runtime callback. Runtime participant owns a separate Runtime-only transaction. Join verifies actual distinct stores and active owner/session; it never enlists one database in the other's transaction.

SQLite connection-string mismatch is not proof: aliases/hard links/unknown identities refuse. Required positives use separately created durable files with verified actual identities. Same-file aliases never fall through to independent mode.

Runtime may commit before an owner guard's release/ack fails. Preserve OutcomeUnknown, read actual metadata and canonical operation, and do not republish or pretend both transactions were atomic. Dispose Runtime locks and issuer guard before the root callback ends.

## Terminal verification and readers

Actual canonical owner verifies SourcePublicationOperation state/revision, immutable preparation and receipt. Workflow FindCommitted checks exact source-reference publication. Agent FindCommitted checks actual Activities Design publication plus exact Runtime reference/template. Runtime-only Agent material is not terminal publication.

For INITIAL transfer Runtime StageComplete rechecks genuine Published references and full closure under actual native source/template/material fences before closing. Terminal AlreadyReleased replay uses the persisted checked fact and ClosedFrom token; it does not re-require material/reference rows after ordinary GC. Rejection requires actual canonical Rejected state. Header stores the verified terminal identity/fingerprint permanently.

Dispatcher/resolver allow only Published/TestRun requiredScope before replay/material/effects, then block reference-less fallback if typed preparation history exists. Direct/indirect/model/trigger/export/import/interest selection cannot use a preparation ID as authority. Known independent legitimate authority still works.

GC initial/final reads union Retained typed preparation closures; template-only/nested protection is independent of execution roots. Terminal documents retain no material and never disappear through ordinary cleanup. Legacy unknown preparation reads conservatively stop collection/implicit legacy admission, not claim empty roots.

Preserve #3912 physical PublicationScope and #3904 native interest fences/accessor by explicit source reconciliation; no source-policy or owner-policy fork.
