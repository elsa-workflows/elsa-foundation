# Data model: Weaver recovery

This document describes logical records. EF column types follow per-provider conventions (research R7). Payloads marked *protected* pass through the Secrets codec. Every record belongs to exactly one session aggregate, and every write checks the session fence.

## Session record (`elsa_agent_sessions`)

| Field | Notes |
| --- | --- |
| `SessionId` | Key. Opaque. Equals the Copilot SDK session id (ADR 0022) |
| `ActorId`, `TenantId` | Owner. Used for list, audit and authorization scoping before any limit |
| `Workspace` | Stamped at create. Only narrows lists |
| `Status`, `CreatedAt`, `UpdatedAt`, `ExpiresAt` | Existing lifecycle. Expiry is evaluated when the session is hydrated |
| `Policy` (protected), `PolicyRevision` | Acknowledged policy and revision. Changes only through the owner (#3440 contract) |
| `Context` (protected) | The session context |
| `OwnerToken`, `Fence`, `LeaseExpiresAt` | Work ownership. `Fence` increases monotonically on every takeover. Writes are guarded on owner and fence. The same update extends the lease, which uses database time (research R5) |
| `Version` | Optimistic concurrency token |
| `PausedTurn` (protected, nullable) | Paused transcript plus an ordered list of snapshot digests, and the stored `PauseFingerprint` kept verbatim. Saving it records the Pause claim |
| `ContinuedFrom` (nullable) | A link to the original session when this session was created to continue past a limit |

## Message record (`elsa_agent_messages`)

Every existing `AgentMessage` member is persisted. The full model is stored as one protected
serialized payload, which includes `HasDisclosureProvenance`, the `AcceptedPolicy` snapshot,
`CreatedAt`, `CompletedAt`, `Source`, `PromptMode`, `CapabilityId` and `ContextAttachmentIds`. The
only change to the model is that the inline cumulative disclosure list is replaced by digest
references. The columns below are the indexed or derived ones.

| Field | Notes |
| --- | --- |
| `SessionId`, `MessageId` | Key |
| `Role`, `Status`, `StatusReason` | `Status` is the shipped `AgentMessageStatus`. `StatusReason` distinguishes `interrupted`, `withdrawn` and `user-stop` for Cancelled |
| `Content`, `Context`, `ToolResults` (protected) | As today |
| `DisclosureDigests` | An ordered list of snapshot digests, replacing the inline cumulative list |
| `DeliveryFingerprint`, `DisclosureHistoryFingerprint` | Stored verbatim. Re-verified on hydrate |
| `AcceptedPolicyRevision`, `RequestId`, `ClientId`, `Sequence` | As today |

## Disclosure snapshot (`elsa_agent_disclosure_snapshots`)

| Field | Notes |
| --- | --- |
| `SessionId`, `Digest` | Key. `Digest` is SHA-256 over canonical JSON |
| `Canonical` (protected) | The canonical JSON text, kept byte-identical to what was digested |
| `Size` | Counts toward the 8 MiB limit. The row count counts toward the 64-snapshot limit |

## Proposal record (`elsa_agent_proposals`)

Every existing `AgentActionProposal` member is persisted as one protected serialized payload. That
includes `ApprovedBy`, `ApprovedAt`, `RequiresApproval` and `BaseRevision`, which approval
idempotency and `RevisionMatches` use. The columns below are the indexed or derived ones. A
`ProposalId → SessionId` index, scoped by owner, lets a proposal route hydrate its session first.

| Field | Notes |
| --- | --- |
| `ProposalId` | Key. Belongs to `SessionId` and `TurnId` |
| `Status` | The shipped `AgentActionProposalStatus` |
| `Invocation` (protected) | The exact authorized tool request, approval binding, target and arguments |
| `PolicyRevision`, `ExpiresAt`, `Dispatched` | The approval basis, used by the story 2.3 recheck |
| `Execution`, `Result` (protected), `AcknowledgementDigest` | Written as one aggregate write together with `Status` |
| `OwnerResolution` (nullable) | The owner's statement: actor, time and claim (applied, not applied or unknown). A later receipt supersedes it without deleting it |

### Unique keys (`elsa_agent_call_keys`)

`(SessionId, Kind, Key) → Value`, where `Kind` is one of the following:
- `call` and `invocation`: the value is a ProposalId.
- `turn-mutation`: the value is the call key.
- `call-input`: the value is the input digest (`AgentToolInvoker.cs:175-178`).

 All the keys for one preparation are inserted in one transaction.

## Audit record (`elsa_agent_audit`)

This is the existing audit event plus a persisted owner association (`SessionId → ActorId, TenantId`). `settledBy` (`user` or `recovery`) is a metadata key. Audit is read through `ListOwnedAsync` with owner, order and take. The EF feature replaces the sink, the reader and ownership together (research R10).

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
