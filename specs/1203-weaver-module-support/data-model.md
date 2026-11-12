# Data model: generic effects

**Status**: Proposed Preview; storage placement for PD2. Signature and validation authority: [privilege policy](contracts/privilege-policy.md), [outcomes](contracts/outcomes.md), [owner operations](contracts/owner-operations.md).

## Generic fields

| Model | Field and rule | Lifetime |
| --- | --- | --- |
| AgentToolPreparation | RequiredPrivilegeClasses nullable immutable list of three known enum values; canonical empty is null | Owner preparation, then copied into frozen invocation |
| AgentPreparedToolInvocation | Same canonical class list; nonempty participates in digest; legacy empty retains exact prior hash shape | Existing proposal protected payload, paused/retained turn graphs and API/SSE |
| AgentToolDescriptor | CanPerformWithoutPersistence false by default; optional ModuleCodes closed set | Installed request-scoped discovery, never inferred from name |
| AgentContextProviderDescriptor | Optional ModuleCodes closed set | Installed context source descriptor |
| AgentEffectReceipt | ModuleCode nullable, omitted; NoPersistedEffect scope 2 requires unchanged owning identity/revision and actual ran evidence | Existing proposal/execution result and audit payload graphs |
| AgentActionProposal | Performed status 12 appended; terminal | Existing proposal plain Status plus protected payload; session/result graphs |
| AgentError | OwnerUnavailable false/omitted; trusted origin is separate private provenance | Normalized error transport; persisted where an existing result graph holds it |
| AgentPrivilegePolicyOptions | Administrative, Irrecoverable, Activation, all false | Live host configuration, never a session grant |
| AgentBootstrapResponse | Read-only three-bool Privileges snapshot | Current HTTP response only; client display |

Classes are a set, codes are bounded ordinal identifiers, and optional values use explicit null/false omission. Existing positional constructors, enum ordinals and purpose strings stay intact. Opaque module data remains module-owned and allowlisted; these generic fields do not authorize raw secret/settings values.

## Ephemeral authority and discovery

- `IAgentPrincipalSource` is scoped and returns the genuine current principal for exact actor/tenant. Never serialize/cache a principal in a session, proposal, audit, digest, provider context or prepared arguments.
- Selected feature metadata holds actual Type and its direct static requirements. Existing private store-selection metadata adds the required variation Type. It is immutable composition metadata, not stored session state.
- Shared schema availability, current options and owning permission checks are observations at each required boundary. They are not a distributed revocation fence.
- Support inventory holds bounded module identity/display/status/support facts with side/host/generation/observation provenance. Browser observations are client-asserted. Failed/incomplete discovery remains unknown; it is never successful absence. No feed credentials, entry URLs, paths or raw exception text enter the projection.
- Module-code trusted provenance is private source-bound metadata. Deserialization of OwnerUnavailable does not recreate owner authority.

## Existing Agent EF placement

| Existing row | New-data/read obligation |
| --- | --- |
| Session | Payload, PausedTurnPayload and PendingAuditsPayload upcast together before individual unprotect/decode; optional class/result fields may be nested there |
| Proposal | Status plain column can hold 12; protected Payload carries frozen classes and result/receipt; cross-check plain/projected fields as today |
| Message / Context / Audit | Existing protected Payload graphs carry normalized result/code data where already referenced; no parallel record collection |
| Disclosure snapshot | Canonical bytes/digest are integrity data, never rewritten by upcast; stamp remains readable |
| Unique key | Existing duplicate/ownership fence semantics, readable stamp |
| Audit owner / counter | Readable stamp despite no protected content; existing counter 1.0 never restamps as Last advances |

Agent 1.1.0 is an additive content-chain step. Its upcaster returns protected content unchanged; authenticated deserialization supplies missing defaults. Every old row kind remains readable. New stamp selection is per actual metadata attempt from the existing context gate; old representable writes can remain 1.0 while held. Unknown/unreadable stamps cannot fall back to 1.0. New content cannot be written through a raw content update that bypasses the interceptor.

Minimal plain owner/proposal-session lookups remain before authorization. Validate owned rows and summary/count projections only after exact actor/tenant/workspace filtering. Foreign unreadable data yields shared 403/404 without hydration or settlement.

## State transitions and outcome retention

The prior pending/review/approved/executing/client-ACK/recovery lifecycle remains authoritative. Add one successful terminal branch: dispatched server action + declared performed support + genuine NoPersistedEffect receipt → Performed. It releases ownership/busy state, retains the exact receipt, and is idempotent. A test verdict may be negative even though the test ran; module data conveys that verdict.

Before dispatch, missing privilege/principal/owner authority/schema admission or stale expected target refuses with zero new effects. After possible dispatch, a throw, partial operation, wrong success receipt or unconfirmed retention is uncertain; no automatic replay. A later policy change cannot overwrite a genuinely accepted terminal receipt. Recovered approvals require a new explicit authenticated execute and current owner checks.

## First owner leaves

Secrets keeps its existing secret Id, opaque repository revision and explicit selected version. Connections adds no invented identity: expected revision is existing IncarnationId plus Version, refusing unknown legacy identity for new revisioned operations. Owner command/read models live in the corresponding Core, authorization implementations in Api. Connection deletion additionally freezes the exact bounded cleanup observation in its approved arguments, compares it before effect and never expands it to new secrets discovered after review. Consumers are a Secrets-owned fan-in with per-source bounded observations and declared disclosure rules. A protected observation token may carry private comparison facts, never a principal or secret value; generic review sees only authorized names or counts.

No new database, relational migration, session owner, schema fleet, domain registry or provider-specific model is introduced by leaf 0.
