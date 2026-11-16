# Research and decisions

Source baseline: verified main `c0b47ceb19c0123a2a75f1475c3b2595ec3506e3`, tree
`6585d28ae9b395fbd0266fe4fa3d27579719d950`. Research is source analysis, not runtime proof.
Independent bounded investigations covered native automatic qualification and admission ownership;
their source hashes and reports are retained with the owning issue's delivery evidence.

## 1. Reuse actual execution; prove a separate automatic operation

**Decision**: Add a distinct module-owned personal Acme card size operation. Reuse existing native
Home authorization, CAS and independent readback. Keep reviewed visibility/workflow tools unchanged.

**Rationale**: The external Studio module already declares S/M support and native Home normalizes
size to small/medium. This changes one reversible personal cosmetic field without hiding a widget,
editing defaults or affecting other entries. Existing `AgentToolInvoker` distinguishes module-required
review from session policy. There is no LowRisk enum; a mutation must not be labelled read-only.

**Alternatives considered**: Weakening visibility's prepared review requirement invalidates the
control. A fake effect or extra generic JSON mutation callback proves no owner semantics. A new
production resource is unnecessary. Use separate strict size input/result schemas and renderer;
do not widen visibility payloads to accommodate a different operation.

**Source**: `tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio/Client/src/module.tsx`,
`Resource.Backend/WidgetResource.cs.in`, `WidgetTools.cs.in`; native
`Essentials/src/Studio/Preferences/Core/StudioHomeLayout.cs`;
`Extensions/Agent/src/Elsa.Agent/Services/AgentToolInvoker.cs`.

## 2. Acknowledge policy in the same conversation

**Decision**: Add an autonomy-only acknowledged operation/readback to the existing session owner;
preserve all unrelated policy and conversation state. Attribute accepted deliveries/results to their
accepted effective policy. Use a conditional generation and/or retained intent only as demanded by
lost-response/concurrent-update tests; an old retry cannot overwrite a newer acknowledged choice.

**Rationale**: `useWeaverSession` currently changes a local dropdown and passes it only to creation;
`ensureSession` then reuses that ID. Its prompt-result attribution reads the latest dropdown.
Creation and session-detail responses omit effective-policy readback. Static `AgentPolicy.Default`
is the current deployment default, not a discovered dynamic administrator-policy service. Never
replace an existing restrictive custom policy with defaults during an autonomy-only command.

**Alternatives considered**: Silent new conversations/copying transcript destroys identity and hides
disagreement. Unconditional set/retry can revert a newer choice. Client disabling alone provides no
server admission order. The first causal native baseline must observe the actual wrong auto/review
behavior, not merely a missing update endpoint.

## 3. Order lifecycle ownership under the existing session boundary

**Decision**: Prefer short per-session reservations and explicit ownership handoffs. The owner returns
the current accepted policy; collection/provider/module work occurs outside the monitor. Preserve
existing message duplicate/pending/insertion serialization, foreground cancellation and retained pause
integrity. Extend only the public owner surface that real consumers/replacements require.

**Rationale**: API collection awaits between initial checks and insertion. Stream session lookup
precedes foreground registration, and terminal message status precedes lease release. Direct invoker
consumers also await registry/authority/preparation before proposal creation. Reviewed/approved/client
pending/unconfirmed operations and receipt continuation remain owned after a foreground lease ends.
`Uncertain` being generally terminal does not establish that a late applicable ACK is impossible.

**Lock order**: Keep call-key -> proposal gate -> short session transition where combined. Never hold
the session monitor while awaiting those gates, scoped delegates, audit, replacement-store or I/O.
Avoid inverse nesting with proposal insertion; identifiable reservations bridge owner commits. If a
separate session monitor is needed, only session -> message-list nesting is allowed. Cancellation
callbacks/disposal stay outside monitors. Cleanup releases only the original ownership token.

**Alternatives considered**: Conditional commit/recollection under a checked policy generation is a
fallback only if causally smaller; it must still cover foreground/proposal ownership. Reject a second
busy flag, check-then-set, proposal scans under a session lock, raw public lock callbacks and default
shadow coordinators for unsupported replacements. Qualify a participating replacement and explicit
unsupported control before freezing the owner extension.

**Source**: Agent `DefaultAgentServices.cs`, `AgentTurnServices.cs`, `AgentToolInvoker.cs`,
`AgentProposalServices.cs`, Core `AgentContracts.cs`/`AgentTurnContracts.cs` and registration.

## 4. Query audit through its owning boundary; distinguish expiry

**Decision**: Keep trusted existing audit reads, and add the smallest supported owner-bounded query
for authenticated HTTP consumers. Filter actor/tenant/order before limit in the reader; no global
read/per-event session-lookup fallback. Missing legacy ownership is ineligible. Serialize correlated
expiry transitions with original proposal/invocation/turn/call identity.

**Rationale**: The current API already filters owner before Take; no disclosure leak is established.
Its reader cannot express scope. A canonical SessionCreated ownership association/index can avoid a
session -> sink -> session DI cycle in defaults with immutable actor/tenant ownership. Association,
append and indexing must be coherent; conflicting association refuses. A replacement with mutable
ownership must maintain current authorization coherently or refuse scoped queries. Optional action
ActorId is not audience ownership. Stamping every emitter is an alternative with omission risk.

Approval/execute pre-dispatch expiry currently changes status without correlated transition audit;
post-dispatch deadline reconciliation must retain Uncertain and any genuine late receipt. Fake clocks
and serialized repeated calls establish zero pre-dispatch effect and no duplicate transition.

## 5. Keep full qualification and truthful evidence categories

**Decision**: Extend the existing qualifier with a mandatory separate autonomy inventory. Preserve all
original38 cases and four current-authority cases, strict source/input/archive/export proof, no-source/
frontend consumer constraints and native cleanup. Add generic exact native-SSE correlation for inline
automatic receipts; never issue an extra execute merely to obtain evidence for a checker.

**Rationale**: The current `Runtime.turn()` creates a new auto-read-only session, so cannot prove
same-session changes. Current receipt correlation expects execute/ACK transport, absent for a genuine
inline effect. Generic protocol fixtures already accept advertised arbitrary tools; no Acme cases or
provider adapter changes are needed. Wrong/missing/truncated/orphan/duplicate native receipt controls
must fail qualification. Store raw failed baseline evidence separately from final passing witnesses.

## Empirical decision gates

No scope/authority clarification remains. Runtime experiments deliberately precede contract freeze:
native size normalization/grid readback; both real adapters' same-conversation controls/mismatches;
short ownership/cleanup handoffs; lost response then newer update then old retry; unresolved ACK
eligibility; participating replacements. The plan/tasks must capture actual observations and adjust
only the smallest necessary candidate contract. None of these gates is satisfied by this research.
