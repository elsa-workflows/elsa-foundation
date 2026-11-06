# Standalone triggers: Studio acceptance

**Feature:** [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582)  
**Status:** Pre-code acceptance plan. No journey has been run or accepted.  
**Source review:** `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`, 5 November 2026.

This record applies the existing [Studio UX acceptance process](../../docs/contributing/studio-ux-acceptance.md). It defines the experience to inspect and the evidence to retain. The feature's reviewed contracts own delivery guarantees, input semantics and lifecycle policy; this record does not ratify an unresolved contract or substitute for backend qualification.

## User goal and references

An operator creates an End of business day schedule, subscribes Reconcile Accounts and Generate Reports, maps the schedule data to each workflow's declared inputs, and activates the entry point. They can change the schedule and pause new starts without editing either business graph. A workflow author can open Reconcile Accounts and associate an additional Event or HTTP trigger through the workflow editor. Both people see the same persisted associations and can understand which starts were accepted, dispatched, completed or failed.

An integrator deliberately selects either HTTP workflow-owned response or asynchronous fan-out. They can see who owns the response, what an acknowledgement means, and where to investigate a failed or incomplete delivery.

Use [Workflow Definitions](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages.tsx) and [Connections](../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage.tsx) as collection references. Use the public components and interaction guidance in [Building a module UI](../../Studio/docs/module-ui.md), the [Studio UX design spec](../../Studio/docs/ux/design-spec.md), and the [design token contract](../../Studio/docs/design-tokens.md). A shared control supplies presentation; backend permissions and resource revisions still govern the action.

## Surfaces and observable outcomes

| Surface | What a person must be able to do and understand |
|---|---|
| Trigger collection | Search and browse the actual persisted resources. Identify type, operational state and subscriber count. Distinguish no resources from no matches, denied access, missing capability and a failed request. Return from creation to an updated collection with useful focus. Counts and pagination describe the server result accurately. |
| Create and configure | Choose an available trigger family and edit its supported configuration. Required fields and family-specific validation identify the exact field to correct. HTTP mode is intentional and remains visible after saving. Cancel leaves no resource or configuration change. Reopening and reloading show saved values. |
| Trigger details | Inspect configuration, resource identity, revision, ownership, effective operational state, subscribers and recent deliveries. Identify why a trigger is paused, unavailable, incompatible or unable to accept work. Enable/pause and live configuration changes expose their impact before applying. |
| Subscriptions on the trigger | Add, change and remove a workflow association; select the serving target and edit its input mappings. Show the resolved serving version separately from the stable subscription target. Each row has its own availability and validation state. |
| Triggers in the workflow editor | Inspect and edit the same live associations for the logical workflow. State explicitly that these changes affect live entry points independently of saving or publishing the open draft. Retained tabs, a historical version and an unsaved draft do not change which live association is edited. |
| Input mapping | Read the selected serving target's declared input names, types, required/default rules and current compatibility. Map source data or configured values per subscription using the reviewed mapping contract. Preview against a sample source payload and show authoritative validation errors beside their inputs. An unavailable preview is labelled; it is never presented as validation success. |
| Change review | Name the affected trigger, workflows, serving slot and operational effects. Distinguish the current and proposed configuration, mappings or associations. Apply uses the reviewed revision; a concurrent change requires reload and review instead of overwriting newer state. Cancellation leaves the current live state intact. |
| Delivery list and detail | Distinguish source occurrence/admission, each subscriber's dispatch, workflow execution and HTTP transport outcome. Link exact workflow execution identities to ordinary Runs inspection. Expose pending work, partial failure, retries, cancellation and recovery without implying that accepted means completed. |

The workflow surface can use the existing [designer panel contribution](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts) and [exported panel context](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorTypes.ts). Load live subscriptions and their target contracts by logical definition identity. The open draft's inputs are not evidence that the serving target is compatible. The panel is not an Activity Definition graph feature.

## Confirmed lifecycle and target behavior

