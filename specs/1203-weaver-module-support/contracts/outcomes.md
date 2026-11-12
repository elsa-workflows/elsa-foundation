# Proposed contract: outcomes, module codes and durable representation

**Current naming and upgrade baseline (12 November 2026)**: the owner-authorized [#4027 rename](../../../docs/reference/package-renames.md) moves the Studio assistant to `Elsa.Weaver.*` and `api.weaver`; user-defined agents keep `Elsa.Agent.*`. This is an identifier/input reconciliation, not a scope or acceptance waiver. The rename starts a fresh Weaver store. The additive upgrade in this work unit is post-rename, pre-effects Weaver1.0 → Weaver1.1, with genuine actual-writer evidence. Pre-rename Agent-store captures remain unchanged historical evidence and cannot qualify that upgrade. All legacy digest, protected-content, counter-lifetime, hold/finalization and no-replay requirements apply within the Weaver family. Contracts remain Preview until the required composed and independent-package journeys pass.

**Status**: Proposed Preview for leaf0, not frozen.
**Authority**: spec1203 FR014/016/018/023 and story1. Prior recovery meanings: [1189](../../1189-weaver-recovery/contracts/recovery.md).

## Additive members

Preserve positional constructors and every existing enum ordinal.

| Surface | Addition/default |
| --- | --- |
| WeaverActionProposalStatus | Append Performed =12 |
| WeaverEffectScope | Append NoPersistedEffect =2; existing ClientWorkingState0 and ServerPersisted1 remain |
| WeaverToolDescriptor | CanPerformWithoutPersistence bool, false; omit false |
| Tool and context descriptors | ModuleCodes nullable read-only collection of strings; null is undeclared compatibility, omitted; empty is a declared closed set accepting no code |
| WeaverEffectReceipt | ModuleCode nullable string, omitted when null |
| WeaverError | OwnerUnavailable bool, false/omitted; public value alone is not provenance |

Prepared class members and defaults are defined in [privilege-policy.md](privilege-policy.md). No principal is a model/persistence member. Use existing WeaverProposalExecutionResult.Status and Executed; no second outcome enum is added.

Code declarations are copied/validated at catalog admission. Codes are bounded dotted ASCII identifiers, outside reserved Weaver namespace, max128characters, ordinal membership, no blank/duplicate declaration entries. Every new M4 tool/source declares a set. Module packages map native codes and safe text into it; core never interprets native code meanings.

## Successful result validation

Both the default executor and proposal service's custom-executor boundary use the actual admitted descriptor and existing exact invocation/tool/target/evidence checks. Capture that descriptor before execution; do not re-run post-effect authority and call a possible effect refused.

| Location/result | Required receipt/declaration | Stored status |
| --- | --- | --- |
| Server persisted | Executed=true, Status executed, successful genuine owner result, ServerPersisted scope, exact before target and module-owned after evidence | Executed6 |
| Server performed | Executed=true, Status performed, descriptor declaration true, successful genuine owner result, NoPersistedEffect scope, unchanged before/after identity and revision, defined owner evidence | Performed12 |
| Client acknowledged | Existing exact client binding/ACK protocol, Status executed and ClientWorkingState scope | Executed6, applied locally only |
| Wrong scope/status/declaration/correlation/target/evidence after possible dispatch | Success is not accepted | Existing Uncertain11/no replay |
| Refused before effect | Existing refusal/error lifecycle; no invented successful receipt | Existing refusal/Failed |
| Partial effect, throw after possible dispatch, lost response after possible effect, or unconfirmed receipt persistence | No fabricated zero-effect assertion | Existing uncertainty/no replay |

A provably undispatched throw follows the existing before-effect error/refusal path, not an invented uncertain effect. Once execution is handed off, an exception remains potentially effectful unless an engine-owned pre-dispatch signal establishes otherwise; arbitrary module/custom-executor claims are not that signal.

Performed means the approved action ran without an owning-resource persisted change, not that it succeeded at its business test or made no external call. A test that ran and found failure can still be performed; its verdict is module data. A missing/inactive target or a test that could not run is refused. A connection test that records time/version is persisted.

Keep pending/terminal/idempotency/claim-release/recovery mappings complete. Performed is terminal and cannot remain busy or be coerced to executed/failed by a generic client fallback. Retained genuine receipts survive duplicate delivery and policy change unchanged. Existing ProposalExecuted audit kind can describe ran with accurate performed/scope metadata; no extra audit enum is needed.

## Classify trusted origin, then normalize

Use one small internal source-bound normalizer and private provenance like the existing context recorded-instance mechanism. It is not a public authorization registry.

1. Identify actual installed-owner origin at preparation, execution, same tool authorization, and installed context collection/retained disclosure under its own descriptor. Strip host/event/custom-executor spoofed markers.
2. For a declaring source, preserve a genuine owner-unavailable semantic before inspecting raw code syntax. Replace an out-of-set/malformed/reserved code and its message with safe fixed generic values. Invalid syntax must not turn genuine unavailability into forbidden.
3. Ordinary replacement code is module.code.unsupported; genuine-unavailable replacement is module.owner.unavailable, outside Weaver namespace. Use fixed engine text, including existing OwnerUnavailableMessage. Preserve original refusal/result/diagnostic classification and status.
4. Normalize typed Code, Error/Violation/Diagnostic.Message, ToolResult.Content and execution Summary before freeze, persistence, HTTP/SSE, provider, review or audit. Never log raw unknown codes/messages.
5. Forward the normalized WeaverError object rather than reconstructing Failure(code,message,status) and dropping metadata/provenance. Arbitrary deserialized flags do not become fresh owner authority.
6. Undeclared sources retain explicit legacy pass-through/dotted-owner-code behavior. Engine-generated errors remain engine errors, not module-set inputs.

Opaque module JSON is not parsed for native messages or codes by Weaver. Module-owned mapping/allowlists and the existing disclosure owner must keep it safe. Generic replacement does not authorize unsafe opaque data, discard a genuine resource receipt wholesale, or automatically change its outcome to uncertain. The generic canary fixture supplies safe owner payloads and canaries in the typed channels; module cases separately prove their opaque projections.

## Weaver1.1 data

Store new classes, receipt module code and performed status/scope in existing records; no relational schema change. Add1.1.0 to the existing Weaver family with identity1.0→1.1 content upcast. Authenticate/unprotect before defaults; never decrypt/re-encrypt inside the upcaster. Preserve purpose strings and disclosure canonical bytes.

Read all declared columns of a protected row through the chain together. Contentless snapshot/key/owner/counter records still require readable stamps. Audit owner association/list and owned session/proposal-count projections must not bypass readability validation.

Plain owner/identity lookups precede authorization and remain minimal. Apply exact actor/tenant/workspace filtering before validating owned full rows/projections. A foreign unreadable record receives only shared403/404, with no hydration or settlement.

## Writes and retries

Select admitted WriteVersion from the existing context's module gate inside each metadata attempt, not from the latest assembly constant. Validate representability before writes. Supported unadmitted custom contexts can write only legacy1.0 content; unknown/unreadable family is never a legacy fallback. New feature admission remains required.

Retain the initial raw owner/fence-guarded row-lock/version increment. Load the tracked session after that lock; apply optional session/paused/pending-audit changes, lease/sequence and selected stamp, then SaveChanges with children in the same transaction through the existing interceptor. This replaces the raw content ExecuteUpdate, not the existing owner/fence mechanism. Retain exact version-conflict checks; do not increment the version twice. Paused/pending-only writes are content writes too.

Move encoding/stamp-bearing row materialization inside each safe attempt. On a verified forward gate-adoption mismatch only, roll back/dispose and use existing bounded metadata retry. Re-select and rebuild entities; do not reuse tracked objects. Do not retry arbitrary schema refusal, corruption or owner/domain dispatch. If receipt persistence cannot settle, preserve the existing uncertain/no-replay result.

An existing audit sequence counter1.0 remains readable for as long as that counter exists and never restamps when Last advances. The [Program obligation](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6671508360) records that retained lifetime, including after finalization. New counters/owners/audits use that attempt's admitted stamp. Raw lease/fence-only bookkeeping may preserve a stamp but cannot smuggle content past the shared guard.

## Complete mapping inventory

- Core status/scope/models; actual descriptor declarations; default executor and custom StoreOutcome integrity; terminal/settled/idempotency/claim release; recovery and accurate audit summaries.
- API contract strings, bootstrap privileges, real source-generated HTTP/SSE graphs and default protected-record serialization.
- SDK scope/preparation/receipt shapes; agentTypes; agentClient status/result parsing and executed fallback; useWeaverSession storage/busy/status parsing; review/results and module renderer fallback.
- Numeric proposal statuses must match actual C#0–12, coordinating #3761, rather than extend the existing incorrect fallback. Test every ordinal and supported wire spelling.
- Probe receipt normalization/validation and dedicated owner-backed performed cases on both actual adapters. Keep all original/current-authority/autonomy/workflow/restart inventories.

## SemVer and freeze

The approved spec classifies performed semantics and the default administrative veto as Major. Optional declarations/error support/receipt member and Weaver1.1 expansion are Minor; later exact3779 native cleanup is Patch. Keep the existing computed pre-GA4.0 preview line under ADR0080D4/E5; no project version or new magnitude engine.

Record the manual public API/behavior/binary compatibility assessment and migration guidance. An unbuilt magnitude gate is not evidence of compatibility. No release, release tag, feed publish or line change is part of this plan. Freeze only after current composed/backend and independent immutable-package journeys demonstrate these surfaces.
