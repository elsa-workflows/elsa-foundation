# Data model: Weaver recovery

This document describes logical records. EF column types follow per-provider conventions (research R7). Payloads marked *protected* pass through the Secrets codec. Every record except the audit tables belongs to exactly one session aggregate, and every write checks the session fence.

Every table below is the EF store's ([EF README](../../Extensions/Agent/src/Persistence/EntityFrameworkCore/README.md#tables)); the in-memory default holds the same records as JSON. Plain columns are the ones a query, an index or authorization reads without decoding anything; everything else lives inside the row's protected payload. Each row also carries a `SchemaVersion`, and every child row a per-aggregate `Sequence` that keeps first-insertion order.

## Session record (`elsa_agent_sessions`)

| Field | Notes |
| --- | --- |
| `SessionId` | Key. Opaque. Equals the Copilot SDK session id (ADR 0022) |
| `ActorId`, `TenantId` | Owner columns. List, audit and authorization scope on them before any limit and before any payload is decoded |
| `Workspace` | Stamped at create (at most 256 characters). Only narrows lists |
| `Title`, `Status`, `UpdatedAtUtcTicks`, `ExpiresAtUtcTicks` | List columns. Expiry is evaluated at hydration, and the list reports an active session past its expiry as expired |
| `PolicyRevision` | The acknowledged policy's revision. The policy itself is inside the protected session payload; it changes only through the owner (#3440 contract) |
| `ContinuedFrom` | A link to the original session when this one continues past a limit (at most 128 characters) |
| `LimitReached` | The documented limit (`snapshots` or `bytes`) a turn was refused at, so the list reports `recoveryState` without decoding |
| `OwnerToken`, `Fence`, `LeaseExpiresAtUtcTicks` | Work ownership. `Fence` increases monotonically on every takeover. Writes are guarded on owner and fence. The same update extends the lease, which uses database time (research R5) |
| `Version` | Moves on every write and acquisition; that update takes the aggregate's row lock |
| `NextSequence` | The next insertion position of the aggregate's child rows |
| `Payload` (protected) | The whole `AgentSession`, including its policy; each plain column is checked against it on load |
| `PausedTurnPayload` (protected, nullable) | The paused turn: transcript, ordered snapshot digests and the stored `PauseFingerprint` verbatim. Saving it records the Pause claim |
| `PendingAuditsPayload` (protected, nullable) | Audit records a recovery settlement still owes; written with the settlement and cleared once emitted |

## Message record (`elsa_agent_messages`)

Every existing `AgentMessage` member is persisted inside one protected `AgentMessageRecord`, which includes
`HasDisclosureProvenance`, the `AcceptedPolicy` snapshot and its revision, `CreatedAt`, `CompletedAt`, `Source`,
`PromptMode`, `CapabilityId`, `RequestId`, `ClientId`, `ContextAttachmentIds`, content, context and tool results. The
only change to the model is that the inline cumulative disclosure list is replaced by ordered digest references, with
`DeliveryFingerprint` and the history fingerprint stored verbatim and re-verified on hydrate.

| Field | Notes |
| --- | --- |
| `SessionId`, `MessageId` | Key |
| `Role`, `Status`, `StatusReason` | Plain columns, checked against the payload. `StatusReason` distinguishes `interrupted`, `withdrawn` and `user-stop` for Cancelled |
| `Payload` (protected) | The `AgentMessageRecord` described above |

## Disclosure snapshot (`elsa_agent_disclosure_snapshots`)

| Field | Notes |
| --- | --- |
| `SessionId`, `Digest` | Key. `Digest` is SHA-256 over canonical JSON |
| `Size` | Counts toward the 8 MiB limit. The row count counts toward the 64-snapshot limit |
| `Canonical` (protected) | The canonical JSON text, kept byte-identical to what was digested |

## Session context (`elsa_agent_context`)

| Field | Notes |
| --- | --- |
| `SessionId`, `AttachmentKey` | Key. `AttachmentKey` is the SHA-256 of the caller-chosen attachment identity |
| `Payload` (protected) | The `AgentContextAttachment`. Appending one never re-encrypts the session |

## Proposal record (`elsa_agent_proposals`)

Every existing `AgentActionProposal` member is persisted inside one protected `AgentProposalRecord`: the exact
authorized tool request and approval binding, `PolicyRevision`, `ApprovedBy`, `ApprovedAt`, `RequiresApproval`,
`BaseRevision`, `OwnerResolution` (actor, time and claim; a later receipt supersedes it without deleting it), the
retained execution outcome, the result and the accepted acknowledgement digest, written as one aggregate write.

| Field | Notes |
| --- | --- |
| `ProposalId` | Key. It also answers the proposal-to-session lookup a proposal route makes first; the lookup is not owner-scoped, and authorization then runs on the session's owner columns before anything hydrates |
| `SessionId` | The owning session (indexed with `Sequence`) |
| `Status` | The shipped `AgentActionProposalStatus`, checked against the payload |
| `ExpiresAtUtcTicks` | The prepared deadline, so the list counts only reviews that can still be decided |
| `Version` | The record version every transition names (compare-and-set) |
| `Payload` (protected) | The `AgentProposalRecord` described above |

### Unique keys (`elsa_agent_call_keys`)

Keyed by `(SessionId, Kind, KeyHash)`, where `KeyHash` is the SHA-256 of `Key`; `Key` and `Value` are stored as plain
text. `Kind` is one of the following:
- `call` and `invocation`: the value is a ProposalId.
- `turn-mutation`: the value is the call key.
- `call-input`: the value is the input digest (`AgentToolInvoker.cs:175-178`).

All the keys for one preparation are inserted in one transaction.

## Audit (`elsa_agent_audit`, `elsa_agent_audit_owners`, `elsa_agent_audit_sequence`)

`elsa_agent_audit` holds each record by its append position (`Sequence`), with `EventId`, `Kind`, `SessionId`,
`ActorId` and creation time as plain columns and the protected `AgentAuditEvent`. `elsa_agent_audit_owners` is the
first-wins owner association (`SessionId → ActorId, TenantId`) that scopes audit queries, and
`elsa_agent_audit_sequence` is the single counter row that allocates append positions. `settledBy` (`user` or
`recovery`) is a metadata key. Audit is read through `ListOwnedAsync` with owner, order and take. The EF feature
replaces the sink, the reader and ownership together (research R10).

## In-memory only (rebuilt, never stored)

- **Claims:** Prompt, Foreground, Preparation, Proposal and Pause, as reference tokens. Research R4 has the rebuild table.
- **Proposal gates, row monitors and the session write gate.**
- **Turn registry** cancellation sources, and **admission-pass** sets.

## State transitions added by recovery

| Durable state found at hydrate | Recovered outcome |
| --- | --- |
| Message `Streaming`, no paused turn | `Cancelled`, reason `interrupted` |
| User message `Pending` | Stays `Pending`, and the Prompt claim is rebuilt. The owner can start or withdraw it |
| Proposal `AwaitingApproval` or `Approved`, deadline passed | `Expired` once, with zero effect, settled by recovery |
| Proposal `Approved`, deadline not passed | Stays `Approved`. Executes only on an explicit execute when the story 2.3 checks hold |
| Proposal `Executing` | `Uncertain` |
| Proposal `AwaitingClient` past its deadline | `Uncertain`, never `Expired` |
| Paused turn present with a receipt | Pause claim rebuilt. The continuation runs once, on the owner's start |
| Unreadable or mismatched fingerprint | The session is unavailable with a reason, and no effect |