- A subscription follows the workflow's serving publication slot. Show that identity and the version currently serving it. When the slot changes from version 1 to version 2, new accepted work resolves version 2 under the reviewed contract.
- Accepted work retains its captured target version. Pausing the entry point, changing an association or unpublishing the serving slot does not silently discard or retarget accepted work. Show the captured version in delivery evidence even when the current slot serves another version or is unpublished.
- Pause affects new acceptance and does not implicitly cancel existing executions. The change review must distinguish new starts, accepted pending work and running workflows.
- Cancelling pending work is a separate explicit operation with its own permission, impact and result. Its confirmation identifies the pending work affected; the outcome does not imply that already started workflows were cancelled.
- Removing an association must explain its effect on new acceptance and accepted work. The trigger and workflow surfaces must show the same result after refresh or reload.
- A trigger with no subscribers has a visible empty state and explanation. Its activation and incoming-delivery behavior must match the reviewed no-subscriber policy. A source occurrence that accepted no work cannot appear as a successful fan-out admission.
- Unpublished, disabled, unavailable and input-incompatible targets remain identifiable and have a useful reason and recovery action. Do not turn them into an empty subscriber list or silently select another workflow.

The final feature contract must settle the no-subscriber policy, activation requirements, acknowledgement status/body, mapping language/context and revision application rules before their corresponding scenarios can pass. An unresolved contract leaves that scenario incomplete.

## Input mapping and shared changes

Use at least two workflows with different declared inputs, including a required string and a typed date or number. Supply values from an actual source payload and from configuration. Show the target contract used by the mapping; required, optional, omitted and explicit null values must follow the reviewed backend rules. Preserve exact numeric values across authoring, persistence and dispatch where the workflow input type requires them.

Prove each of these outcomes through the UI and real APIs:

1. A valid mapping saves, survives reload and reaches the intended workflow inputs without adding a trigger activity to the business graph.
2. A missing required value, wrong type or invalid source path/expression identifies the subscription and field, preserves the user's edit and offers correction. Server rejection stays distinct from local authoring feedback.
3. Preview shows the source sample, mapped input results and validation outcome with sensitive material protected under the host's normal policy. Preview does not start workflows or mutate the live resource.
4. Publishing a compatible replacement updates the displayed serving version. Publishing an incompatible input contract produces visible incompatibility and the reviewed admission behavior; the previous mapping is not silently rewritten or treated as valid.
5. Changing a shared trigger shows every affected subscriber in the impact review. Changing one subscription's mapping does not alter another's mapping. Applying from one surface is visible from the other after refresh or reopening.
6. Two concurrent editors cannot overwrite each other silently. Retain the rejected editor's local changes, explain the conflict and provide a path to reload, compare and reapply against the current revision.

The existing [workflow run input parsing](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowRunInputs.ts) and [workflow input binding authoring](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowInputArgumentBindings.tsx) are references for type handling and feedback. The latter's activity `WorkflowRequest` context does not define the standalone source mapping context.

## HTTP modes and truthful outcomes

| Scenario | Observable result |
|---|---|
| Workflow-owned response | The trigger names one response-owning workflow. A real HTTP request receives that workflow's intended response through the reviewed request lifetime. Inputs, response and exact run identity can be correlated. |
| Ambiguous response ownership | Adding a second response owner or choosing an incompatible response contract is refused at the reviewed boundary. The UI names the conflict and retains the last valid live configuration. |
| Asynchronous fan-out | The mode explains that the trigger acknowledges accepted work and subscribers do not compete to write the original response. A real request produces the reviewed acknowledgement only after the required admission has succeeded. Subscriber workflow status is shown separately. |
| Require all subscribers | The default per-trigger policy is visible beside async mode and on both association surfaces. With one invalid/unavailable enabled subscriber, the actual request is refused before any starts are accepted. The UI and caller result identify the failure without claiming partial progress. |
| Accept available subscribers | The selected per-trigger policy is visible. A real request with mixed eligibility durably accepts healthy subscribers and explicitly reports partial acceptance with accepted/refused outcomes. No healthy subscribers produces a failed request. Policy change has an impact review and is captured in delivery evidence. |
| Invalid source request | Malformed/oversized/unauthorized source input is refused before acceptance. Studio shows a safe source failure; no subscriber is labelled accepted. |
| Invalid mapping or unavailable recipient | RequireAll refuses the complete request before acceptance; AcceptAvailable may accept healthy recipients and returns explicit partial acceptance with the refused recipient/findings. Zero accepted is failure. Both caller and Studio show the selected policy and exact distinction. |
| Failed or unavailable admission | Failure of the required source/persistence boundary or an unreadable/unknown cohort cannot produce successful acceptance. A safely evaluated individual unavailable target follows the selected policy. Queue shedding after durable acceptance remains accepted, owed dispatch and visible recovery work. |
| Partial fan-out and recovery | One subscriber starts successfully while another fails at the controlled boundary. Recovery makes progress on eligible failed/pending work without starting the successful subscriber again. Attempts and exact target/run identities remain inspectable. |
| Timeout, disconnection or response write failure | Show the HTTP transport outcome separately from whether work was accepted, dispatched or completed. A completed workflow alone is insufficient evidence that its HTTP response reached the client. |
| Cancelled pending work | The explicit result distinguishes CancelledBeforeDispatch, DispatchCommitted and actual TargetAccepted/Started. Commitment may have no queued/running execution yet; show “dispatch committed; awaiting target acceptance,” keep owed recovery visible and disable an unavailable execution link. Pause/unpublish does not replace explicit cancellation. |

