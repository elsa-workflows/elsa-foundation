# Proposed API and packaged CLI contract

Status: proposed operation/wire contract. Names below are design targets, not shipped commands or current capability claims.
Use the [promotion contract](promotion.md) for effect/recovery semantics and [bundle contract](release-bundle.md) for content.

## Capability and scope

Advertise versioned release relations through the existing capability discovery mechanism: bundle formats/profiles/limits,
create/export/import, target identity/prerequisites, preview/apply/status/resume/stop/rollback/history and owner-kind support.
A target advertises each native/BPMN/ETL import/catalog/activation role only when its actual composed providers support it.
Missing support is a named unavailable capability, never guessed from feature/package names. Discovery is not authorization.

Every operation is backend/tenant qualified. Derive tenant from authenticated context, compare verified target authority and
apply the existing workflow publication/design/resource permission conventions. Read-only status cannot authorize effects.
Creation/export requires source read/export authority; import/apply/resume/stop and evidence writes use appropriate target
management authority. No private authentication store or source-to-target credential forwarding is introduced.

## Operations

| Operation | Input | Result / effects |
|---|---|---|
| Create | Exact typed roots and desired root roles; separate display metadata | Freeze complete manifest/blobs under source retention; expose release only after durable capture. |
| Export | Exact retained release identity | Original versioned bundle, manifest and verified blobs; no rebuild/latest lookup. |
| Import | Bounded portable bundle and optional display metadata | Validate and retain exact target-local release archive. Does not materialize owner catalogs or activate roots. |
| Inspect | Release identity | Immutable manifest, owner-specific roots/versions, closure and separately attached evidence. |
| Preview | Exact release digest, target identity, scoped mapping choices, action and selected slots/roles | Persist an observed fixed preview ID/fingerprint, intended changes, blockers and evidence/readiness observations. No owner staging or serving changes. |
| Apply / stage | Preview identity/fingerprint, exact subject/context and target-scoped idempotency key | Revalidate; bind immutable intent to a durable attempt. Stage only or coordinate root effects according to requested action. |
| Get / wait | Attempt ID and bounded observation options | Durable current attempt/member observations. A timeout is incomplete observation, not failed deployment. |
| Resume | Existing attempt, current actor authorization | Reconcile receipts, advance still-ready command fences and continue the same safe immutable intent. Never refresh expected revisions. |
| Stop | Existing attempt, current actor authorization | Stop new dispatch; reconcile/revoke planned owner commands. Report remaining effects/uncertainty. Does not roll back. |
| Rollback | Selected retained release/context, fresh preview and new idempotency key | New normal attempt through owners; current prerequisites and conflicts apply. |
| History | Target/release filters with existing bounded pagination | Original/resuming actor, target, subject/context, root/member outcomes, evidence and diagnostics. |
| Attach evidence | Versioned exact-subject report reference/digest and coverage | Verify association and append it separately. Neither attachment nor upload implies trust, passed checks or whole-release coverage. |

Import may retain an archive even when target prerequisites block later application; report content validity separately from
current target readiness. Preflight validates all owner prerequisites without running the authoring compiler. Server-owned
preview records bind actual observations; client-supplied expected values cannot manufacture a valid preview. Expired/missing
preview means refresh is required. A refresh is another explicit preview, never silent apply-plan substitution.

API apply accepts/returns a durable attempt identity before the client begins observing effects; response loss is recoverable
using its idempotency key and unchanged request fingerprint. A client must not mint a new key after an uncertain response.
Use existing request/trace and problem-detail conventions, stable machine codes and located member/requirement diagnostics.
HTTP transport success is distinct from attempt success; accepted/recovering work must not be flattened to a success boolean.

## Pipeline result

Versioned JSON includes operation, request/trace ID, verified target authority/tenant, release/context/preview/attempt identity,
requested goal, phase/outcome, explicit root roles, member states/receipts/observed revisions, residual effects, blockers,
warnings, evidence coverage/mode/verdict and next permitted action. Include safe source locations and retryability without
connection settings, secret values, tokens or foreign-tenant existence. Preserve unknown fields only where the versioned
extension contract expressly permits them; unknown required semantic versions fail visibly.

`Active` means the plan's role-specific effects completed: serving roots active, ETL catalog roots available and dependencies
staged/retained. `Staged` is success only for an explicitly requested staging goal. No-op requires full content/context/selection
match and fresh validation. Failed, partial, recovering and superseded remain distinct even if some desired content is present.

## CLI shape and exits

Extend `dotnet elsa` with a release command family calling these same HTTP operations; it does not enter the schema-migration
worker, reimplement validation or compile workflow content. Target authentication uses normal configured pipeline identity;
secret values are not accepted as bundle/mapping/report fields. Support JSON output, bounded wait/deadline and cancellation.
Exact command syntax/help and runnable scripts are produced with implementation, tested from the packed tool in a clean consumer.

Proposed exit mapping for action/wait commands:

| Exit | Meaning |
|---|---|
| 0 | Requested goal confirmed achieved or true no-op. |
| 1 | Unexpected client/internal error; output preserves known attempt identity and uncertainty. |
| 2 | Invalid input or unsupported/malformed bundle/contract. |
| 3 | Authentication/authorization/tenant/target-scope refusal. |
| 4 | Readiness/stale/concurrent/ownership conflict prevented the requested effect. |
| 5 | Confirmed failed attempt with no remaining own serving effect. |
| 6 | Partial, recovering, unavailable or timed-out observation; effects may be incomplete/unknown. |
| 7 | Confirmed superseded intent; output lists any remaining own effects. |

Read/inspect/history commands return 0 for successful retrieval even when the retrieved attempt failed; `wait`/apply return
according to the requested goal's outcome. Preview returns 4 when blockers prevent apply. A post-dispatch transport failure
returns 6 with the original key/known attempt and recovery instructions, never a false confirmed failure. Distinct JSON codes
separate unavailable, partial and unknown observations sharing exit 6. Ctrl+C stops observation/new client dispatch; it does not
prove target work was cancelled. Retry/resume/stop use the durable attempt and current authorization.
