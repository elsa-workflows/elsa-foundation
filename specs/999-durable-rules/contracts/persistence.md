# Rules persistence contract

This is a C#-shaped, provider-neutral contract for the six-module Rules domain. It is a planning artifact, not compiled API or runtime evidence. Provider mechanics stay in Rules.Persistence.EntityFrameworkCore; inference and complete receipt validation stay in Rules.Runtime; native parent proof stays in the Rules native adapter.

## Store commands and reader queries

The two public persistence seams have different responsibilities. Commands return small confirmations only; queries return the bounded payloads.

~~~csharp
public interface IRuleRunStore
{
    ValueTask<RuleAdmissionConfirmation> AdmitAsync(
        RuleRunAdmission admission, CancellationToken cancellationToken = default);
    ValueTask<RuleRefusalConfirmation> RefuseAdmissionAsync(
        RuleAdmissionRefusal refusal, CancellationToken cancellationToken = default);
    ValueTask<RuleCancellationConfirmation> CancelAsync(
        RuleCancellationRequest request, CancellationToken cancellationToken = default);

    ValueTask<RuleWorkClaimResult> ClaimWorkAsync(
        RuleClaimRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuleLeaseConfirmation> RenewAsync(
        RuleWorkLease lease, CancellationToken cancellationToken = default);
    ValueTask<RuleLeaseConfirmation> ReleaseAsync(
        RuleWorkLease lease, CancellationToken cancellationToken = default);
    ValueTask<RuleReceiptConfirmation> AcceptRoundAsync(
        RuleWorkToken token, RuleRoundSubmission submission, CancellationToken cancellationToken = default);
    ValueTask<RuleTransitionConfirmation> ConsumeRoundAsync(
        RuleWorkToken token, RuleReceiptIdentity receipt, CancellationToken cancellationToken = default);

    ValueTask<RuleDeliveryClaimResult> ClaimDeliveryAsync(
        RuleDeliveryClaimRequest request, CancellationToken cancellationToken = default);
    ValueTask<RuleTransitionConfirmation> AcknowledgeDeliveryAsync(
        RuleDeliveryLease lease, RuleDeliveryAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default);
}

public interface IRuleRunReader
{
    ValueTask<RuleWorkReadResult> ReadWorkAsync(
        RuleWorkToken token, CancellationToken cancellationToken = default);
    ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(
        RuleDeliveryLease lease, CancellationToken cancellationToken = default);
    ValueTask<RuleRunReadResult> FindAsync(
        RuleRunIdentity identity, CancellationToken cancellationToken = default);
    ValueTask<RuleTerminalReadResult> ReadTerminalAsync(
        RuleRunIdentity identity, CancellationToken cancellationToken = default);
    ValueTask<RuleProgressReadResult> ReadProgressAsync(
        RuleRunIdentity identity, RuleProgressPosition? after, int take,
        CancellationToken cancellationToken = default);
}
~~~

These are the only public store/reader operations required for direct invocation, worker progression, bounded inspection, and native delivery. They do not expose a database context, arbitrary transaction callback, generic queue, execution kernel, universal registry, or mutable state-update method. Admission/cancellation/refusal callers use a Rules facade that validates and snapshots inputs; no public “already validated” flag crosses the boundary. Direct callers retrieve committed result facts and terminal diagnostics through the bounded `ReadTerminalAsync` query, not a synthetic delivery row.

## Identity and immutable requests

~~~csharp
public sealed record RuleScope(string? TenantId, string PersistencePartition);
public sealed record RuleRunIdentity(RuleScope Scope, string InvocationId);
public sealed record RuleInvocationFingerprint(string Sha256);

public sealed record RuleArtifactSnapshot(
    int FormatVersion, string ArtifactId, string Sha256, ReadOnlyMemory<byte> CanonicalBytes);
public sealed record RuleFact(string Atom);

public sealed record RuleAssetPin(
    string PackageRelativeAssetPath, string AssemblyFullName, string AssetSha256);
public sealed record RulePackagePin(
    string PackageId, string NuGetVersion, string NuGetContentHash,
    IReadOnlyList<RuleAssetPin> SelectedAssets);
public sealed record RuleSemanticPin(
    int PinSchemaVersion, IReadOnlyList<RulePackagePin> Packages,
    string LanguageId, int LanguageVersion, int ReceiptSchemaVersion, int StateSchemaVersion);

public sealed record RuleNativeResultAddress(
    int FormatVersion, string WorkflowExecutionId, string ActivityExecutionId,
    string ExecutableId, string ExecutableHash, string NodeId, string BookmarkId,
    string ResumeTargetId, string RegistrationId, string StimulusType, string StimulusHash,
    string ResultContractId, int ResultSchemaVersion, string DeduplicationPolicy, string ProviderId);

public sealed record RuleInvocationEnvelope(
    RuleRunIdentity Identity, RuleArtifactSnapshot Artifact, IReadOnlyList<RuleFact> InitialFacts,
    int MaxRounds, RuleSemanticPin ExpectedImplementation,
    int InvocationSchemaVersion, RuleNativeResultAddress? NativeDestination);
public sealed record RuleRunAdmission(RuleInvocationEnvelope Invocation);
public sealed record RuleCancellationRequest(RuleInvocationEnvelope Invocation);
public sealed record RuleAdmissionRefusal(RuleInvocationEnvelope Invocation, RuleRunDiagnostic Diagnostic);
public sealed record RuleRunDiagnostic(string Code);
~~~

The tenant field is nullable and exact: null never falls through to a default tenant. Scope fields use case-sensitive ordinal equality with no trimming or Unicode normalization and the 256 UTF-8 byte/nonempty limits in the plan. The null tenant tag is distinct. Index a versioned length-prefixed canonical identity hash and compare the retained full identity on every lookup; hash collision is a nonmutating Conflict for commands, never a match to another tenant or invocation. Identity-keyed read queries return NotFound without payload when the indexed row retains a different full identity; the exact requested identity is absent. The facade computes a versioned canonical fingerprint from scope, stable invocation ID, exact artifact bytes/hash, canonical initial facts, captured round policy, expected implementation pin, invocation/state schema versions, and frozen optional destination. Collections and byte buffers are defensively snapshotted before persistence. The caller does not provide an independent fingerprint. Because package pins, facts, receipts, and terminal results are collection-valued records, implementation compares their canonical identities and validated contents rather than relying on generated record equality over collection references.

ExpectedImplementation comes from a trusted Rules identity source. A native publication source persists that same selected pin with the node; an old invocation always reuses its frozen pin. Pin identity covers the three semantic packages—Rules.Core, Rules.Runtime, and Rules.Persistence.Core—and explicitly semantic third-party packages, with actual package ID/version/NuGet content hash and selected runtime asset path/assembly identity/SHA-256. NuGet content hash, raw package archive digest, model artifact hash, and framework/runtime environment are distinct evidence. Package identity does not establish publisher authenticity. The implementation plan owns exact package outputs and the supported file-based .NET 10 runtime profile; there is no runtime downloader, custom loader, or “latest” resolution.

## Ownership, model revision, and round validation

~~~csharp
public sealed record RuleWorkLease(
    RuleRunIdentity Identity, RuleInvocationFingerprint Fingerprint,
    string Owner, long Fence, DateTimeOffset ExpiresAt);
public sealed record RuleWorkToken(RuleWorkLease Lease, long ExpectedModelRevision);
public sealed record RuleDeliveryLease(
    RuleRunIdentity Identity, string DeliveryId, string Owner, long Fence, DateTimeOffset ExpiresAt);

public sealed record RuleRoundIdentity(
    RuleRunIdentity Run, RuleInvocationFingerprint Fingerprint, int Ordinal, string InputStateSha256);
public sealed record RuleRoundInput(
    RuleRoundIdentity Identity, RuleArtifactSnapshot Artifact, RuleSemanticPin Implementation,
    IReadOnlyList<RuleFact> Facts, int MaxRounds, long ModelRevision);
public sealed record RuleFiringEvidence(string RuleId, RuleFact Conclusion);
public sealed record RuleRoundReceipt(
    RuleRoundIdentity Identity, string ImplementationPinHash,
    IReadOnlyList<RuleFact> AddedFacts, IReadOnlyList<RuleFiringEvidence> FiringEvidence,
    string SemanticReceiptSha256);
public sealed record RuleRoundAttempt(string AttemptId, string WorkerId, long LeaseFence);
public sealed record RuleRoundSubmission(RuleRoundReceipt Receipt, RuleRoundAttempt Attempt);
public sealed record RuleStoredRoundReceipt(
    string ReceiptId, RuleRoundReceipt Receipt, RuleRoundAttempt AcceptedAttempt,
    long AcceptedModelRevision, long? ConsumedModelRevision);
public sealed record RuleReceiptIdentity(string ReceiptId, string SemanticReceiptSha256);

public sealed record RuleRoundValidationInput(RuleRoundInput Input);
public enum RuleValidationDisposition { Equivalent, Invalid, UnsupportedFormat }
public sealed record RuleRoundValidationResult(
    RuleValidationDisposition Disposition, string? SafeDiagnosticCode);
public interface IRuleRoundValidator
{
    ValueTask<RuleRoundValidationResult> ValidateAsync(
        RuleRoundValidationInput input, RuleRoundReceipt candidate,
        CancellationToken cancellationToken = default);
}
~~~

The lease owner/fence and trusted database expiry authorize a worker. ExpectedModelRevision is a separate semantic compare-and-swap token. Claim returns the token with the current revision. Mutating acceptance and consumption require the exact current revision and atomically recheck scope, invocation fingerprint, owner, current fence, unexpired lease, input/pin, and nonterminal state. An exact retry of an already-recorded command may return only its prior confirmation as a read-only replay before checking current revision or fence; it must match the complete logical transition and semantic receipt/proof identity, and it never returns successor work or ownership. A changed command conflicts. Claim/reclaim replaces the fence. Renewal and release are lease-only commands: they check current owner/fence/expiry but do not require or change model revision. A provider row concurrency token may change on renewal; it is not the model revision. Lease expiry is evaluated from trusted provider time and cannot be extended after it has expired or been replaced.

The EF provider preserves the shared schema write gate by using tracked `SaveChanges` inside an explicit transaction. It checks a live database UTC sample after all writes and before commit, rolling back the entire attempt if the persisted pre-operation lease expiry has been reached (or the clock moved behind the initial sample). Claim checks its newly assigned expiry; renewal checks the previous persisted expiry, never its newly extended expiry. Row concurrency comparison and the write lock keep ownership stable through this check and commit. This is a transaction-local post-write validity check, not a literal expiry predicate in the generated UPDATE and not a promise that commit completes before wall-clock expiry. Database time is authoritative at the provider's precision; an undetected backward wall-clock adjustment remains an operational clock limitation, not an exactly-once physical-effects guarantee. SQLite samples its VFS UTC clock in a separate SQL statement; future server providers must use live UTC rather than transaction-start time.

Claim requests carry the worker identity assigned by host composition and the immutable implementation plus invocation/receipt/state versions it can execute. Incompatible work remains unmodified and is not repeatedly stolen by workers that cannot interpret it. Delivery claims carry supported terminal-result schemas; result delivery itself does not require the inference implementation.

The runtime is the semantic owner of IRuleRoundValidator; persistence providers depend only on that contract. It recomputes the complete deterministic step over one frozen input: all applicable firings, including already-satisfied conclusions, omitted firings, added facts, duplicate identifiers, and canonical representation. For a new acceptance, the provider also verifies that RuleRoundAttempt.WorkerId and LeaseFence match the current owner and fence; an equivalent replay preserves the first accepted attempt instead of replacing its attribution. The language is finite positive propositional facts, with no terms, variables, bindings, negation, or retraction. A receipt records each firing’s rule ID and single conclusion; AddedFacts contains only the canonical new atoms.

Validation may occur outside the short provider transaction against the frozen RuleRoundInput. Before accepting, the provider atomically confirms that input digest, implementation pin, model revision, and current owner/fence/expiry still match. Equivalent is the only accepted validation result. Invalid returns InvalidReceipt with no run mutation; UnsupportedFormat or missing compatible validator returns recoverable Unavailable. These are not admitted model faults. Persisted malformed or unsupported receipts are retained and reported unavailable rather than silently repaired or reinterpreted.

The semantic receipt digest includes canonical logical round identity, input state, implementation pin, firings, and additions, excluding its own digest field and all attempt metadata. ReceiptId derives from logical round identity. The store records the winning RuleRoundAttempt and any consumed-at-revision marker separately from the immutable receipt body; `RuleStoredRoundReceipt` is the bounded joined projection. Equivalent semantic replay for the same logical round is read-only and preserves the first accepted attempt attribution; a different semantic digest conflicts. Exact replay may confirm an earlier acceptance or consumption after its original owner loses the lease, but returns only that transition’s IDs/revision. It never renews a lease or reveals successor work.

The first provider implementation uses versioned SHA-256 transition IDs over UTF-8 text with fixed-width lowercase digest fields and invariant-decimal ordinals: round ID hashes `rules.round:1:{invocationFingerprint}:{ordinal}`; receipt ID hashes `rules.receipt:1:{roundId}:{inputFactsHash}`; consumption ID hashes `rules.consume:1:{receiptId}:{semanticReceiptHash}`. The invocation fingerprint already binds the complete length-framed identity, frozen artifact, pin and budget. These IDs do not include the physical attempt. Retained receipt metadata stores the exact original consumption revision and terminal/next-round IDs, so retries after subsequent progress return the original confirmation rather than deriving an answer from today's run state.

For new acceptance, validation runs once outside a transaction and every outcome is followed by a fresh winner read, including Invalid and UnsupportedFormat. The provider checks the run's concurrency token after reading related receipt data, before validating that joined projection; a concurrent consume/renew/cancel causes a fresh read rather than a false corruption result. This read check does not replace the transactional tracked compare-and-swap. ReadWork refreshes the model revision only for the same current owner/fence and never writes or renews the lease. Accepted records can be consumed from their common envelope without rerunning the old interpreter. Acceptance commits only the pending receipt and revision; consumption atomically commits additions, fact history, the receipt's consumed marker and either the next round or terminal plus any native delivery obligation. Empty additions establish success before the round-budget fault check, including at the exact budget boundary. Projection sizing includes the pending receipt's actual body and escaped attempt/transition metadata, rather than assuming that its JSON body is the entire record.

## Terminal result and native delivery records

~~~csharp
public enum RuleTerminalKind { Succeeded, Faulted, Cancelled, Refused }
public sealed record RuleTerminalResult(
    string TerminalId, RuleTerminalKind Kind, int CommittedRounds,
    int ResultSchemaVersion, IReadOnlyList<RuleFact> ResultFacts,
    string FinalFactsSha256, RuleRunDiagnostic? Diagnostic);
public sealed record RuleTerminalEnvelope(
    string DeliveryId, RuleNativeResultAddress Destination,
    RuleTerminalResult Result, string CanonicalPayloadSha256);

public enum RuleDeliveryProofKind { Consumed, AlreadyConsumed, ParentProvenTerminal }
public sealed record RuleDeliveryProof(
    RuleDeliveryProofKind Kind, string DestinationIdentity,
    string TerminalId, string PayloadSha256, string ParentCommitOrTerminalId);
public sealed record RuleDeliveryAcknowledgement(RuleDeliveryProof Proof);
~~~

The terminal fact set is the complete bounded canonical committed result needed by direct and native callers; its content digests are recomputed from canonical bytes. A pre-admission cancellation/refusal has the defined empty result set. After admission, cancellation preserves initial and committed facts but excludes any pending receipt that has not been consumed.

If a native destination exists, terminal/refusal result and owed delivery are committed in the same Rules transaction as the terminal transition. Direct runs create no delivery outbox item. The adapter verifies delivery proof against the admitted executable, native bookmark/registration, exact destination and payload before constructing the acknowledgement. The Rules store verifies that proof identity matches its frozen terminal envelope. Missing parent proof remains retryable. Confirmed equivalent consumption, prior consumption, or durable terminal-parent proof may retire delivery; mailbox enqueue status alone cannot.

When comparing a deserialized terminal record containing IReadOnlyList<RuleFact>, the native adapter must compare canonical bounded content and identities, not generated C# record equality over collection references. Existing RunEtl.ResumeAsync has a scalar-only comparison at Extensions/Etl/src/Workflows/Runtime/Activities/RunEtl.cs:74-81; that comparison must not be copied for Rules.

## Confirmation and read projections

~~~csharp
public enum RuleAdmissionDisposition
{
    Admitted, Replay, AlreadyCancelled, AlreadyRefused, Conflict, Invalid, Unavailable, Refused
}
public sealed record RuleAdmissionConfirmation(
    RuleAdmissionDisposition Disposition, RuleRunIdentity Identity,
    long? ModelRevision, string? TerminalId);

public enum RuleRefusalDisposition
{
    Refused, Replay, ExistingAdmission, AlreadyCancelled, Conflict, Invalid, Unavailable
}
public sealed record RuleRefusalConfirmation(
    RuleRefusalDisposition Disposition, RuleRunIdentity Identity, string? TerminalId);

public enum RuleCancellationDisposition { Cancelled, Replay, TerminalUnchanged, Conflict, Invalid, Unavailable }
public sealed record RuleCancellationConfirmation(
    RuleCancellationDisposition Disposition, RuleRunIdentity Identity,
    long? ModelRevision, string? TerminalId);

public sealed record RuleClaimRequest(
    string Owner, IReadOnlyList<RuleSemanticPin> SupportedImplementations,
    IReadOnlyList<int> InvocationSchemas, IReadOnlyList<int> ReceiptSchemas,
    IReadOnlyList<int> StateSchemas);
public enum RuleWorkClaimDisposition { Claimed, NoEligibleWork, Unavailable }
public sealed record RuleWorkClaimResult(
    RuleWorkClaimDisposition Disposition, RuleWorkToken? Token);

public sealed record RuleDeliveryClaimRequest(string Owner, IReadOnlyList<int> ResultSchemas);
public enum RuleDeliveryClaimDisposition { Claimed, NoEligibleDelivery, Unavailable }
public sealed record RuleDeliveryClaimResult(
    RuleDeliveryClaimDisposition Disposition, RuleDeliveryLease? Lease);

public enum RuleLeaseDisposition { Renewed, Released, LeaseLost, Unavailable, Conflict }
public sealed record RuleLeaseConfirmation(
    RuleLeaseDisposition Disposition, RuleWorkLease? Lease);

public enum RuleReceiptDisposition
{
    Accepted, Replay, Conflict, LeaseLost, RevisionConflict, InvalidReceipt, Unavailable
}
public sealed record RuleReceiptConfirmation(
    RuleReceiptDisposition Disposition, RuleRunIdentity Identity, string? ReceiptId,
    long? ModelRevision, string? TerminalId, string? NextRoundId);
public enum RuleTransitionDisposition
{
    Consumed, Acknowledged, Replay, TerminalUnchanged, Conflict,
    LeaseLost, RevisionConflict, ProofUnavailable, Invalid, Unavailable
}
public sealed record RuleTransitionConfirmation(
    RuleTransitionDisposition Disposition, RuleRunIdentity Identity,
    string? TransitionId, string? ReceiptId, string? TerminalId,
    string? NextRoundId, long? ModelRevision);

public enum RuleWorkReadDisposition { EvaluateRound, ConsumeReceipt, Terminal, LeaseLost, NotFound, Unavailable }
public sealed record RuleWorkReadResult(
    RuleWorkReadDisposition Disposition, RuleWorkToken? CurrentToken,
    RuleRoundInput? RoundInput, RuleStoredRoundReceipt? PendingReceipt, string? TerminalId);
public enum RuleDeliveryReadDisposition { Ready, LeaseLost, NotFound, Unavailable }
public sealed record RuleDeliveryReadResult(
    RuleDeliveryReadDisposition Disposition, RuleTerminalEnvelope? Envelope);

public enum RuleRunReadDisposition { Found, NotFound, Unavailable }
public enum RuleRunStatus { Running, Succeeded, Faulted, Cancelled, Refused }
public sealed record RuleRunView(
    RuleRunIdentity Identity, RuleInvocationFingerprint Fingerprint,
    string ArtifactId, string ArtifactSha256, string ImplementationPinHash,
    RuleRunStatus Status, long ModelRevision, int CommittedRounds,
    int CommittedFactCount, string? PendingReceiptId,
    string? TerminalId, RuleTerminalKind? TerminalKind);
public sealed record RuleRunReadResult(RuleRunReadDisposition Disposition, RuleRunView? View);

public enum RuleTerminalReadDisposition { Found, NotTerminal, NotFound, Unavailable }
public sealed record RuleTerminalReadResult(
    RuleTerminalReadDisposition Disposition, RuleTerminalResult? Result);

public sealed record RuleProgressPosition(string Cursor);
public enum RuleProgressItemKind { Fact, Round }
public sealed record RuleRoundProgress(
    int Ordinal, string InputStateSha256, string? ReceiptId,
    string? SemanticReceiptSha256, long AcceptedModelRevision, long? ConsumedModelRevision,
    string? AcceptedAttemptId, string? AcceptedWorkerId, long? AcceptedLeaseFence,
    int AddedFactCount);
public sealed record RuleProgressItem(
    RuleProgressItemKind Kind, string Key, long FirstVisibleModelRevision,
    RuleFact? Fact, RuleRoundProgress? Round);
public sealed record RuleProgressPage(
    IReadOnlyList<RuleProgressItem> Items, RuleProgressPosition? Next);
public enum RuleProgressReadDisposition { Page, NotFound, Unavailable, InvalidPosition }
public sealed record RuleProgressReadResult(
    RuleProgressReadDisposition Disposition, RuleProgressPage? Page);
~~~

These unions have shape invariants: EvaluateRound carries exactly a round input and refreshed token; ConsumeReceipt carries exactly the accepted pending receipt and refreshed token; Terminal carries only terminal identity; LeaseLost, NotFound, and Unavailable carry no successor payload. Command confirmations contain only disposition and transition/receipt/terminal IDs, current model revision, and same-owner lease confirmation where applicable. In a delivery acknowledgement, TransitionId identifies the delivery obligation. They never return facts, full receipts, run views, or result envelopes.

FindAsync returns a bounded summary only. A run view contains exact identity/fingerprint, implementation/artifact pins, status, current revision, committed-round count, pending-receipt ID, terminal ID/outcome, and safe counts. ReadTerminalAsync returns the full but bounded immutable terminal result, including facts and diagnostic, for direct callers; it does not create or imply a delivery outbox. A progress page contains one bounded stream of tagged fact or round entries, ordered by first-visible model revision then item kind and canonical key; round metadata includes winning physical attempt and separate accepted/consumed revisions. The first page captures a model revision; its opaque exclusive cursor binds exact scope/run, that snapshot revision, and the last ordering position. Subsequent pages reconstruct the same as-of view from immutable receipt/fact history and revision-stamped transitions. Thus facts committed later—even if their canonical keys sort earlier—are excluded from that cursor sequence, rather than silently inserted before or after its cursor. A fresh first-page query observes a newer snapshot. The page maximum is the plan’s 256 entries. The plan’s 8 MiB state-projection maximum applies to one current projection, not cumulative history; receipts are separate immutable rows and remain inspectable through bounded pages.

All queries are exact-scope and read-only. ReadWorkAsync may receive a token with an older model revision only if the same lease owner/fence is still current and unexpired. It returns one consistent current snapshot and refreshed model revision without mutation or renewal. Thus, after an acceptance confirmation returns the receipt ID and new model revision, the same owner may read with its earlier token to observe the pending receipt and refreshed token; it does not wait for lease expiry. The next command must use that observed revision and atomically recheck it. An expired/replaced fence returns LeaseLost and no work payload. ReadDeliveryAsync similarly checks delivery owner/fence/expiry but does not renew. Find and progress queries do not reveal work-owner tokens.

## Transition and replay rules

| Transition | Atomic effect and disposition |
|---|---|
| New admission | After identity-first lookup proves the scoped key is absent, validate the artifact and actual worst-reachable receipt/result representation against the [plan bounds](../plan.md#finite-v1-bounds), confirm the requested semantic pin against trusted installed deployment evidence, then create the run, initial facts, first owed round, and model revision. |
| Admission replay | After common-envelope validation and canonical fingerprinting, first look up the exact scoped invocation key. Matching fingerprint returns Replay/AlreadyCancelled/AlreadyRefused from the existing row before interpreting the artifact or checking current interpreter availability. A structurally invalid new invocation, including MaxRounds less than or equal to zero, returns Invalid and creates no Rules row. A retry must retain that invocation’s original pin and destination; it cannot stamp the currently installed pin over an older admitted request. Different fingerprint is Conflict. Replay confirms the existing admission even if this deployment cannot execute its pin; work claim/evaluation reports recoverable Unavailable or leaves the run for a compatible worker, without changing its identity or state. |
| Pre-admission cancellation | Race against admission/refusal via the same unique scoped key. Insert a cancellation tombstone and, for a proven native destination, its terminal delivery atomically. Matching later admission replays it; changed identity conflicts. Cancellation does not parse old artifact/state schemas. |
| Permanent refusal | A first deterministic refusal through AdmitAsync returns Refused; later matching admission returns AlreadyRefused. RefuseAdmissionAsync replays an existing matching refusal even if the newly supplied diagnostic differs, since the invocation identity and original settlement are immutable. A new refusal requires the supplied diagnostic to equal the compatible validator's diagnostic. Only a valid common envelope and deterministic permanent Rules-level invalidity established by a compatible interpreter can create Refused. Refusal settles the same scoped key and native delivery atomically. Invalid envelopes, including a nonpositive round limit, return Invalid without inventing a Rules run or refusal tombstone; missing/mismatched local capability is Unavailable and also creates no refusal tombstone. For native input invalidity, the adapter settles the existing durable parent dispatch-refusal outcome only after proving the committed destination; until then the parent's native responsibility remains retryable. |
| Accept round | Validate the complete candidate receipt against frozen input; atomically recheck identity, input digest, expected model revision, and current fence; store one pending receipt plus winning attempt attribution; increment model revision only. Facts and committed-round count do not change. |
| Consume receipt | Accept only the persisted receipt ID/hash. Atomically mark it consumed, apply its additions once, increment committed-round count, then create exactly one next owed round or terminal result and native delivery. No additions means Succeeded even on the last allowed round; additions on the last allowed round mean Faulted with rules.round-budget-exhausted. |
| Cancel active run | Increment model revision, retain initial/committed facts, leave an accepted pending receipt unapplied, set immutable Cancelled result, and atomically create native delivery if applicable. If consumption commits first, preserve those newly committed facts; cancellation may stop only a run still active. Existing terminal outcomes remain unchanged. |
| Renew/release | Verify the current unexpired owner/fence. Renewal changes expiry/provider row token only; release invalidates the fence. Neither reads nor writes require a semantic model revision. |
| Acknowledge delivery | Require a current delivery lease and proof matching the immutable terminal payload and frozen native destination. Acknowledgement is Replay only for the same proof; missing proof retains delivery for retry. |

Database uniqueness and conditional writes arbitrate races. After a lost response, a caller may replay the exact command to learn its disposition, then use the old token for a read. If its same owner/fence is still current, ReadWork returns the latest model revision and tells it whether to evaluate, consume, or stop. If replaced, the old owner may at most learn an exact prior replay; it cannot obtain a successor token or work. A successor claim/read sees the same pending receipt or next owed round under the new fence.

## Provider and verification boundary

Rules.Persistence.EntityFrameworkCore owns a Rules-specific context, provider model, history family, and migration set for SQLite, PostgreSQL, SQL Server, and MySQL. The contract matrix runs against every real provider; SQLite fast tests are not provider qualification. The shared-database topology witness uses two independently running packaged PostgreSQL workers on the same declared machine/container topology. This does not claim separate physical hosts or power-loss qualification. ETL persistence is SQLite-only and is not the Rules store template.

An ordinary native CLR activity plus an immutable publication contribution submits admission/cancellation intent through native checkpoints and routes terminal data through bookmark resume. Rules state remains in Rules persistence, never in the parent workflow checkpoint. The adapter proves durable parent consumption or terminal state before acknowledgement. It need not change the shared activity-capability registrations owned by #2891/#1785.