For both modes, test route conflicts with embedded HTTP endpoints and other standalone triggers through normal authoring/activation APIs. The screen must show the conflict and corrective action. One endpoint owner remains visible in asynchronous fan-out; the subscriber count does not represent multiple HTTP response owners.

## Permissions, ownership and module availability

Use separate real identities for an authorized operator, a reader and a denied user, plus separate tenant scopes. Exercise direct URLs and API rejection as well as navigation visibility. A reader sees permitted facts and disabled/unavailable actions with reasons; a denied user sees no stale resource identities, counts, source data or delivery details. Switching backend, tenant or identity clears previous results and invalidates in-flight responses and edits.

Source-owned trigger configuration is readable within the caller's access and clearly marked with its source authority. Editing controls follow the source's allowed actions; an operational control remains available only when separately permitted. The UI never offers a successful local override that the backend will ignore.

Test an absent backend capability, unsupported contract major, missing trigger family module and missing optional configuration editor. Explain which capability is unavailable, preserve permitted readable facts, and provide recovery without resetting saved values or pretending a trigger is valid. A public module extension must also be exercised by an independently built consumer. Built-in family behavior alone does not qualify the extension contract.

Keep permissions and availability server-authoritative. The existing [backend capability evaluation](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/backends.ts), [workflow tag access handling](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowTagsPage.tsx), and [User Tasks actor/revision API](../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/taskApi.ts) provide implementation references.

## Composed journey inventory

Extend the existing [owned-host runner](../../tools/ux-acceptance/run.py) and [acceptance journeys](../../Studio/tests/acceptance/) with the following inventory. These are required evidence names for the new journeys, not assertions that they exist or have run. Each capture has a `.png` screenshot and `.aria.yml` accessibility snapshot; each journey retains a Playwright trace and actual request/result metadata with sensitive material removed.

