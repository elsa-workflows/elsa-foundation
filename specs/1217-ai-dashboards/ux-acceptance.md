# AI-authored dashboards: interaction design and UX acceptance

**Status:** Implementation and scoped composed acceptance recorded for [#3604](https://github.com/elsa-workflows/elsa-foundation/issues/3604). The [downloadable evidence ledger](evidence/2026-11-13/README.md) contains original screenshots, matching ARIA, safe traces and exact-source receipts. The final local keyboard/editor/Home witnesses now pass; resulting-main, publication and final delivery gates stay open below; earlier failed/unavailable/interrupted attempts retain their recorded outcomes.

## User goal and confirmed boundaries

A signed-in person starts with a question about data deliberately exposed by participating Elsa modules, clarifies its meaning, previews a trustworthy answer and saves a reusable question. They place that question on a personal dashboard, reopen and refresh it, optionally share its definition, and investigate authorized source records. Another viewer receives results under their own current access. Ordinary query and presentation editing remains available when Weaver's provider is unavailable.

The owner's selected scope permits new supported queries and configurable charts, tables, lists and metrics. Module-owned data and declared business dimensions are the initial sources. AI assists during ask, refine and preview; ordinary saved-query refresh requires no model turn. Dashboard and question sharing never conveys the author's data access. Generated widget code, external-database querying, runtime mutation and proactive monitoring are excluded from this release.

**Owner-confirmed direction, 6 November 2026:** introduce a separate named Dashboards area with Mine and Shared with me alongside the existing Home experience, preserving Home's routes, widgets, user layout and administrator defaults. The detailed editor arrangement remains a design proposal. Audience kinds and collaborative editing policy follow the agreed authorization design rather than being inferred from this visual proposal.

## References and composition

- Collection reference: [Connections](../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage.tsx) and [Workflow Definitions](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx). Use their page header, search/filter toolbar, results surface, truthful counts and pagination structure.
- Shared controls and feedback: [module UI guidance](../../Studio/docs/module-ui.md). Use public `@elsa-workflows/studio-ui` controls and host dialogs/notifications; keep field validation, load failures and stale-data notices inline.
- Layout reference: existing [Home](../../Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage.tsx) and [widget frame](../../Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/WidgetFrame.tsx). Reuse semantic sizes and framing concepts without changing Home preference semantics.
- Save/copy reference: the [workflow saved-view dialog](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowLibraryControls.tsx). Its explicit save-new/update choices and revision-conflict recovery are useful patterns; its tenant-wide sharing checkbox does not establish the new audience model.
- Conversation reference: existing [Weaver surface](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/WeaverSurface.tsx) and resource-specific result/review contributions. Query-owning modules contribute context, tools, rendering and apply/save semantics through Weaver's existing contracts.
- Appearance contract: [design tokens](../../Studio/docs/design-tokens.md). Respect current theme, appearance and navigation settings; a module consumes `--studio-*` tokens and public controls.

## Proposed surfaces and transitions

### Dashboards and Questions collections

The Dashboards collection has named **Mine** and **Shared with me** filters, search, and a primary **Create dashboard** action when allowed. Rows identify the dashboard, owner and available sharing/access state without exposing inaccessible audience identities. Empty collections explain how to create a dashboard or that nothing has been shared; filtered emptiness offers clear-filter recovery. A denied or unavailable collection is not presented as empty.

A Questions collection makes saved questions discoverable and reusable. A question shows its name, owning dataset/module, relevant revision and available actions. Opening a question shows its query interpretation, presentation and current result. The user can add an accessible question to a dashboard without silently copying the author's results. Create and copy return to a coherent saved state with an explicit confirmation; the user can then choose to open or place the result.

### Question authoring

The authoring workspace gives the question a stable draft identity and presents the ordinary query controls, preview and Weaver conversation together. At a desktop width the preview remains visible while refining; at narrow widths these become an ordered stack or explicitly selected views preserving the same draft. The ordinary editor is available without opening Weaver.

1. **Ask.** The user asks, for example, “Show workflows whose failure rate increased this week.” Weaver receives the available, authorized module capability descriptions. The UI indicates the active source and any provider unavailability without claiming that unsupported data exists.
2. **Clarify.** Consequential ambiguity becomes a specific question with clear alternatives, such as calendar week and time zone versus trailing seven days, or the applicable denominator. Available module semantics govern the choices. No choice is silently assumed when it would materially change the answer. The user can cancel clarification without creating a saved question.
3. **Refine.** “Group by folder and exclude test runs” updates a proposed definition only when those operations and fields are actually supported. The workspace explains any unavailable field or history with a useful alternative. Unsupported requirements remain visible until the user changes them; the source or metric is never silently substituted.
4. **Review the proposal.** Show the proposed dataset, metric meaning, filters, grouping, comparison/time windows, time zone, parameters and visualization in ordinary language. Module-owned result rendering may expose technical details in a disclosure. Applying a proposal updates the working draft under the resource's declared semantics; it does not silently persist it.
5. **Preview.** Run the validated query under the current viewer. Show real results, coverage, source/metric interpretation and refresh time. The preview clearly identifies unapplied edits or a query revision different from the displayed result. A request that finishes after cancellation or a newer edit cannot replace the newer preview.
6. **Edit normally.** The user changes supported source fields, filters, aggregation, grouping, sorting, bounded time range, parameter values or presentation through labeled controls. Options come from participating module capabilities. The editor validates changes before execution and explains field-specific corrections. Changing chart type preserves the query; an incompatible chart choice explains what must change.
7. **Save explicitly.** The user supplies a name and chooses **Save question**, **Save changes** or **Save a copy**, according to current rights and resource identity. Save is separate from preview and proposal application. Show an accessible saving state, retain the draft on failure, and expose revision conflict recovery without overwriting another editor's change. Successful save shows the persisted name/revision and offers placement on a dashboard.

History/reuse exposes saved definition revisions and their interpretation, not a misleading historical result snapshot. Revisiting an older definition identifies that revision, validates it against current capabilities, and makes restoring or copying an explicit edit. Unsupported historical revisions remain inspectable only within current access and provide actionable recovery.

### Dashboard arrangement, reopening and refresh

A dashboard's title, owner/access state and primary actions precede its tiles. Empty dashboards offer **Add question** and, when available, **Ask Weaver**. Adding a question, changing size, moving a tile and removing a tile have explicit, understandable persistence behavior. Removing a tile does not delete its saved question. Reusing a question on several dashboards does not silently change every dashboard's presentation overrides.

Tiles have instance identities separate from question and renderer identities. The same saved question can appear more than once where allowed without duplicate accessible IDs or controls targeting the wrong tile. Each tile identifies its source, metric/filter interpretation, relevant coverage and last successful refresh, with details available without conversation.

The user arranges tiles through keyboard-operable move controls as well as any pointer interaction. Announce the new position and preserve focus on the moved tile's control. Reopening the saved dashboard restores its actual persisted arrangement and question references. A missing or inaccessible question remains a meaningful unavailable tile where disclosure is allowed; it never becomes a different question.

**Refresh** and any permitted periodic refresh use ordinary query execution. They work with the AI provider unavailable and never open a conversation or silently request a model turn. Scope changes clear unsafe retained results. A failed refresh may retain a clearly stale result only while the viewer's current access still permits its display. The user can cancel an active query, understand the resulting state and retry deliberately; one failing tile does not hide successful tiles.

### Sharing, editing and revocation

Sharing begins from an explicit question or dashboard action and identifies exactly which definition is being shared. A dialog lists the permitted audience kinds and current grants according to the approved authorization model, with **Can view** and **Can edit** choices only when supported. The default is personal until the author explicitly shares. Public links and cross-tenant sharing are excluded unless separately agreed.

Before committing a share, explain: “Viewers see data using their own access.” Distinguish definition access from source-data access; a person may be able to open a shared dashboard while a tile is unavailable to them. Sharing a dashboard must explain any inaccessible or separately shared question dependencies rather than silently widening their audience. Audience discovery does not expose identities outside the sharer's authorized scope.

A read-only viewer sees current results and authorized drill-down, plus **Save a copy** when allowed; they cannot edit the original through ordinary controls, Weaver or direct API calls. An editor can make only the changes granted by policy; editing a definition does not itself grant sharing administration. The owner can change/revoke grants where authorized. Reopening, refresh, drill-down and supported export reevaluate current definition and data access. After revocation or source permission loss, remove protected results/identities/counts and provide an appropriate access message.

### Drill-down and supported export

An aggregate or list exposes **View matching records** only when the contributing module supports it. The destination carries the meaningful query/time/filter context and identifies any limitations. Every displayed source record is authorized for the current viewer. The destination rechecks access even when reached from a previously rendered tile; a forbidden or removed record has actionable recovery to the question/dashboard without disclosing protected details.

Export is offered only when the module and viewer permit it. Its scope and limits are explicit, cancellation is available where supported, and it reuses neither the author's result cache nor an expired permission decision. An unsupported export/drill-down is explained rather than rendered as a control that cannot work.

## Observable states and recovery

| State | Observable outcome and recovery |
| --- | --- |
| Initial loading | Identify the requested collection/question/tile; avoid empty or zero claims before completion. |
| Empty collection / no matches | Distinguish no saved resources from filters matching none; offer create or clear filters when allowed. |
| Complete result with zero | Display numerical zero with the metric denominator and coverage. Do not use a generic “no data” state. |
| Complete result with no rows | Explain that no authorized records matched the selected definition; retain its filters and period. Do not imply that inaccessible records exist. |
| Missing data or unavailable metric | Identify the unavailable capability at a safe level and offer supported alternatives, configuration help or retry where meaningful. |
| Unsupported question | Preserve the user's request and explain which requested operation/field is unsupported; do not fabricate a result. |
| Incomplete coverage | Mark affected periods/metrics and known coverage limits. Do not present a partial denominator as complete or infer a trend from missing comparison history. |
| Stale result | Label the last successful result/time and why it is stale; retry or edit is available. Never retain results after authorization scope becomes unsafe. |
| Failed refresh | Distinguish failure from empty/zero. Preserve editable definitions and disclose retry/cancellation; failures in one tile do not erase other tiles. |
| Cancelled / bounded-cost rejection | Stop the request, prevent late-result replacement, retain the draft and explain a narrower time range or supported alternative. |
| Invalid query or visualization | Associate errors with fields, reveal the affected editor section and keep the user's correction draft. |
| Save conflict / failed save | Preserve unsaved work; show reload/review and save-copy paths according to rights. Do not claim local fallback is a shared save. |
| Read-only / denied / revoked | Explain permitted actions without exposing protected identities or results. A hidden button is supported by authoritative backend enforcement. |
| Provider unavailable | Weaver offers recovery/status; ordinary query editing, saving, reopening and refresh remain usable. An existing query is not marked invalid merely because AI is unavailable. |
| Module disabled/removed or schema changed | Retain the definition identity and identify the invalid reference where permitted. Offer explicit repair/copy; never substitute a source, field or metric. |
| Optional AI explanation | Identify it as generated, ground factual statements in the actual current result and expose relevant coverage. Omit causal claims unsupported by evidence. |

## Accessibility, themes and narrow layouts

- Use logical page/section headings, one host-owned main landmark, labeled controls and meaningful accessible names for tile-specific actions. Loading and action announcements must be useful without repeatedly announcing each chart point.
- The complete ask/clarify/refine/preview/save/arrange/share/drill journey works with the keyboard. Dialog focus enters predictably, remains contained, returns to its opener on cancel and moves coherently after successful creation/navigation. Escape/cancel preserves or discards work exactly as described; destructive or irreversible effects use the existing host confirmation conventions.
- Move controls announce position, keep visible focus and never require drag-and-drop. Shared questions/tiles with long names and duplicate titles remain distinguishable by context.
- Charts have readable titles, units and legends plus an accessible exact-value table/list. Meaning must not depend only on color. Empty, zero, gaps and unavailable periods are represented honestly in both visual and accessible views.
- Check the changed collection, editor, preview, dashboard and share surfaces in light and dark, plus supported material/layout treatments, Dim and High contrast where available. Check both current left and top navigation where their composed geometry affects the workspace. Record an exclusion with a concrete reason instead of omitting it silently.
- At 320 CSS pixels, headers/actions wrap, controls remain operable, dialogs fit and all content can be reached. Horizontal scrolling stays in a named, keyboard-focusable results/chart region rather than on the page. Stacking the conversation/editor preserves the draft and does not hide Save or cancel.

## Minimum qualification journeys

The following are required proof outcomes, not statements that the implementation already provides them. Tests must use an owned composed host and disposable stores. They must not attach to or mutate a personal/shared host.

| ID | Journey and discriminating evidence | Current status |
| --- | --- | --- |
| UX-01 | Ask a genuinely novel supported question, answer a consequential clarification, refine grouping/filters, inspect proposed interpretation, preview real module data and explicitly save. Independently calculate the expected metric/comparison and verify the displayed values. Identify the real provider/model run separately from fixtures. | Passed: actual weekly clarification, changed grouping/filter, independently computed comparison and explicit save; source2161b81b92, separate real SDK identity. |
| UX-02 | Add the saved question to a named dashboard, change size/order with keyboard controls, reopen after navigation/reload and verify persisted definition/arrangement. Reuse/copy and saved-history revision behavior remain explicit. | Passed: actual two-tile arrangement, share, reload and same-store real restart; scoped native copy/history receipts. |
| UX-03 | Make the provider unavailable, change a supported filter and presentation normally, save, reopen and refresh. Verify numerical results and backend/provider invocation evidence showing no model turn during refresh. | Passed: native 4-profile editing/refresh and actual provider-disabled host restart; zero additional SDK sends. |
| UX-04 | Share with an authorized audience; open as a different read-only viewer and an authorized editor. Verify copy/update/share rights through UI and APIs, per-viewer aggregate results and authorized drill-down. Revoke definition access and separately change source access; protected results and caches no longer appear. | Passed locally: independent read-only viewer and authorized editor UI save/reopen/private copy, no sharing rights/direct403, scoped results/drill and fresh-principal revocation; current R5 sourcebae6d602b7. |
| UX-05 | Repeat scoped queries across different users and tenants, including a viewer with narrower field/record access and an author with elevated access. Verify aggregate visibility, cache partitioning/invalidation and supported export authorization without leaking author results. | Passed: native/API/package scoped authorization controls and independent Acme owner120000/reader60000; outstanding signed-token invalidation is not qualified. |
| UX-06 | Use a deliberately exposed customer-category business dimension from a participating module, compare processing-time results with an independent calculation, and drill into authorized records. Prove an independent module can contribute through the same reviewed contracts; no prebuilt widget or hardcoded central dataset branch supplies the answer. | Passed: independent package-only module and actual Acme SDK/native source math/drill. |
| UX-07 | Exercise zero, empty, missing data, unsupported question, incomplete comparison history, stale and failed results, cancellation and bounded-cost rejection. Inspect the distinct visible states and recovery; a cancelled/older response cannot replace a newer preview. | Passed: native renderer/cancellation controls plus actual module/null/empty/unknown/stale/offline/422 recovery and live unsupported explanation; exact source receipts remain separate. |
| UX-08 | Change/remove a field, metric or module after saving. Reopen/refresh/history inspection explains the invalid reference; an explicit repair validates a new definition. No silent source/metric substitution occurs. | Passed: actual module removal/history withholding, explicit native replacement validation/save/new revision/reopen/history; no substitution. |
| UX-09 | Cancel creation/share/edit dialogs, trigger field validation and concurrent save conflict, recover without losing the draft, and complete the journey keyboard-only with predictable focus. | Passed locally: native dialog/validation/conflict/focus controls plus full actual keyboard-only ask/clarify/refine/preview/save/arrange/share/drill, seven zero-pointer proofs and real host restart at R5 sourcebae6d602b7. |
| UX-10 | Inspect empty/populated/denied/error states, long names, charts/exact values and relevant theme/layout/narrow captures on the actual host. Verify coherent collection/create/edit/detail transitions. | Passed: native/Home/comparison all four profiles and field gallery, long-name/renderer/theme/narrow original captures independently inspected. |
| UX-11 | Reopen existing Home with its saved layout/admin default, module widgets, missing contribution entries, onboarding and workflow task group. Exercise widget settings/refresh/reset and verify behavior/preferences remain intact. | Passed locally: existing widget/default/settings/reset/onboarding/task-group proof plus current settled missing-contribution Home states and raw-settings retention after ordinary UI save/reload at R5 sourcebae6d602b7. |

Provider evidence, independent calculation, authorization tests and independent module evidence may be separate supporting runs, but each must identify the actual source revision, host/configuration and limitations. A deterministic substitute can verify integration behavior; it cannot satisfy the actual-provider requirement. The existing generic composed runner does not call external models, so its success alone cannot supply UX-01's provider evidence.

## Evidence and independent acceptance

Follow [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md). Extend the existing owned-host journey runner for this feature where needed; do not claim unrelated journeys as dashboard evidence. Review requires a committed source revision and the exact current PR head. Preserve the runner's source SHA, PR head, dirty state, failure stage and distinct passed/failed/skipped/cancelled/unavailable/incomplete outcomes. Every selected journey must execute successfully; retries, omissions or expected failures do not convert an incomplete journey into acceptance.

Retain downloadable current-commit screenshots, accessibility snapshots, Playwright traces and reports for the complete affected experience. Name the user/tenant and access scenario in test metadata without storing credentials or protected production data. Include preview interpretation/results, ordinary controls, saved/reopened dashboard, sharing roles/revocation, drill-down, provider-unavailable behavior and the theme/narrow/error states actually inspected. Record provider-call counts and independently checked metric inputs/results as supporting evidence, clearly distinguishing live-model runs from fixtures.

Link the exact Actions run/artifact and named captures/traces from the existing PR evidence comment. Local absolute paths alone are not shared evidence. Missing, expired or stale artifacts must be regenerated; inspect screenshots rather than inferring their quality from DOM assertions. After fixes, rerun the affected suites and composed journey on the new head and refresh the independent review.

The independent acceptance/spec reviewer receives the user goal, references, this record, exact head, downloadable evidence and an owned running app when available. They inspect complete transitions, hierarchy, spacing, alignment, labels, action priority, focus, keyboard, recovery and saved persistence. Their receipt belongs in the existing PR review/evidence loop; an author's summary is not an independent receipt.

| Acceptance field | Current record |
| --- | --- |
| UX acceptance | **Locally accepted — final publication, clean-stack merge and resulting-main gates pending** |
| Reviewed PR head / tested source SHA | Exact heads and source-specific receipts in [delivery evidence](evidence/2026-11-13/README.md); none relabeled as later-head proof |
| Actions run and downloadable artifacts | Actions queued/cancelled states remain explicit. 190 original PNG/ARIA pairs, six accepted safe traces with four current keyboard traces under review and allowlisted reports are retained in the evidence ledger. |
| Actual provider qualification | Passed: real Copilot SDK1.0.4 through Weaver; weekly Workflow/Acme gpt-6.1-sol and separate unsupported gpt-5.6-sol witnesses |
| Independent numerical/metric verification | Passed: weekly62.5/50/+12.5 →75/25/+50 →80/25/+55; Acme owner120000/reader60000 and Enterprise300000; comparison4/3/+1 |
| Independent UX reviewer / inspected captures | Independent acceptance reviewer opened all190 retained originals and all six safe traces in actual TraceViewer; correctness/standards verified hashes and zero credential matches |
| Open design assumptions | Owner navigation and one model-agnostic Weaver author confirmed; native contract/design reviews clean |
| Unverified coverage | Final clean-stack integration, current trace publication review and resulting-main verification |

Passing a schema test, build, fixture demo or first milestone does not change this status. Unresolved usability defects make acceptance **failed**; missing/inaccessible/uninspected evidence makes it **incomplete**. Feature delivery requires the reviewed implementation, completed qualification and resulting-main evidence under the repository rules.
