# Proposed Studio release and promotion acceptance

**Status: Proposed.** This document defines intended behavior and required evidence for [#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590).
No release screen, screenshot, composed journey or product acceptance is claimed by this design.
The [feature spec](../spec.md), [bundle](release-bundle.md) and [promotion](promotion.md) contracts govern semantics.

## User goal and references

An author selects exact native, BPMN and ETL content with supported required dependencies, creates one immutable release,
and inspects/downloads its manifest. An operator previews and explicitly applies that same release to an authenticated
target with its own mappings, follows durable outcomes, and explicitly rolls back after fresh validation.
Running work retains its exact content/dependencies and captured mapping identities; ordinary resource/secret rotation remains effective.

Use [Workflow Definitions](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx)
and Connections as collection references, and existing publication review for change/conflict presentation.
Compose pages, tables, dialogs, tabs and status messages with [shared module controls](../../../Studio/docs/module-ui.md).
Apply the [Studio UX acceptance](../../../docs/contributing/studio-ux-acceptance.md) protocol before coding and during review.
Release inspection links to existing owner screens; it does not introduce an editable copy of the content catalogs.

## Exact selected roots and model behavior

| Selected content | Selection and target outcome |
|---|---|
| Native workflow | Exact published definition/version and executable association; explicitly selected serving roots use existing Runtime activation slots. |
| Ordinary BPMN workflow | Exact workflow version plus its BPMN/model material and cross-model dependencies; use the owning ordinary workflow/publication path. |
| Direct BPMN process | Exact published process/model identity through the existing [BPMN catalog](../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/BpmnWorkflowsPage.tsx); serving activation requires the reviewed owner bridge consistent with ADR 0043. |
| Standalone ETL pipeline | Exact published pipeline integer version/artifact through [ETL definitions](../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/EtlDefinitionsPage.tsx); make it available for normal exact `RunEtl` selection/invocation. Do not invent a direct-start or latest-serving ETL selector. |

Show selected roots separately from required dependency members and explicit per-root activate-serving/make-available intent.
Retain owner-specific version encoding and complete identity; a label, recommendation or newest version is not an exact selection.
Dependency-only workflow/model content acquires no independent endpoint, schedule or slot ownership by being imported.
Missing model adapters, unsupported content or unavailable activation bridges are visible blockers during development;
refusal does not satisfy the owner's requirement to deliver all three model families in the first complete release capability.

## Source and target connection lifetimes

Reuse [Backends](../../../Studio/src/essentials/Elsa.Studio.Backends/README.md) profiles and the shared SDK connection/authentication mechanism.
Keep source and prepared target sessions separately authenticated and visibly named, including verified authority/environment and tenant.
Promotion target selection must not globally switch the source editor, discard unsaved work, retarget the Studio host bridge,
reuse another backend's tokens, or silently substitute the default/last-selected backend after reload.
Extend shared prepared-session support where necessary; do not create private authentication or an arbitrary remote proxy.
Changed backend identity, tenant, target authority or expired session invalidates pending target actions and stale preview data.
Configured profiles remain source-owned; modifying profile metadata does not alter a retained deployment's target identity.
Persisted release/attempt links remain qualified by backend; a missing profile produces a recoverable selection state.

## Proposed journey and observable states

1. **Create:** from Releases or an owner collection, choose exact supported roots; inspect required closure and prerequisites.
   Show loading, empty, no matches, unauthorized/read-only source, missing/corrupt/unsupported dependency and refused identity states.
   Creation succeeds only after durable full capture; show the immutable digest, selected roots and download action.
2. **Inspect:** separate Content, Evidence and Deployment history. Content is read-only; distinguish evidence mode, subject,
   covered roots/occurrences, verdict and incomplete/stale status. Failed or subset evidence never becomes whole-release success.
3. **Prepare target:** show authenticated target identity/tenant and discovered capabilities; map located logical requirements
   to metadata-only target resource identities/incarnations. Same names in distinct roots/reusable placements remain distinct.
   Show missing/wrong type, denied, deleted/recreated, unavailable/dormant capability and connection authorization states.
4. **Review:** separate immutable additions/conflicts, serving changes, binding changes, trigger conflicts and prerequisites.
   Display no-op accurately, readiness observations and evidence limits. Preview reserves nothing; changing target/mappings invalidates it.
5. **Apply:** require an explicit action naming target and release. Disable duplicate submission while observing the same durable attempt.
   Use API-issued attempt identities/idempotency semantics; a timeout or cancelled observation is not proof of a failed deployment.
6. **Follow/reopen:** show staged, applying, active/available, no-op, failed, partial, recovering and superseded outcomes with
   per-owner/member facts, residual effects and next actions. A mixed activation interval is visible; no group transaction is promised.
   Reopening retrieves durable target state. Resume/recovery actions follow the promotion authorization contract.
7. **Rollback:** select retained content and reviewed mappings, obtain a fresh target preview and explicitly apply through the same owners.
   Explain blockers without promising to reverse running executions, schema changes or external effects. Recovery does not auto-rollback.

Permission loss clears private cached details and prevents new actions; never render stale authorized data after a denied refresh.
Create/mapping cancellation restores focus and preserves the previous usable state. Navigation guards cover unsaved choices and pending writes.
All changed controls have accessible names, keyboard operation, visible focus and understandable validation/recovery messages.
Narrow and theme layouts preserve target identity, primary actions, member outcomes and readable change tables.

## Current-commit composed evidence matrix

Run against a real composed Studio host and two separately owned disposable target hosts/stores with different resource bindings.
Use the same retained release digest/blobs throughout; record source commit, tested commit, PR head and packaged CLI version.
The feature-specific journeys extend the existing acceptance runner; unrelated existing journeys are not release evidence.

| Witness | Required inspected result/artifacts |
|---|---|
| Create and transfer | Select native, ordinary/direct BPMN and standalone ETL roots with shared reusable dependencies; download/reimport/reopen; unchanged identities, complete closure and model-specific outcomes. |
| Two targets | Actual Studio apply and packaged CLI/API apply of identical release content to both targets with different mappings; target identities, payload digests, previews and owner receipts retained. |
| Validation/access | Missing/incompatible dependencies/mappings, unsupported capability, denied/cross-tenant access, identity replacement and source-owned/read-only inspection; no hidden content/secret disclosure. |
| Drift/retry | Stale preview, concurrent/external writer, binding-only change, no-op, lost response and identical retry; visible accurate effects and machine-readable API/CLI equivalence. |
| Partial/recovery | Interrupt a composed host before/after owner commit; reopen and recover forward, observe mixed/partial/superseded members and authorized next actions without false success. |
| Pins/rollback | Real waiting/progressing native/BPMN/ETL work across activation/rebinding/rollback; preserved exact dependencies/mapping choices, ordinary rotation and intended subsequent-start behavior. |
| Experience | Empty/populated/error/loading, save/reopen and cancellation; keyboard/tab/focus/validation; relevant light/dark themes and narrow viewport captures of changed surfaces. |

Retain current-commit screenshots, accessibility snapshots, traces, API/CLI reports and per-step outcomes in downloadable evidence.
An independent reviewer opens the screenshots/traces and inspects the complete affected experience, recording the exact head,
tested commit, Actions run/artifact, observed states, findings/retests and exclusions using the existing UX receipt format.
Passed assertions alone are not visual acceptance. Failed, skipped, cancelled, unavailable and incomplete witnesses remain explicit;
fix/retest affected whole journeys and refresh evidence before claiming acceptance or resulting-main completion.