| Journey | Actions and disproof checks | Required captures |
|---|---|---|
| `standalone trigger management` | Open empty list; create each initial family; cancel a create; reject invalid configuration; save and reload; search for no matches; open details and edit configuration; inspect no-subscriber state. | `triggers-reference`, `triggers-empty`, `triggers-populated`, `triggers-no-matches`, `trigger-create`, `trigger-validation`, `trigger-details`, `trigger-no-subscribers` |
| `standalone trigger associations and inputs` | Add/change/remove from trigger details and from the workflow editor; reload both surfaces after every direction; map differing target contracts; preview; reproduce invalid/incompatible input; reject concurrent edit; confirm no business graph or draft publication changed. | `trigger-subscriptions`, `workflow-live-triggers`, `subscription-mapping`, `mapping-preview`, `mapping-errors`, `target-incompatible`, `subscription-conflict` |
| `standalone event and schedule starts` | One event and one real due schedule each start multiple workflows independently; one workflow also starts from another standalone trigger. Inspect declared inputs and exact runs. Change schedule/timezone with impact review; pause new starts; inspect the reviewed missed/due-firing policy and accepted work retained across the change. | `event-delivery`, `schedule-next-firing`, `shared-change-review`, `trigger-paused`, `schedule-delivery`, `subscriber-runs` |
| `standalone HTTP modes` | Send real requests in each explicit mode; inspect response owner or acknowledgement; RequireAll rejects a mixed-validity set while AcceptAvailable returns explicit partial 202; zero accepted and source-wide unavailable intake fail; observe postaccept queue shedding as owed work and transport failure separately from accepted/started/completed work. Publish real embedded HTTP endpoints against standalone reservations in both ownership orders. A genuine two-minute Delay outlives the normal server response deadline; a browser AbortController disconnects only after physical admission. | `http-workflow-response-mode`, `http-response-owner-error`, `http-fanout-mode`, `http-admitted`, `http-policy-refused`, `http-partially-admitted`, `http-zero-accepted`, `http-rejected`, `http-route-conflict`, `http-transport-outcome`, `http-embedded-owner-conflict`, `http-standalone-owner-conflict`, `http-timeout-retained`, `http-client-disconnected` |
| `standalone delivery lifecycle and recovery` | Capture accepted version 1; replace serving slot with version 2; verify later acceptance targets version 2; pause/unpublish and retain accepted version 1; cancel unsealed work; stop after seal before enqueue and inspect dispatch commitment, too-late cancellation and unavailable run link; restart/recover without repeating successful starts. Separately keep A alive on its real source claim while concurrent B claims and completes another source in the same actual queue, then release A's exact inspected seal. | `delivery-admission`, `delivery-subscriber-outcomes`, `delivery-captured-version`, `target-unpublished`, `pending-cancel-review`, `pending-cancelled`, `delivery-dispatch-committed`, `cancel-dispatch-committed`, `delivery-partial-failure`, `delivery-recovered`, `delivery-concurrent-claims`, `delivery-concurrent-peer-started` |
| `standalone genuine BPMN inputs` | Author literal/model-input mappings through the actual BPMN service-worker binding editor; cancel and check focus; save/reopen and retain mappings; reject invalid destinations/types; publish the real model and prove the service worker consumes its declared input through ordinary model continuation. Do not substitute API seeding or a native wrapper for this changed editor. | `bpmn-worker-input-mapping`, `bpmn-worker-input-validation`, `bpmn-worker-input-reopened`, `bpmn-declared-input-run`, `bpmn-worker-input-consumed` |
| `standalone access and availability` | Exercise reader/denied/operator identities, direct navigation, tenant/backend switches, source-owned configuration, missing capability/type/editor, initial and refresh failures with recovery. Build the independent authoring/editor source consumer, remove its real optional Studio feature, retain metadata editing, and restore its saved values. A separately enabled contract-advertisement fixture removes the actual v1 API and advertises major 2 through the normal capability registry; it implements no v2 API semantics. | `triggers-read-only`, `triggers-denied`, `triggers-other-tenant`, `trigger-source-owned`, `trigger-backend-scope-switch`, `triggers-scope-response-isolated`, `trigger-module-unavailable`, `trigger-capability-unavailable`, `trigger-contract-incompatible`, `trigger-optional-editor-installed`, `trigger-optional-editor-unavailable`, `triggers-loading`, `triggers-refresh-error`, `triggers-load-error`, `triggers-recovered` |

The runner currently exercises desktop and narrow light/dark projects and enforces a finite capture inventory. Add the new journeys and captures to that inventory rather than allowing a selected subset to masquerade as full acceptance. Add a 320px inspection for changed surfaces; the existing narrow project is 360px. Check Dim and High contrast on the new collection, details, mapping and delivery surfaces. Record the exact palette/mode and viewport in evidence.

Use actual composed Workbench and Studio hosts built from the committed checkout, owned ports/process groups and disposable stores. Seed workflow definitions and test identities through real setup APIs where needed; create/edit resources and both association directions through the UI. Deliver actual Events, await actual schedule firings and send actual HTTP requests. A manual fire-now control does not qualify scheduling. Controlled clocks/faults may establish backend boundaries, but must be labelled and tied to the corresponding real resource/delivery shown in Studio.

Restart and multi-node claims require matching backend qualification with retained delivery identities, then Studio inspection of those outcomes. Include a due schedule with zero eligible recipients: show its retained overdue cursor/retry reason, restore eligibility and observe one acceptance, then the next future firing. Known zero eligibility and unreadable admission/store failure have separate reasons and neither advances the cursor. An already partially accepted firing retains its original accepted/refused set. A browser reload does not prove process restart. A single host does not prove multi-node behavior. Retain controlled load/failure evidence separately from ordinary successful requests. Existing embedded-trigger, Event broadcast, bookmark-resumption, publication authority and tenant/access regressions remain part of the feature's backend qualification; this UI plan does not replace them.

The controlled process-loss journey uses the independently built acceptance module to read the real protected dispatch seal and hold its first native enqueue. The runner retains process, boot, source, claim, seal, recipient and captured-material facts; cancellation uses the ordinary preview/apply API and UI. It kills only owned process A, starts B over the same stores with the blocking fixture disabled, and waits the full production claim visibility interval after death without changing clocks or claims. This establishes sequential peer recovery. The separate concurrent witness retains both live processes: A's original claim stays unexpired while B claims and completes a second source, then the owner releases A's exact inspected seal. Its normal hosts share Runtime/Design/Identity SQLite stores and have separate content and Nuplane stores; this is a source-queue witness, not distributed placement qualification. `delivery-partial-failure` names the actual post-seal process-loss/owed-work state, not an invented recipient failure. Healthy-subscriber retry suppression is exercised separately beside the controlled shed recipient in the HTTP journey.

For an in-flight scope change, the runner starts a second Workbench with isolated Runtime/Design stores and an explicitly shared owned Identity store/keyring. It temporarily disables Studio host authentication and uses normal authenticated backend profiles so the frozen primary does not block shell navigation. The supported BackendSwitcher navigation replaces the old document; evidence records request cancellation only if observed. After the primary resumes, the peer must still show its distinct resource data. The runner restores ordinary Studio host authentication and stops the peer afterward.

## Keyboard, layout and recovery inspection

Inspect the complete changed experience, including empty and populated lists, create/edit, mappings, change review and delivery detail. Verify labelled controls, visible focus, logical Tab order, keyboard access to tabs/actions, Escape/cancel where supported and focus restoration after close/save. Errors are associated with the correct field and the user can reach and correct them without losing other input. Long workflow names, source labels, routes, identifiers and error messages wrap or scroll in their owning region; the page itself does not overflow at narrow widths.

Inspect loading, initial failure and refresh failure separately. Retained data is labelled appropriately and cannot remain actionable after its backend, tenant, identity or permission scope changes. Retry preserves the correct resource context and clears stale failure state only after recovery. Success notifications describe the completed action; validation, stale state and load errors stay inline. An incomplete operation has no success toast.

Use real composed permission and capability states for their acceptance. An isolated fixture can support otherwise difficult transient rendering checks, but record that limitation and retain a separate composed journey for the underlying product behavior. Every unexecuted, unavailable or excluded state needs a reason in the evidence matrix; no exclusion is implicit.

## Evidence and independent acceptance

Before running, attach this goal/reference/state record to the implementation issue/PR evidence. Run against the exact committed candidate; retain `report.json`, `playwright.json`, HTML report, screenshots, snapshots and traces. Record tested source/merge SHA and PR head separately. Retain failed runs as failed, and link the later exact-commit retest rather than replacing their outcome.

Use the existing CI artifact `studio-ux-<tested SHA>-<attempt>` and link its Actions run and downloadable artifact in the PR. Name the new screenshots/traces in the evidence matrix. Local paths alone are insufficient review evidence. Rerun when artifacts expire or the affected source changes. Final closeout also needs resulting-main qualification under the repository's delivery rules.

The independent acceptance/spec reviewer receives this record, exact head, backend qualification references, downloadable visual evidence and an owned running app where useful. They must open the screenshots and inspect the complete experience, including the corresponding Workflow Definitions/Connections reference. They assess hierarchy, spacing, labels, action emphasis, live-vs-draft clarity, state truthfulness, themes, narrow layout, keyboard/focus and recovery. Record each finding as a specific state and user consequence, and retest fixes on the new exact head.

Post the receipt in the existing PR evidence/review comment:

```text
UX acceptance: passed | failed | incomplete
Reviewed PR head: <full SHA>
Tested source/merge SHA: <full SHA>
Goal and reference: <this record and reference links>
Observed journeys/states: <executed and visually inspected inventory>
Backend boundary evidence: <admission/version/retry/restart/multi-node references>
Actions run/artifact: <downloadable links and named screenshots/traces>
Independent reviewer: <identity/lens; distinguish author's subagent from final app review>
Findings and retest: <finding, fix commit and exact retest, or none>
Not verified: <failed/skipped/cancelled/unavailable/incomplete states and reasons, or none>
```

A green machine report is supporting evidence. It does not establish independent visual acceptance. Missing, stale, inaccessible or uninspected evidence leaves UX acceptance incomplete; unresolved usability defects make it failed. This document, a design packet or a happy-path demo cannot establish delivery of #3582.
