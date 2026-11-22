# Studio UX consistency backlog

A checklist of UI polish and consistency defects found in a whole-app review of Studio, sized so
that each checkbox is one small task.

- **Audit date:** 2026-11-21
- **Audited tree:** `74a97158d2` plus a working tree with about 167 uncommitted modified files. Files
  were still changing during the audit, so every line number below is a hint, not an address.
- **The audited code is not on `main`.** That working tree was committed as `a318d67828`, pushed to
  `main` on 2026-11-21 and reverted there the same day because it broke CI. It is kept on branch
  `wip/studio-in-progress-2026-11-21`. Until that work lands, an item may describe code that `main`
  does not have yet; confirm the defect on your own base before changing anything.
- **Status:** delivery in progress. The section trackers in #4990 show live progress.
- **Normative rules:** [UX design spec](../ux/design-spec.md), [module UI guide](../module-ui.md),
  [design tokens](../design-tokens.md). This file lists deviations from them; it does not replace them.
- **Tracking:** every open item has a Foundation issue, shown in brackets after its ID. They hang
  under tracker #4990 through one sub-tracker per section. The issues are not labelled
  `ready-for-agent`: an orchestrator session hands them to workers. Follow the Foundation
  issue-tracker and concurrent-work rules in `AGENTS.md` before starting an item.

## How to work an item

1. Pick one unchecked item whose prerequisites (`needs:`) are ticked.
2. Find the code by the quoted class, label or string. Use the line number only as a starting point.
3. Confirm the defect still exists. If it is already fixed, tick the box and write `already fixed`.
4. If the item is tagged **[check]**, or depends on a point that an owner decision (`D1`–`D14`) lists
   as open, stop and ask rather than guess.
5. Make the change with kit primitives from `@elsa-workflows/studio-ui` and `--studio-*` tokens.
6. Run `pnpm typecheck`, `pnpm lint` (includes `pnpm lint:css`) and `pnpm build` from `studio/`, plus
   the tests of the module you touched.
7. Tick the box and append the PR reference, for example `- [x] **X3.4** (#1234) … (PR #1301)`.

Do not weaken a test to make an item pass, and do not widen an item beyond its line.

## Evidence tags

| Tag | Meaning |
|---|---|
| **[seen]** | Observed in screenshots or computed styles of the running app (1440×900, light theme). The screenshots were not committed. |
| **[verified]** | Reported by a source reader, and the cited file and line were re-checked by script on the audit date. Only the presence of the quoted pattern was checked, not the proposed fix. |
| **[source]** | Reported by a source reader with file and line, not re-checked. Several readers reported partial coverage. |
| **[check]** | Reported but not confirmed. Verify before changing anything. |

## Coverage gaps

Treat these as unaudited. Follow-up audit tasks are listed under [Follow-up audits](#follow-up-audits).

- **Not captured in the running app:** top-bar menus (theme switcher, notifications, Weaver, user
  menu), the bottom dock, the activity draft editor, the activity publication dialog, run-detail tabs,
  the Decisions Tests and Settings tabs, dark-mode dialogs, narrow viewports.
- **Captured but not reviewed:** dark-mode page screenshots.
- **Modules absent from the audited host:** ETL, Extension Builder, External sign-in, User access,
  Weather sample. These were looked at in source only.
- **No completed source audit:** ETL, Extension Builder, Feature management, Identity (external
  authentication), Console stream, OpenTelemetry, Structured logs, Weather sample, Weaver workflows and
  the JavaScript and Liquid expression editors. The reader assigned to this group never returned a
  report. Items for these modules come from a handful of targeted greps and are far from complete.
- **Partially read:** `WorkflowInstances.tsx`, `WorkflowExecutableInspector.tsx`,
  `ReusableBoundaryInspector.tsx`, `graph-authoring/*`, `AgentEditor.tsx`, `ConnectionCreateDialog.tsx`,
  `FormDesigner.tsx`, `PropertyEditors.tsx`, `TableEditor.tsx`, the host Diagnostics page and
  `propertyEditors.tsx`.

### Extension audit follow-up

The table records the source paths inspected for A1. PR #5326 carries the screenshots and executed journeys for the A5 composition. The five optional surfaces ran against a disposable Studio host and Workbench with ETL persistence, external authentication, the Extension Builder bridge, and the Weather sample assembly and shell feature enabled.

| Surface | Audit findings |
|---|---|
| ETL | Followed file and connector collection selection through draft creation, editing, validation, publication and artifact inspection. Reviewed runtime selection, paging, cancellation and status presentation. The collection and editor now have separate views. |
| Extension Builder | Followed managed workspace creation through the solution explorer, editor, source control, build, promotion and runtime dock. The dock uses shared keyboard navigation and disclosures. Disabled operation descriptions explain the required permission. The live host also exposed a case mismatch between the backend’s enum serialization and Studio that kept New extension from finding project templates. |
| External sign-in and User access | Reviewed read-only configuration, editable database-backed connections, provider test and preview return, directory search, identity links, sessions and revoke confirmation. User access now shares the Studio timestamp format in the detail view and confirmation. |
| Feature management | Inspected host selection, capability gates, the feature list and relationship inspector, cascading draft changes, settings fields, secret handling, save and restart notices. Related migrations and the file-structure issue remain tracked under Feature management. |
| Console stream, Structured logs and OpenTelemetry | Reviewed the full pages and dock contributions, including buffering, connection status, filter and pause behavior, trace detail and compact layouts. The resulting changes and composed captures are in PRs #5280, #5292, #5294 and #5298. |
| Weather sample | Inspected route registration, host-backed refresh, retry, loading, tabular results and the result count. The sample requires a host assembly reference and module catalog registration as well as enabling its shell feature. |
| Weaver workflows | Inspected the metadata, workflow-definition and executable prompt registrations and contextual attachments. Confirmed provider-off and menu states separately in PR #5310. |
| JavaScript and Liquid expression editors | Inspected both inline and expanded registrations, source renderers, read-only state, source insertion and shared code editor bindings. JavaScript also supplies result checks, variable-addition preservation and language tooling. |

### Host audit follow-up

PR #5339 covers the host portions of A2 and A6. The Diagnostics index derives destinations from authorized navigation, guards against cyclic parent registrations, and isolates contributed widgets. Widget loading and subscriptions clean up when the screen unmounts. The built-in property editors distinguish collection and element scope, retain unavailable choices, validate number and duration input, and keep stored password values out of the input. The host test suite exercises those editors.

## Path shorthand

| Short | Path from the repository root |
|---|---|
| `WF` | `studio/src/essentials/Elsa.Studio.Workflows/Client/src` |
| `H` | `studio/src/essentials/Elsa.Studio.Hosting/Client/src/app` |
| `KIT` | `studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/ui` |
| `DASH` | `studio/src/essentials/Elsa.Studio.Dashboard/Client/src` |
| `BACK` | `studio/src/essentials/Elsa.Studio.Backends/Client/src` |
| `EXT/<Name>` | `extensions/<Name>/studio/Elsa.Studio.<Name>/Client/src` |

Exceptions to the `EXT` pattern: Identity is `extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/Client/src`,
Weaver is `extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/Client/src`, and Extension Builder
keeps its components under `EXT/ExtensionBuilder/extension-builder/`.

## Owner decisions

The owner recorded D1–D14 on 21 November and [resolved the remaining product choices](https://github.com/elsa-workflows/elsa-foundation/issues/4990#issuecomment-7071720883) on 22 November.

- [x] **D1. Creation labels.** Use “New …” for creation entry points and dialog titles. Use “Create” for confirmation and version actions, including parallel drafts and form versions.
- [x] **D2. Save label: "Save" everywhere,** including screens with a separate publish step. Retire
  "Save changes", "Save draft" and "Update view".
- [x] **D3. Kickers removed; sentence case.** Remove the small labels above titles in dialogs and
  sections (`.ad-kicker`, `.connections-kicker`, dialog kickers). Switch the Workflows label recipe
  (`--wf-label-transform: uppercase`, `WF/styles.css:87`) and the other uppercase labels to sentence
  case. Not covered: `eyebrow` on a page header, which stays under the existing kit rule ("only when
  it adds information beyond the title/navigation").
- [x] **D4. Detail-page title: compact, 16px / 600** (the run-detail size today), followed by status
  chip and actions. List pages stay at 20px / 600, so the two differ on purpose.
- [x] **D5. The shell owns the breadcrumb trail.** Pages hand their trail to the shell and stop
  rendering their own breadcrumb. This needs host work first (K15).
- [x] **D6. Create flow.** Open a name-and-description dialog, then the editor. This applies to dashboards and questions too.
- [x] **D7. Verbs.** "Delete" destroys a record; "Remove" detaches an item from a list or parent.
  "Retry" everywhere, not "Try again". The Weaver "Withdraw" / "Deny" pair was dropped from this
  decision because they are different actions (see X5.9).
- [x] **D8. Screen names.** "Import from Elsa 3"; "Activity definition upgrades"; "Task forms";
  "Runtime diagnostics". "Console" / "Server console" was not decided: "Server console" could not be
  found in the Console module or host source, so that claim is unconfirmed (see X5.10).
- [x] **D9. Domain terms.**
  - "Run", not "Instance". Not asked: `studio/CONTEXT.md` already makes "Run" the user-facing term
    and says to avoid "Workflow Instance" in navigation and general labels.
  - "Executable" only: "Artifact ID", "Artifact hash" and the "Artifact:" chip all become
    "Executable …". `CONTEXT.md` defines "Artifact Hash", so the glossary has to change first (G1).
  - Use “Run data” and “Run data detail” for user-facing evidence labels. G2 records how these labels relate to the runtime terms.
  - The selector labelled "Provider key", "Implementation provider", "Design-owned provider" and
    "Implementation type" becomes "Provider". **[check]** that all four select the same thing:
    `CONTEXT.md` lists "Implementation provider" as a term to avoid for the Flowchart / Sequence /
    BPMN choice (the "Activity Graph Composition Model").
- [x] **D10. Workspace tabs.** Transient routes reuse the current tab. Cap the workspace at 12 tabs, evicting the least recently used clean tab. Preserve unsaved tabs even when this takes the workspace over 12.
- [x] **D11. Collection search is instant.** No "Search" submit button.
- [x] **D12. Rows open through the whole row** (`studioRowInteraction`). Name buttons and "Details"
  buttons that duplicate it go. **[check]** that keyboard users can still open a row before removing
  a link.
- [x] **D13. Required fields carry a "(required)" suffix.** Optional fields carry no marker.
- [x] **D14. Capitalization.** Keep “Test Run” and “Executable Inspector” capitalized in UI labels. Use sentence case for other labels, including “User tasks”.

## Phase 0: kit work

Most inconsistencies exist because a shared primitive is missing or unused. Do these first.

- [x] **K1. Reusable dialog shell.** (#4992) [verified] No `StudioDialog` component is exported. A Radix-based
  shell with `.studio-dialog*` classes already exists inside `KIT/dialog/DialogHost.tsx` for
  `api.dialogs.confirm/prompt/alert`. Extract a declarative `StudioDialog` from it (title, optional
  description, X close, focus trap, footer slot taking `StudioDialogFooter`) and export it from
  `@elsa-workflows/studio-ui`. Migrate one dialog as the reference (suggest Create workflow). (PR #5271)
- [x] **K2. Destructive button variant.** (#4993) [verified] `KIT/forms/Button.tsx` has
  `StudioButtonVariant = "primary" | "default" | "ghost"`. Add `danger`, styled in `Button.css` with
  tokens. Workarounds to replace afterwards: `wf-delete-action`, `studio-button danger`,
  `backend-remove`, `connections-danger`, `external-auth-danger-action`, `data-tone`. (PR #5268)
- [x] **K3. Icon-only button.** (#4994) [source] Add a kit icon button that requires `aria-label`.
  Workarounds: `.wf-icon-button`, `.studio-icon-button`, `.ad-icon-button`, `.connections-icon-button`. (PR #5270)
- [x] **K4. Pressed (toggle) button.** (#4995) [seen] Console and Structured logs use
  `StudioButton className="active"`. (PR #5280)
- [ ] **K5. Richer menu and split button.** (#4996) [source] The designer "More workflow actions" menu
  (`WF/workflow-editor/WorkflowEditorToolbar.tsx:214`) and `PublishActionButton.tsx` duplicate the same
  `AnchoredPopover` + `role="menu"` code. **[check]** `OverflowMenu` already marks items
  `data-checkable`; read `KIT/disclosure/types.ts` to see what is really missing (group headings,
  radio items, split trigger) before adding anything.
- [x] **K6. Page-size control and "Load more" in `StudioCollectionFooter`.** (#4997) [verified] The footer is a
  bare `<footer>` wrapper. `DefinitionPager`, Activity definitions, ETL and User access each build
  their own page-size select. (PR #5269; only `DefinitionPager` was migrated. The Executables and Runs
  footers still use `.wf-page-size`, so their "Rows" select now looks different from Definitions.)
- [x] **K7. Breadcrumbs for callback navigation.** (#4999) [verified] `StudioBreadcrumbs` takes
  `items[{ href, label }]`, `current` and `renderLink`. Try porting `ScopeBreadcrumb`
  (`.wf-breadcrumb`) with `renderLink` first; change the kit only if href-less items are required. (PR #5281)
- [x] **K8. Eyebrow.** (#5000) [verified] No new primitive is needed: `StudioPageHeader` has `eyebrow` and
  `icon`. Document D3 in the design spec: no kickers in dialogs or sections, sentence-case labels, and
  the page-header `eyebrow` only under its existing rule. `needs: D3` (PR #5279)
- [x] **K15. Shell breadcrumb trail (D5).** (#5001) **[check]** Find where the shell prints "Studio / X"
  (start in `H/App.tsx`) and whether a page can already supply a multi-level, clickable trail to it;
  the audit did not identify such a mechanism. Design it before any X2 item starts, and say what
  happens to the `StudioPageHeader breadcrumbs` prop and to `StudioBreadcrumbs`. `needs: D5` (PR #5293)

Candidates reported by source readers. Confirm the need before building any of them.

- [ ] **K9.** (#5002) [source] Stepper for multi-step flows (`WF/Elsa3ReusableImportPage.tsx:495–633`,
  `WF/ActivityUpgradeWorkbenchPage.tsx:390–463`).
- [x] **K10.** (#5003) [source] Key-fact definition list (`ad-publication-summary`, `ad-impact-facts`, (PR #5309)
  `ad-contract-workbench-facts`).
- [ ] **K11.** (#5004) [source] Radio group and checkbox field (`AgentPlayground.tsx:195`,
  `AgentEditor.tsx:254`, `ConnectionCredentialField.tsx:99–115`).
- [x] **K12.** (#5005) [source] Dismissible `StudioAlert`, to replace module banners (see X9.4). (PR #5299)
- [x] **K13.** (#5006) [source] Compact inline loading status (`ad-inline-status`). (PR #5309)
- [x] **K14.** (#5007) [source] Multi-select field (`DASH/native/ResourceDialogs.tsx:165`,
  `DASH/native/NamedDashboards.tsx:524`). (PR #5313)

## Phase 1: cross-cutting items

### X1. Detail-page headers

Six title sizes were measured [seen]:

| Screen | Title style |
|---|---|
| Workflow designer | 13px bold, inside the breadcrumb row |
| Run detail | 16px / 600 |
| Executable inspector | 18px / 700 |
| Activity definition workbench | 20px / 600 |
| Decision editor | 20px / 700 |
| Settings › Backends | 19.5px / 700 |

List pages are 20px / 600 and stay that way. Decided (D4): detail pages use a compact 16px / 600
title.

- [x] **X1.1** (#5014) Write the recipe into `studio/docs/ux/design-spec.md`: compact 16px / 600 title, status (PR #5307)
  chip, actions; the trail lives in the shell (D5). **[check]** whether `StudioPageHeader` can render
  the compact size; if it cannot, that is kit work to do first. `needs: D4`
- [x] **X1.2** (#5015) [seen] Workflow designer header. `needs: X1.1` The designer header is a documented (PR #5307)
  exception in the design spec; read that section first.
- [x] **X1.3** (#5016) [seen] Run detail header (the title is already 16px / 600; bring the rest of the header (PR #5307)
  in line). `needs: X1.1`
- [x] **X1.4** (#5017) [seen] Executable inspector header. `needs: X1.1` (PR #5307)
- [x] **X1.5** (#5018) [source] Activity definition workbench: `WF/ActivityDefinitionWorkbench.tsx:256` puts (PR #5307)
  `<h1 id="activity-definition-title">` inside the breadcrumb `current` slot with custom
  `ad-header-actions`. Use `StudioPageHeader title= actions=` and move the trail to the shell.
  `needs: X1.1, K15`
- [x] **X1.6** (#5019) [source] Decision editor: `EXT/Decisions/DecisionsPage.tsx:216` custom `<h1>` sized by (PR #5307)
  `.decision-editor-title h1 { font-size: 1.25rem }` (`EXT/Decisions/styles.css:18`). `needs: X1.1`
- [x] **X1.7** (#5020) [seen] Settings › Backends profile header. `needs: X1.1` (PR #5307)
- [x] **X1.8** (#5021) [source] Heading levels. The kit rule is `headingLevel={2}` only "when the host already (PR #5307)
  renders the page's h1". Check each of these against that rule: `WF/ActivityAvailabilityPage.tsx:179`
  and `WF/RuntimeDiagnosticsSettingsPage.tsx:81` (level 2), `WF/ActivityUpgradeWorkbenchPage.tsx:296`
  and `WF/Elsa3ReusableImportPage.tsx:479` (level 1), `EXT/Connections/ConnectionsPage.tsx:144` and
  `EXT/Secrets/SecretsPage.tsx:253` (level 2).
- [x] **X1.9** (#5022) [source] Remove the 18px icon from inside the title on (PR #5307)
  `WF/ActivityAvailabilityPage.tsx:179` (`ListChecks`) and `WF/RuntimeDiagnosticsSettingsPage.tsx:81`
  (`DatabaseZap`), or pass it through the header `icon` prop.
- [x] **X1.10** (#5023) [source] Agent pages use `eyebrow` plus `icon` (`EXT/Agent/AgentPlayground.tsx:174`, (PR #5307)
  `HumanReviewPage.tsx:101`, `AgentInvocationPage.tsx:93`); no other module does. D3 did not cover
  page-header eyebrows, so judge each against the kit's existing eyebrow rule.

### X2. Breadcrumbs and back navigation

Decided (D5): the shell owns the trail, so no page renders its own breadcrumb or "Back to …" button.
Every item in this section `needs: K15`.

- [x] **X2.1** (#5025) [seen] The shell prints a non-clickable "Studio / X" above pages that render their own (PR #5307)
  breadcrumb (designer, run detail, executable inspector, activity workbench, decisions, tags, views).
  Make the shell trail complete and clickable for these pages, then remove the page breadcrumbs:
  [source] `StudioBreadcrumbs` in `EXT/Decisions/DecisionsPage.tsx:215`,
  `WF/ActivityDefinitionWorkbench.tsx:256` and `WF/ActivityDefinitionDraftEditor.tsx:959`.
  `needs: D5`
- [x] **X2.2** (#5026) [seen] Tags and Saved views: the shell says "Studio / Workflows" while the page says (PR #5303)
  "Definitions › …". Make them agree.
- [x] **X2.3** (#5027) [verified] `EXT/Secrets/SecretEditorPage.tsx:79` ghost "Back to secrets" button → (PR #5303)
  shell trail.
- [x] **X2.4** (#5028) [verified] `EXT/UserTasks/TaskDetailPage.tsx:33` (also `:147`, `:149` [source]) ghost (PR #5303)
  "Back to task list"; `:34` [source] an `<a className="studio-button">` "Task inbox" →
  shell trail.
- [x] **X2.5** (#5029) [verified] `EXT/UserTasks/FormsPage.tsx:163` default "Back to forms" with no icon → (PR #5303)
  shell trail.
- [x] **X2.6** (#5030) [verified] `EXT/FeatureManagement/module.tsx:1048` raw "Back" button. (PR #5307)
- [x] **X2.7** (#5031) [source] `WF/ActivityDefinitionWorkbench.tsx:263` `StudioLink className="ad-back-to-designer"` (PR #5307)
  with a 15px arrow; the Elsa 3 import page uses `.ad-back`.
- [x] **X2.8** (#5032) [source] No way back at all: Agent playground ("Edit draft" default button, (PR #5307)
  `AgentPlayground.tsx:177`), Agent review, Agent invocation ("Open the run", `AgentInvocationPage.tsx:97`),
  and the Agent create form (`AgentsPage.tsx:293–306`, Cancel or Escape only).
- [x] **X2.9** (#5033) [source] No breadcrumb on `WF/ActivityUpgradeWorkbenchPage.tsx:296` and (PR #5307)
  `WF/Elsa3ReusableImportPage.tsx:479`, which are reachable only from Activity definitions.
- [x] **X2.10** (#5034) [source] Port `ScopeBreadcrumb` (`.wf-breadcrumb`, raw "Root" button, 13px chevrons) to (PR #5303)
  `StudioBreadcrumbs`. It navigates scopes inside one workflow, so D5 may not apply to it; ask before
  moving it into the shell. `needs: K7`
- [x] **X2.11** (#5035) [source] `WF/ActivityDataMemberBrowser.tsx:140` raw "Back" button. (PR #5307)

### X3. Dialogs

Four dialogs were measured [seen]:

| Dialog | Shell | Kicker | Close X | Footer buttons |
|---|---|---|---|---|
| Create workflow | `.wf-dialog`, radius 16 | none | no | 32px, `StudioButton` |
| Create activity definition | `.ad-dialog` | uppercase "DESIGN-OWNED REUSABLE ACTIVITY" | yes | 38px, raw |
| Create connection | `.connections-modal`, lighter backdrop | uppercase "NEW CONNECTION" | yes | 36px, raw |
| Edit view | `.wf-organization-dialog`, radius 0 | none | no | "Update view" is not primary |

Target for every dialog: `StudioDialog`, no kicker, X close, footer of Cancel then one primary confirm
at the kit size. All migration items below `needs: K1`.

Workflows:

- [x] **X3.1** (#5037) [source] `WF/workflow-editor/CreateWorkflowDialog.tsx:158–261` (`.wf-create-dialog`).
  Also replace `<div className="wf-alert" role="alert">` at `:239` with `StudioAlert`. (PR #5271, PR #5278)
- [x] **X3.2** (#5038, PR #5323) [source] `WorkflowRunInputDialog.tsx:50–110`. Footer has two unstyled buttons, so
  "Run workflow" has no primary emphasis.
- [x] **X3.3** (#5039, PR #5323) [source] `OperatorActionDialog.tsx:55–162`. Uses `data-tone` for destructive styling.
  `needs: K2`
- [x] **X3.4** (#5040) [source] `MoveWorkflowDefinitionsDialog.tsx:103–132`. (PR #5301)
- [x] **X3.5** (#5041, PR #5323) [source] `HttpTestRequestDialog.tsx:240–247`.
- [x] **X3.6** (#5042, PR #5323) [source] `ActivityVersionChangeDialog.tsx:94–226`.
- [x] **X3.7** (#5043, PR #5323) [source] Inline dialog in `WorkflowEditor.tsx:1611–1624`.
- [x] **X3.8** (#5044) [source] `SaveWorkflowTemplateDialog.tsx:16–35`. Backdrop lacks `role="presentation"`;
  footer is raw with `.wf-primary-button`. (PR #5287)
- [x] **X3.9** (#5045) [source] `WorkflowTagsDialog.tsx:47` and `:111`. (PR #5301)
- [x] **X3.10** (#5046) [source] `WorkflowViewsPage.tsx:152` (Edit view; uses `aria-label` where siblings use
  `aria-labelledby`; "Update view" is not primary [seen] and becomes "Save" under D2). (PR #5290)
- [x] **X3.11** (#5047) [source] `WorkflowLibraryControls.tsx:241`. (PR #5296)
- [x] **X3.12** (#5048) [source] `WorkflowFolderDeletionDialog.tsx:68–74`. Inline error is a
  `<p class="wf-dialog-error">` with an embedded raw "Reload preview" button → `StudioAlert` with action. (PR #5301)
- [x] **X3.13** (#5049) [source] `WorkflowFolderMutationDialogs.tsx` (9 raw buttons, no `StudioButton`). (PR #5301)
- [x] **X3.14** (#5050) [source] Folder picker drawer in `WorkflowFolderNavigation.tsx:686`
  (`.wf-folder-drawer`, bare `<strong>` heading). (PR #5301)
- [x] **X3.15** (#5051, PR #5323) [source] Incident details dialog in `WorkflowInstances.tsx:3149–3166`.

Activity definitions:

- [x] **X3.16** (#5052) [source] `WF/ActivityDefinitionCreateDialog.tsx:116` (`.ad-create-dialog`). (PR #5306)
- [x] **X3.17** (#5053) [source] `WF/ActivityDefinitionTestRunDialog.tsx:289`; text "Close" button at `:402`. (PR #5306)
- [x] **X3.18** (#5054) [source] `WF/ActivityDefinitionPublicationReview.tsx:491`. (PR #5306)
- [x] **X3.19** (#5055) [source] `WF/ActivityDefinitionManagementDialogShell.tsx:21`, shared by the Draft, Fork,
  Provider migration and Version lifecycle dialogs. Cancel sits on the left at `:41`. (PR #5306)
- [x] **X3.20** (#5056) [source] Remove dialog kickers: "Design-owned mutable work"
  (`ActivityDefinitionDraftDialog.tsx:76`), "Atomic parallel draft"
  (`ActivityDefinitionProviderMigrationDialog.tsx:64`), "Immutable publication"
  (`ActivityDefinitionPublicationReview.tsx:376`). `needs: D3` (PR #5306)

Host and Dashboard:

- [x] **X3.21** (#5057) [source] `H/modules/PackageFeedsPage.tsx:339` (`modules-upload-dialog`). Footer at
  `:367–368` has no primary submit. (PR #5314)
- [x] **X3.22** (#5058, PR #5312) [source] `DASH/native/ResourceDialogs.tsx:15` (`named-dialog`).
- [x] **X3.23** (#5059, PR #5312) [source] `DASH/HomeTaskGroup.tsx:166`; footer at `:197` is a lone raw "Cancel".
- [x] **X3.24** (#5060, PR #5312) [source] `DASH/WidgetFrame.tsx:178`; footer at `:192–193` uses raw buttons with
  `data-variant`.
- [x] **X3.25** (#5061, PR #5312) [seen] `DASH/HomePage.tsx:250` add-widget picker; text "Close" at `:264`.

Extensions:

- [x] **X3.26** (#5062, PR #5327) [source] `EXT/Connections/ConnectionDetailDialog.tsx:25–31`. Raw footer buttons at
  `:59–63`; no Close or Cancel in the footer. `needs: K2`
- [x] **X3.27** (#5063, PR #5327) [source] `EXT/Connections/ConnectionCreateDialog.tsx:448–483`. Uppercase eyebrow
  (`EXT/Connections/styles.css:31` [verified]). **[check]** the footer was not read.
- [x] **X3.28** (#5064, PR #5327) [source] `EXT/Secrets/SecretDialogs.tsx:30` (create) and `:183` (rotate); no X close.
- [x] **X3.29** (#5065, PR #5327) [source] `EXT/Secrets/SecretDetail.tsx:32` header with a text "Close" button.
- [x] **X3.30** (#5066, PR #5333) [seen] Feature management: two custom dialogs with differing eyebrows, text "Close"
  and a raw `primary` class. Locate them in `EXT/FeatureManagement/module.tsx`.
- [x] **X3.31** (#5067, PR #5333) [check] `EXT/UserTasks/TaskDetailPage.tsx` history (`user-task-history-dialog`) and
  reassign dialogs were not read.
- [x] **X3.32** (#5068, PR #5333) [source] `EXT/Decisions/TableEditor.tsx:243–245` hand-rolled `role="dialog"` column
  popover → `AnchoredPopover`.

Browser dialogs and busy labels (no kit prerequisite):

- [x] **X3.33** (#5069, PR #5333) [verified] `WF/workflow-editor/CollectionSlotEditor.tsx:42` `window.confirm` →
  `api.dialogs.confirm`.
- [x] **X3.34** (#5070, PR #5319) [verified] `DASH/native/NamedDashboards.tsx:275` `window.confirm` → `api.dialogs.confirm`.
- [x] **X3.35** (#5071) [verified] `BACK/BackendsPage.tsx:121` and `:145` `window.confirm` →
  `api.dialogs.confirm`; `:58` `window.alert` → inline `StudioAlert`. (PR #5324)
- [x] **X3.36** (#5072) [source] `EXT/Connections/ConnectionsPage.tsx:115` calls `dialogs.confirm` without (PR #5299)
  `title` or `confirmLabel`; Secrets passes both (`SecretsPage.tsx:187, 202`).
- [x] **X3.37** (#5073, PR #5327) [verified] Use "…" not "..." in busy labels: `EXT/Secrets/SecretDialogs.tsx:135, 198`;
  [source] `CreateWorkflowDialog` ("Creating..."), `WorkflowRunInputDialog` ("Starting..."),
  `EXT/Secrets/SecretPickerEditor.tsx:86`.

### X4. Create flows

Five patterns exist [seen]: modal dialog (workflow, activity definition, connection); inline page with
the primary on the left and Cancel on the right (agent); page with a wrapping card and tabs (secret);
page with header actions (dashboard, question); straight into the editor (decision, form).

Decided (D6): a name-and-description dialog, then the editor opens.

- [x] **X4.1** (#5077) [source] Agent: `EXT/Agent/AgentsPage.tsx:163, 293–306` inline form that replaces the (PR #5329)
  list; primary "Create and edit" before Cancel. `needs: D6`
- [ ] **X4.2** (#5078) [seen] Secret: remove the whole-page card on Create secret. [source] `CreateSecretDialog`
  also exists in `SecretDialogs.tsx:24`; keep one flow, which D6 makes the dialog. **[check]** that the
  secret form (it has tabs) fits a dialog; if not, ask. `needs: D6`
- [x] **X4.3** (#5079) [seen] New question: "Preview" is the filled primary rather than "Save question". (PR #5311)
- [x] **X4.4** (#5080) [seen] Decision and form: creation opens the editor directly. Add the dialog step.
  `needs: D6` (PR #5331)
- [x] **X4.5** (#5081) [check] Dashboard and question: creation is a page with a query builder, which is more
  than a name and description. Start both with the D6 dialog, then open the editor. (PR #5311)

### X5. Labels and capitalisation

- [x] **X5.1** (#5083) [seen] Rename to "New …" (D1): "Create workflow", "Create secret", "Create connection",
  "Create form", "Create tag", "Create dashboard". Apply “New …” to dialog titles as well. Keep “Create” for confirmations and version actions. `needs: D1` (PR #5297)
- [x] **X5.2** (#5084) [seen] Rename to "Save" (D2): "Save changes" (Runtime evidence settings, Secrets), "Save
  draft" (Forms, ETL, Feature management), "Update view". Activity availability, Decisions and Theme
  builder already say "Save". `needs: D2` (PR #5297)
- [x] **X5.3** (#5085) [verified] Route labels in `WF/module.tsx`: `:173` "Activity Definition upgrades" →
  "Activity definition upgrades" (D8); `:180` "Activity Definitions" → "Activity definitions", matching
  the nav label and page title. Leave `:200` "Executable Inspector" as it is (D14). (PR #5291)
- [x] **X5.4** (#5086) [seen] Sentence case: "Create Workflow", "Create Activity Definition"
  (`WF/ActivityDefinitionCreateDialog.tsx:117`), "Theme Builder", "New Activity Type Key"
  (`WF/ActivityDefinitionForkDialog.tsx:194`). Use the creation verbs in D1 for dialog titles. Leave "Test Run"
  (`WF/workflow-editor/WorkflowEditorToolbar.tsx:189`) as it is (D14). (PR #5297)
- [x] **X5.5** (#5087) [source] "User Tasks" → "User tasks": `EXT/UserTasks/TasksPage.tsx:46–47` and
  `EXT/UserTasks/module.tsx:22–24`. D14 specifies “User tasks”. `needs: D14` (PR #5289)
- [x] **X5.6** (#5089) [source] "Create activity" (`WF/ActivityDefinitionsPage.tsx:482, 512`) against the dialog
  title "Create Activity Definition". Use one string. (PR #5297)
- [x] **X5.7** (#5090) [source] Apply D7 to Delete/Remove: Theme builder `H/modules/ThemeBuilderPage.tsx:379,
  443, 489`, Backends `BACK/BackendsPage.tsx:121`, `WF/ActivityGraphImplementationEditor.tsx:270, 1363`.
  `needs: D7` (PR #5305)
- [x] **X5.8** (#5091) [verified] Apply D7 to Retry: "Try again" at `H/auth/studioAuth.tsx:114`; [source]
  `H/weaver/WeaverSurface.tsx:186`, "Retry mode change" `:117`, and the Definitions list variants
  "Retry current search", "Restart from first page", "Reload workflow support"
  (`WF/workflow-editor/WorkflowDefinitions.tsx:833–834`). `needs: D7` (PR #5305)
- [x] **X5.9** Withdrawn on 2026-11-21: not a defect. "Withdraw"
  (`H/weaver/WeaverMessageList.tsx:132`) withdraws the user's own unstarted message; "Deny"
  (`H/weaver/WeaverProposalReview.tsx:107`) rejects a proposal. They are different actions.
- [x] **X5.10** (#5092) [seen] Make route label, nav label and page title one string: "Import from Elsa 3",
  "Activity definition upgrades", "Task forms", "Runtime diagnostics" (D8). **[check]** "Console" /
  "Server console": find where "Server console" is shown before changing anything; it was not found
  in source. `needs: D8` (PR #5305)
- [x] **X5.11** (#5093) [source] "Secrets" capitalised mid-sentence:
  `EXT/Connections/ConnectionCredentialField.tsx:129, 141, 173`, `EXT/Secrets/SecretPickerEditor.tsx:86`. (PR #5305)
- [x] **X5.12** (#5094) [source] Decisions delete wording: "Move decision to recycle bin"
  (`DecisionsPage.tsx:144`), "Move to recycle bin" (`:258`), overflow "Delete", "Permanently delete
  decision" (`:164`). (PR #5305)
- [x] **X5.13** (#5095) [source] Saved views: "Delete saved view" (`WorkflowLibraryControls.tsx:253`) against
  "Delete" and "Apply" (`WorkflowViewsPage.tsx:117, 119`). (PR #5305)

### X6. Refresh

Definitions and Runs use the header split-button with an interval menu [seen]. Move every other
page-level refresh to `StudioPageHeader refresh=`. Per-widget refresh in `DASH/WidgetFrame.tsx` is not
in scope.

- [x] **X6.1** (#5097) [source] `EXT/Agent/AgentsPage.tsx:187`. (PR #5329)
- [x] **X6.2** (#5098) [source] `EXT/Agent/AgentPlayground.tsx:178–179` ("Reload draft", redundant `aria-label`). (PR #5329)
- [x] **X6.3** (#5099) [source] `EXT/Agent/HumanReviewPage.tsx:108` and `AgentInvocationPage.tsx:97`. (PR #5329)
- [x] **X6.4** (#5100) [source] `EXT/Connections/ConnectionsPage.tsx:146`. (PR #5299)
- [x] **X6.5** (#5101) [seen] `EXT/Secrets/SecretsPage.tsx:255` (labelled "Refresh secrets" in the running app). (PR #5299)
- [x] **X6.6** (#5102) [source] `EXT/Decisions/DecisionsPage.tsx:176`. (PR #5320)
- [x] **X6.7** (#5103) [source] `EXT/UserTasks/TaskDetailPage.tsx:168–171` ("Refresh task" inside the overflow
  menu). (PR #5325)
- [ ] **X6.8** (#5104) [seen] `WF/ActivityDefinitionWorkbench.tsx:267–268` (outlined button with icon).
- [ ] **X6.9** (#5105) [seen] Remaining lists with a plain "Refresh" button in header actions. Find them by
  searching for a `StudioButton` labelled "Refresh" outside `KIT`.

### X7. Status badges

Four badge styles were measured [seen]:

| Class | Height / weight / radius | Used by |
|---|---|---|
| `studio-status-chip` | 20px / 650 / 5px | Forms |
| `wf-status-badge` | 22px / 700 / 999px | Runs, definition versions |
| `secrets-badge` | 22px / 400 / 999px, lowercase "active" | Secrets |
| `settings-default-badge` | 21px / 700 / 9px | Appearance |

Replace each with `StatusChip` or `StatusPill` and sentence-case text.

- [x] **X7.1** (#5107) [verified] `WF/workflow-editor/DefinitionVersionCells.tsx:23` (also `:32`, `:35`) (PR #5322)
  `wf-status-badge`. See also W6.
- [x] **X7.2** (#5108) [source] `WorkflowStatusBadge` in `ActivityExecutionBrowser.tsx:78`, (PR #5334)
  `IncidentNextStep.tsx:154`, `ReusableBoundaryInspector.tsx`; `.wf-chip` "Template"/"Draft" in
  `WorkflowEditorToolbar.tsx:163`.
- [x] **X7.3** (#5109) [seen] `secrets-badge` in Secrets. (PR #5299)
- [x] **X7.4** (#5110) [seen] `settings-default-badge` in Appearance. (PR #5304)
- [x] **X7.5** (#5111) [source] `.ad-badge` (`WF/activityDefinitions.css:57`), `.ad-status-chip` (PR #5334)
  (`WF/activityDefinitionCollection.css:17`), `.ad-version-lifecycle` (`WF/activityDefinitions.css:111`),
  `.ad-validation-result` (`:203`).
- [x] **X7.6** (#5112) [source] `ConnectionStatusBadge` / `.connection-status` (PR #5299)
  (`EXT/Connections/connectionPresentation.tsx:12–16`).
- [x] **X7.7** (#5113) [source] `.decision-draft-status` pill (`EXT/Decisions/styles.css:22`). (PR #5320)
- [x] **X7.8** (#5114) [check] `.etl-run-status` in ETL. (PR #5326)

### X8. Tabs and view switches

- [x] **X8.1** (#5116) [verified] `WF/ActivityDefinitionWorkbench.tsx:276` hand-rolled boxed `.ad-tabs` with its (PR #5328)
  own key handler → `StudioTabs` (the draft editor already uses it at
  `ActivityDefinitionDraftEditor.tsx:777`).
- [x] **X8.2** (#5117) [verified] `WF/ActivityDefinitionContractEditor.tsx:785` `.ad-member-tabs` → `StudioTabs`. (PR #5328)
- [x] **X8.3** (#5118) [verified] `WF/workflow-editor/ReusableBoundaryInspector.tsx:393` `.wf-boundary-pane-tabs`. (PR #5328)
- [x] **X8.4** (#5119) [verified] `H/App.tsx:1180–1190` `bottom-panel-tab-list` hand-rolled tablist. (PR #5310)
- [x] **X8.5** (#5120) [verified] `EXT/Diagnostics.OpenTelemetry/module.tsx:244`. (PR #5298)
- [x] **X8.6** (#5121) [verified] `EXT/ExtensionBuilder/extension-builder/BottomDock.tsx:134`. (PR #5326)
- [x] **X8.7** (#5122) [source] `PanelTabList` (`.wf-panel-tab-list`) in `WorkflowInstances.tsx:2540`, (PR #5328)
  `WorkflowEditor.tsx:1283`, `graph-authoring/GraphAuthoringWorkbench.tsx:148`. Check whether
  `StudioTabs` has a dense mode before migrating.
- [x] **X8.8** (#5123) [seen] Dashboards and Questions "Mine / Shared with me": square 30px buttons with
  radius 0 → `PeerViewSwitch` or `StudioTabs`. (PR #5311)
- [ ] **X8.9** (#5124) [source] `EXT/Attention/AttentionWidget.tsx` (around line 20) `aria-pressed` filter
  buttons → `PeerViewSwitch`.
- [x] **X8.10** (#5125) [source] Selects used as view switches: `EXT/UserTasks/TasksPage.tsx:51–53` (field
  labelled "Inbox"), `EXT/Decisions/DecisionsPage.tsx:223` ("Decision version"). (PR #5325)
- [x] **X8.11** (#5126) [source] `EXT/Decisions/DecisionsPage.tsx:184–185` uses `StudioTabs` for Active / (PR #5320)
  Recycle bin where `PeerViewSwitch` is the peer-view primitive.

### X9. Loading, empty and error states

- [ ] **X9.1** (#5128) [source] Plain "Loading…" text → `SkeletonRows`:
  `EXT/Agent/AgentPlayground.tsx:107`, `HumanReviewPage.tsx:91`, `AgentInvocationPage.tsx:79`;
  `EXT/Secrets/SecretEditorPage.tsx:81`, `SecretsPage.tsx:305`;
  `EXT/UserTasks/TaskDetailPage.tsx:34, 150`, `FormsPage.tsx:53`;
  `EXT/Decisions/CasePanel.tsx:107`, `DecisionInputsEditor.tsx:141`;
  `EXT/Attention/AttentionWidget.tsx:12`; `DASH/WidgetFrame.tsx:88, 92` ([verified] `:88`).
- [ ] **X9.2** (#5129) [verified] Suspense fallbacks in `WF/workflow-editor/WorkflowDefinitions.tsx:816` and
  `:991` (also `:992`, `:994`, `:1014` [source]).
- [x] **X9.3** (#5130) [source] `WF/ActivityDefinitionWorkbench.tsx:283` and (PR #5309)
  `ActivityDefinitionCreateDialog.tsx:126` (`ad-inline-status`);
  `EXT/Connections/ConnectionList.tsx:32` (a `StudioResultState` titled "Loading connections…").
- [ ] **X9.4** (#5131) [source] Custom error blocks → `StudioAlert` / `StudioResultState`:
  `.ad-failure` (`WF/ActivityDefinitionWorkbench.tsx:204`),
  recovery and conflict cards (`WF/ActivityDefinitionDraftEditor.tsx:1068, 1069, 1079, 1134`),
  `.connections-notice` (`EXT/Connections/ConnectionsPage.tsx:136–141`),
  `.secrets-notification` (`EXT/Secrets/SecretsPage.tsx:369–374` [verified]),
  `EXT/Agent/HumanReviewPage.tsx` stale and receipt paragraphs (around line 106),
  `H/App.tsx:1895` (`backend-connection-notice`) and `:1911`,
  `H/workspace/WorkspaceTabPanels.tsx:92–93`, `DASH/WidgetFrame.tsx:89, 148`.
  [check] `.etl-error`, `.external-auth-error`. Host startup, workspace tabs, ETL and Identity completed in PR #5339.
- [ ] **X9.5** (#5133) [seen] BPMN pages show the raw string "This backend does not support
  elsa.api.bpmn-catalog." with no page header. Use an "unavailable" result state in plain language.
- [x] **X9.6** (#5134) [source] "Invalid secret address." (`EXT/Secrets/module.tsx:63`) and "Invalid task (PR #5303)
  address." (`EXT/UserTasks/module.tsx:16`) are plain paragraphs → `StudioResultState`.
- [x] **X9.7** (#5135) [source] `role="status"` on error text where `role="alert"` is right: (PR #5334)
  `WF/workflow-editor/AttachedRunActivityInspector.tsx:26`, `AttachedTestRunControls.tsx:25`.
- [x] **X9.8** (#5136) [source] `EXT/Decisions/DecisionsPage.tsx:179–180` shows success in a `StudioAlert`; (PR #5320)
  action results belong in `api.notifications.notify`.
- [ ] **X9.9** (#5137) [source] Empty states as bare paragraphs: `EXT/Attention/AttentionWidget.tsx:23`,
  `EXT/Workflows.Dashboard/WorkflowRunHealthWidget.tsx:66`, `WorkflowDashboardUnavailable.tsx:5`.

### X10. Timestamps and identifiers

Replace local date formatting with `StudioTimestamp`.

- [x] **X10.1** (#5139) [verified] `WF/workflow-editor/WorkflowInstances.tsx:842`. (PR #5334)
- [x] **X10.2** (#5140) [verified] `EXT/Connections/connectionPresentation.tsx:29` (`connectionDate`, used by (PR #5299)
  `ConnectionDetailDialog.tsx:41–42`).
- [x] **X10.3** (#5141) [verified] `EXT/Decisions/CasePanel.tsx:130`. (PR #5320)
- [x] **X10.4** (#5142) [verified] `EXT/UserTasks/FormsPage.tsx:204`, `FormsPageLifecycle.tsx:436`,
  `FormVersionWorkspace.tsx:48`, `TaskDetailPage.tsx:188`. (PR #5325)
- [x] **X10.5** (#5143) [verified] `EXT/Etl/EtlDefinitionsPage.tsx:648, 701`. (PR #5326)
- [x] **X10.6** (#5144) [verified] Identity `UserAccessPage.tsx:12`. (PR #5326)
- [x] **X10.7** (#5145) [seen] Questions list shows "11/21/2026, 1:29:59 AM" where other lists show
  "Nov 21, 2026". (PR #5311)
- [x] **X10.8** (#5146) [check] Console, OpenTelemetry and Structured logs modules. (PR #5298)
- [x] **X10.9** (#5147) [source] Drop the local `formatDate` fallbacks beside `StudioTimestamp`: (PR #5329)
  `EXT/Agent/AgentsPage.tsx:178, 314`, `EXT/Secrets/SecretsPage.tsx:325, 407`.
- [ ] **X10.10** (#5148) [source] Identifiers shown as plain text → `CopyableIdentifier`:
  `EXT/UserTasks/FormVersionWorkspace.tsx:43–46` (form id),
  `EXT/Secrets/CopySecretNameButton.tsx:37` (custom copy button). See also E2.

### X11. Raw buttons instead of `StudioButton`

Counts are raw `<button` occurrences. Canvas and context-toolbar buttons may be legitimately custom;
the designer header is a documented exception.

- [ ] **X11.1** (#5150) [verified] `WF/graph-authoring/CompensationScopeDesigner.tsx` (20).
- [x] **X11.2** (#5151) [verified] `WF/workflow-editor/WorkflowInstances.tsx` (19). (PR #5334)
- [ ] **X11.3** (#5152) [verified] `WF/graph-authoring/NativeBlockCommands.tsx` (12).
- [x] **X11.4** (#5153) [verified] `WF/Elsa3ReusableImportPage.tsx` (12). [seen] It renders unstyled 38px and (PR #5332)
  20px `.ad-primary-action` buttons.
- [ ] **X11.5** (#5154) [verified] `WF/ActivityPropertiesPanel.tsx` (12).
- [x] **X11.6** (#5155) [verified] `WF/workflow-editor/ReusableBoundaryInspector.tsx` (11). (PR #5334)
- [x] **X11.7** (#5156) [verified] `WF/ActivityDefinitionPublicationReview.tsx` (10). (PR #5316)
- [ ] **X11.8** (#5157) [verified] `WF/workflow-editor/WorkflowFolderMutationDialogs.tsx` (9).
- [x] **X11.9** (#5158) [verified] `EXT/FeatureManagement/module.tsx` (22). (PR #5338)
- [x] **X11.10** (#5159) [source] `WF/ActivityDefinitionWorkbench.tsx:264–268, 285, 342` (`ad-primary-action`, (PR #5316)
  "Fork recommended version", "Create parallel draft" shown twice).
- [x] **X11.11** (#5160) [source] `WF/ActivityDefinitionDraftEditor.tsx:983` raw "Save"; `:1068` "Discard (PR #5316)
  recovery".
- [ ] **X11.12** (#5161) [source] `WF/ActivityUpgradeWorkbenchPage.tsx` (5), `WF/ActivityAvailabilityPage.tsx`
  (6), `WF/ActivityDefinitionsPage.tsx` (8), `WF/ActivityDataChooser.tsx` (10),
  `WF/ActivityDataMemberBrowser.tsx` (7), `WF/CollectionValueEditor.tsx` (4),
  `WF/DictionaryValueEditor.tsx` (3), `WF/WorkflowLazyBoundary.tsx` (2).
- [x] **X11.13** (#5162) [source] `WF/workflow-editor/`: `WorkflowExecutables.tsx` (9), (PR #5334)
  `ActivityExecutionBrowser.tsx` (9), `WorkflowFolderNavigation.tsx` (8),
  `WorkflowLibraryControls.tsx` (7), `InspectorPanel.tsx` (6).
- [ ] **X11.14** (#5163) [source] `WF/graph-authoring/SequenceDesigner.tsx` (9),
  `BpmnInterchangeCommands.tsx` (6).
- [x] **X11.15** (#5164) [source] Weaver: `H/weaver/WeaverSurface.tsx`, `WeaverMessageList.tsx`,
  `WeaverProposalReview.tsx`, `WeaverComposer.tsx`, `WeaverPromptStarters.tsx`, `WeaverLauncher.tsx`. (PR #5310)
- [x] **X11.16** (#5165) [source] Host: `H/App.tsx:844, 1206, 1215`; `H/modules/ModuleManagementPage.tsx:298,
  311, 328, 408, 440`; `H/modules/PackageFeedsPage.tsx:170–177, 183, 221, 253, 290`;
  `H/modules/PackageMaintenance.tsx:97, 116`; `H/settings/AppearancePage.tsx:244`. (PR #5304)
- [x] **X11.17** (#5166) [source] Dashboard: `DASH/HomePage.tsx:237, 241, 299`, `DASH/WidgetFrame.tsx:62–69`. (PR #5313)
- [ ] **X11.18** (#5167) [source] Extensions: `EXT/Connections/ConnectionCredentialField.tsx:137`,
  `ConnectionPickerEditor.tsx:90`, `EXT/Secrets/SecretsPage.tsx:279`,
  `EXT/Secrets/SecretPickerEditor.tsx:90`, `EXT/UserTasks/fieldControls.tsx:53, 60, 80`.
- [x] **X11.19** (#5168) [check] ETL draft toolbar. (PR #5326)
- [x] **X11.20** (#5169) [source] Icon-only buttons named by `title` only: `H/modules/PackageFeedsPage.tsx:172,
  175`, `H/modules/ModuleManagementPage.tsx:440`. `needs: K3` (PR #5304)

### X12. Native `<details>` instead of `AdvancedSection` / `TechnicalDetails`

- [x] **X12.1** (#5171) [verified] `WF/ActivityDefinitionContractEditor.tsx:400, 566, 588` (and `:567` (PR #5316)
  [source]). "Advanced" appears three times with three class names.
- [x] **X12.2** (#5172) [verified] `WF/ActivityDefinitionCreateDialog.tsx:128`. (PR #5306)
- [x] **X12.3** (#5173) [verified] `EXT/Agent/AgentEditor.tsx:244`. `EXT/Agent/module.tsx:42` relies on (PR #5329)
  `closest("details")`; keep that focus behaviour working.
- [ ] **X12.4** (#5174) [verified] `EXT/Connections/ConnectionDetailDialog.tsx:50` → `TechnicalDetails`.
- [x] **X12.5** (#5175) [verified] `EXT/Decisions/CasePanel.tsx:130` (also `:135`, `:137`), (PR #5320)
  `DecisionInputsEditor.tsx:182` [source].
- [x] **X12.6** (#5176) [verified] `DASH/native/NamedDashboards.tsx:558` uses `<details>` as an action menu →
  `OverflowMenu`. (PR #5311)
- [x] **X12.7** (#5177) [verified] `H/settings/AppearancePage.tsx:121`. (PR #5304)
- [x] **X12.8** (#5178) [verified] `EXT/ExtensionBuilder/extension-builder/SolutionExplorer.tsx:153` (through
  about `:193`). (PR #5326)
- [x] **X12.9** (#5179) [seen] Workflow publication review: three native disclosure triangles. (PR #5316)
- [x] **X12.10** (#5180) [source] `WF/ActivityDefinitionContractProposalChangeList.tsx:52` ("Technical (PR #5316)
  comparison") and `ActivityDefinitionContractProposalReview.tsx:166` ("Technical details"): one label,
  `TechnicalDetails`.
- [x] **X12.11** (#5181) [source] `WF/ActivityAvailabilityPage.tsx:425` ("Raw metadata"), (PR #5316)
  `WF/Elsa3ReusableImportPage.tsx:716–743`, `WF/ActivityDefinitionPublicationReview.tsx:337–467`.
- [x] **X12.12** (#5182) [source] `DASH/native/QueryControls.tsx:121–131`, `DASH/native/ResultView.tsx:84`,
  `DASH/native/ResourceDialogs.tsx:124`, `H/weaver/WeaverMessageList.tsx:24`. (PR #5321)

### X13. CSS texture

Work file by file and run `pnpm lint:css` after each. Counts are grep counts on the audit date.

Uppercase labels (`needs: D3`):

- [x] **X13.1** (#5184) [verified] `WF/styles.css`: the token `--wf-label-transform: uppercase` at `:87` drives (PR #5309)
  12 `text-transform: var(--wf-label-transform, uppercase)` rules; two literal `uppercase` rules sit at
  `:6061` and near `:7547`.
- [x] **X13.2** (#5185) [verified] `WF/activityDefinitions.css:17` (`.ad-kicker`); [source] `:85`, `:211`, (PR #5309)
  `:275`; `WF/activityContractEditor.css:280–281`; `WF/activityDefinitionDependencyExplorer.css:29`;
  `WF/activityUpgradeWorkbench.css:578–580`.
- [x] **X13.3** (#5186) [verified] `H/styles.css:708, 1856, 2402, 2750`. (PR #5304)
- [x] **X13.4** (#5187) [verified] `H/weaver/weaver.css:37`; [source] `H/weaver/weaverControls.css:191, 251`. (PR #5310)
- [x] **X13.5** (#5188) [verified] `BACK/backends.css:39`. (PR #5310)
- [x] **X13.6** (#5189) [source] `H/navigation/navigation.css:21`, `H/components/UserMenu.css:86`,
  `H/components/ThemeSwitcher.css:167`, `H/themes/layouts.css:76`, `H/themes/signal.css:26`,
  `H/themes/elsaCloud.css:24`. (PR #5304)
- [x] **X13.7** (#5190) [verified] `EXT/Diagnostics.OpenTelemetry/styles.css:244, 311`. (PR #5298)
- [x] **X13.8** (#5191) [verified] `EXT/Diagnostics.StructuredLogs/styles.css:168` ([seen] table headers TIME /
  LEVEL / SOURCE). (PR #5298)
- [ ] **X13.9** (#5192) [verified] `EXT/Connections/styles.css:31`.
- [ ] **X13.10** (#5193) [source] `WF/workflow-editor/activityInspection.css:181`,
  `WF/workflow-editor/graphCanvas.css:453`, `WF/graph-authoring/compensationScopeDesigner.css:292`.
- [x] **X13.11** (#5194) [verified] `EXT/ExtensionBuilder/styles.css` (2 uppercase rules). (PR #5326)

Raw font sizes, weights, radii and colours:

| File | Raw font-size | Weight 700/800 | Raw px radius | Hex / rgba | Tag |
|---|---|---|---|---|---|
| `WF/styles.css` | 226 | 44 | 18 | 35 | [verified] font-size, [source] rest |
| `H/styles.css` | 57 | 18 | 12 | 5 | [verified] font-size, [source] rest |
| `EXT/FeatureManagement/styles.css` | 36 | 4 | 26 | 12 | [verified] |
| `WF/activityAvailability.css` | 25 | 0 | 4 | 1 | [verified] font-size, [source] rest |
| `EXT/Decisions/styles.css` | 21 | 0 | 1 | 0 | [verified] font-size, [source] rest |
| `H/weaver/weaver.css` | 26 | 1 | 3 | 2 | [source] |
| `EXT/Diagnostics.OpenTelemetry/styles.css` | 16 | 2 | 11 | 0 | [verified] |
| `EXT/Diagnostics.StructuredLogs/styles.css` | 16 | 5 | 2 | 15 | [verified] |
| `EXT/ExtensionBuilder/styles.css` | 14 | 12 | 1 | 1 | [verified] |
| `EXT/ConsoleStream/styles.css` | 4 | 0 | 1 | 57 | [verified] |
| `WF/activityDefinitions.css` | 0 | 12 | 3 | 0 | [source] |
| `WF/workflow-editor/activityInspection.css` | 23 | 1 | 0 | 0 | [source] |
| `WF/workflow-editor/boundaryInspector.css` | 20 | 0 | 0 | 0 | [source] |
| `WF/activityDataChooser.css` | 17 | 0 | 1 | 0 | [source] |

- [ ] **X13.12** (#5195) One task per row of the table above: replace raw values with `--studio-*` tokens.
  Many raw radii are `999px` pills; check each one before changing it.
  Console stream, OpenTelemetry and Structured logs completed in PR #5298 (Console chrome colors in PR #5294). Extension Builder completed in PR #5326.
- [ ] **X13.13** (#5196) [source] `WF/styles.css:12–36` maps `--wf-*` onto non-Studio variables (`--card`,
  `--muted`, `--border`) with hex fallbacks. Remap to `--studio-*` and drop the fallbacks.
- [x] **X13.14** (#5197) [verified] `EXT/ConsoleStream/styles.css` carries about 57 hex/rgba values;
  [source] it uses a private `--cs-*` namespace. (PR #5294; fixed ANSI SGR colors retain the documented exemption.)
- [x] **X13.15** (#5198) [source] Hex `iconColor` literals in navigation registrations: `H/App.tsx:150–157`,
  `WF/module.tsx:121, 126–136`, `EXT/Connections/module.tsx:23`, `EXT/Secrets/module.tsx:24`. (PR #5318)
- [ ] **X13.16** (#5199) [source] `EXT/Agent/styles.css:176` hex fallback `var(--studio-danger, #b42318)`.
- [x] **X13.17** (#5200) [source] `WF/Elsa3ReusableImportPage.tsx:806` uses an inline JS `styles` object; move (PR #5332)
  it to CSS with tokens.

### X14. Developer jargon in user-facing copy

D9 decided the Executable, evidence and Provider wording. X14.7 also needs the glossary change G1, and
X14.6 needs G2. For any other domain term, check `CONTEXT.md` first and ask.

- [x] **X14.1** (#5202) [seen] Activity definitions, Upgrades, Elsa 3 import: "Exact immutable version", (PR #5332)
  "Authoritative dependency closure", "Review authoritative plan", "Analyze immutable collection", "No
  Execute-Workflow fallback", "Immutable public contract", "Provider key" (becomes "Provider" under D9;
  see the **[check]** there).
- [x] **X14.2** (#5203) [source] `WF/ActivityDefinitionWorkbench.tsx`: "Design-owned mutable work", "Immutable (PR #5332)
  publication", "Unavailable contribution" (`:204`).
- [x] **X14.3** (#5204) [source] `WF/ActivityDefinitionContractEditor.tsx`: "Durable storage driver" (`:488`), (PR #5332)
  "Published baseline unavailable." (`:317`); `ActivityDefinitionContractProposalReview.tsx:166–168`
  "Exact proposal binding", "Provider"; `ActivityDefinitionContractProposalChangeList.tsx:51` "The
  opaque provider payload is intentionally not rendered."
- [x] **X14.4** (#5205) [source] `WF/ActivityDefinitionPublicationReview.tsx:418, 433` "Published head"; (PR #5332)
  management dialog busy label "Applying atomically…".
- [x] **X14.5** (#5206) [source] `WF/ActivityUpgradeWorkbenchPage.tsx`: "idempotency key" (`:429`), "Durable (PR #5332)
  apply receipt", "Outcome unknown"; `WF/Elsa3ReusableImportPage.tsx`: "Side-effect-free analysis",
  "Dependency-closed selection", "Final atomic review", "Durable import receipt".
- [x] **X14.6** (#5207) [source] Runs and designer: "evidence" throughout; "Attach the unchanged Test Run
  snapshot…" (`WF/workflow-editor/AttachedRunActivityInspector.tsx:25`); "Loading authoritative
  compatibility evidence…". D9 replaces "evidence" in UI copy, with “Run data” and “Run data detail”. `needs: G2` (PR #5300)
- [x] **X14.7** (#5208) [seen] Executables: "Executable" and "Artifact" name the same thing on one screen
  ("Artifact ID", "Artifact hash", "Artifact: {id}" filter chip). Use "Executable" for all of them
  (D9). `needs: G1` (PR #5300)
- [x] **X14.8** (#5209) [source] Dashboards: "Category / x axis" (`DASH/native/ResourceDialogs.tsx:164`,
  `NamedDashboards.tsx:523`), "From (inclusive UTC instant)" (`DASH/native/QueryControls.tsx:124`). (PR #5311)
- [x] **X14.9** (#5210) [source] `EXT/Agent/AgentEditor.tsx:229` "Model: none from …" → "No model selected". (PR #5329)
- [x] **X14.10** (#5211) [check] Extension Builder tooltip "Requires canBuild". (PR #5326)

## Phase 2: per-screen items

### Workflows module

- [x] **W1** (#5213) [seen] Runs: search input text is weight 700; every other list is 400. (PR #5322)
- [ ] **W2** (#5214) [seen] Workflow designer and activity designer headers differ. "Test Run" is borderless
  in one and outlined in the other; the ⋯ button is borderless in one and boxed in the other.
  **[check]** palette category headings appear to differ in size.
- [x] **W3** (#5215) [seen] Saved views row actions are four equal default buttons, one with an emoji pencil (PR #5322)
  ("✏️Edit"). Use an `OverflowMenu`.
- [x] **W4** (#5216) [seen] Definitions table: the "Published" column header is clipped behind "Actions" at (PR #5322)
  1440px.
- [ ] **W5** (#5217) [seen] Activity availability: raw 32px filter input and 16px-radius option cards.
- [x] **W6** (#5218) [verified] `WF/workflow-editor/DefinitionVersionCells.tsx:23–35` renders a published (PR #5322)
  version with `data-status="completed"` and retired with `"cancelled"`. Use a tone that matches the
  meaning.
- [x] **W7** (#5219) [verified] 15px toolbar icons at `WF/workflow-editor/WorkflowEditorToolbar.tsx:186, 189`; (PR #5322)
  [source] `WorkflowDefinitions.tsx:791, 862`; the kit rule is 16px. Also 14px and 15px in
  `EXT/Connections/ConnectionsPage.tsx:145–146` and `EXT/Secrets/SecretsPage.tsx:253–255`.
- [x] **W8** (#5220) [seen] Clicking an agent in Agents opens the Activity definitions workbench, and the (PR #5329)
  sidebar selection jumps to Library.
- [x] **W9** (#5221) [source] `WF/ActivityDefinitionsPage.tsx:495–496`: "Rows per page" select in the filter (PR #5322)
  toolbar. `needs: K6`
- [ ] **W10** (#5222) [source] `WF/ActivityDefinitionsPage.tsx:515` div-grid table with `role="table"` and
  `WF/workflow-editor/WorkflowDefinitions.tsx:867` `.wf-grid` → `StudioDataGrid`.
- [x] **W11** (#5223) [source] `WfListSkeleton` and `WfErrorCard` (`StatusViews.tsx`) are thin aliases over kit (PR #5322)
  components; use the kit components directly.
- [x] **W12** (#5224) [source] `WF/ActivityDefinitionDraftEditor.tsx:638` finds a tab by querying (PR #5328)
  `.wf-inspector [role="tab"]` and clicking the one whose text is "Inputs". Replace with state.
- [ ] **W13** (#5225) [source] Selects with only an `aria-label`: `WF/ActivityDefinitionContractEditor.tsx:456,
  473, 488`, `WF/WorkflowPropertiesView.tsx:192, 222`. Wrap in `StudioField`.
- [x] **W14** (#5226) [source] `WF/workflow-editor/WorkflowInstances.tsx`: 7 raw `<select|input|textarea>`. (PR #5334)
- [ ] **W15** (#5227) [source] `WF/module.tsx:126–136`: one module spread over four nav placements with
  inconsistent parenting; Upgrades and Elsa 3 import have no nav entry.
- [ ] **W16** (#5228) [source] `WF/ActivityDefinitionCreateDialog.tsx:127`: label "Description " with a
  trailing space, a duplicate `aria-label` and a `<small>Optional</small>`. Under D13 the optional
  marker goes. `needs: D13`
- [x] **W17** (#5229) [source] Local feedback as bare `<p role="status">` in (PR #5334)
  `AttachedRunActivityInspector.tsx:25–36`, `AttachedTestRunControls.tsx:12–25`,
  `ActivityExecutionBrowser.tsx:63, 246` → `ActionNotice` / `StudioAlert`.

### Extensions

- [x] **E1** (#5231) [source] Connections: a "Details" button duplicates the row link (PR #5299)
  (`ConnectionList.tsx:39, 46`). Replace both with `studioRowInteraction` on the row. `needs: D12`
- [ ] **E2** (#5232) [source] Secrets: show/hide toggle (`SecretDialogs.tsx:245`) → `RevealToggle`; the row
  copy button → `CopyableIdentifier`.
- [x] **E3** (#5233) [seen] Decisions: "Add rule" is a second filled primary beside header Save and is (PR #5320)
  pill-shaped (`TableEditor.tsx:166` [source]). [source] `CasePanel.tsx:101, 115` add more primaries.
- [x] **E4** (#5234) [seen] Forms: the editor is wrapped in a page card; "Publish" and "Cancel" sit inside the
  card while "Back to forms" sits above it; the page title is 12px. (PR #5331)
- [x] **E5** (#5235) [seen] Dashboards and Questions: labels sit above fields here but use the floating-legend
  style elsewhere; Questions has an inline "Delete" per row instead of an overflow menu; the footer
  reads "1 loaded questions". (PR #5313)
- [x] **E6** (#5236) [seen] Structured logs and Console: about ten same-weight toolbar buttons. Move the
  secondary ones into an overflow menu. (PR #5292)
- [x] **E7** (#5237) [check] ETL: one-word page descriptions ("Design", "Runtime"); list and editor share one
  page. (PR #5326)
- [x] **E8** (#5238) [verified] Feature management: the whole screen lives in one 2,298-line `module.tsx`. (PR #5338)
- [x] **E9** (#5239) [source] Agent review: "Deny" is a default-styled button beside primary "Approve" (PR #5329)
  (`HumanReviewPage.tsx:121–122`). `needs: K2`
- [x] **E10** (#5240) [source] Decisions: recycle-bin rows use inline buttons (`DecisionsPage.tsx:208`) while (PR #5320)
  active rows use `OverflowMenu`; "Unpublish" and "Move to recycle bin" are default buttons
  (`:257–258`). `needs: K2`
- [x] **E11** (#5241) [check] Forms list and editor share one route with no URL per form
  (`FormsPage.tsx:161`). (PR #5331)
- [x] **E12** (#5242) [source] Collection footers word counts differently: "N shown" (PR #5299)
  (`ConnectionsPage.tsx:157`) against "N agents".
- [x] **E13** (#5243) [source] Raw label + select without `StudioField` in the Dashboard query builder:
  `DASH/native/QueryControls.tsx:27–126`, `ResourceDialogs.tsx:49, 162–165`,
  `NamedDashboards.tsx:522–524`. (PR #5313)
- [x] **E14** (#5244) [source] `DASH/HomePage.tsx:191` field labelled "Refresh" is a refresh interval. (PR #5313)
- [x] **E15** (#5245) [source] Workflows dashboard widgets: custom metric
  (`EXT/Workflows.Dashboard/WorkflowDashboardMetric.tsx:24`) → `StatTile`; raw `<table>` at
  `WorkflowRunHealthWidget.tsx:147`. (PR #5321)
- [ ] **E16** (#5246) [source] Attention widget shows severity only as a border colour
  (`EXT/Attention/styles.css:8–10`); add a `StatusChip`.
- [ ] **E17** (#5247) [source] `/secrets` duplicates `/security/secrets`; the legacy route lacks
  `requiresBackend` (`EXT/Secrets/module.tsx:40–44`). **[check]** whether the legacy route is still
  needed.
- [ ] **E18** (#5248) [source] Row opening in the other lists (D12): Secrets uses an
  `<a class="secrets-row-link">` plus an "Edit" button (`SecretsPage.tsx:316, 328`), Decisions a raw
  `decision-link` button (`DecisionsPage.tsx:193–199`), Forms a raw `user-task-link` button
  (`FormsPage.tsx:169–179`). Move each to `studioRowInteraction`. **[check]** keyboard access, and
  whether "Edit" only duplicates opening the row. `needs: D12`
- [x] **E19** (#5249) [source] Remove the "Search" submit button and search as the user types (D11): (PR #5299)
  `EXT/Connections/ConnectionsPage.tsx:150–151`, `EXT/Secrets/SecretsPage.tsx:260–261`. `needs: D11`
- [x] **E20** (#5250) [source] Field markers (D13): drop "Comment (optional)" (PR #5329)
  (`EXT/Agent/HumanReviewPage.tsx:114`) and the "Optional." descriptions (`AgentsPage.tsx:300`,
  `AgentEditor.tsx:248`). `EXT/Connections/ConnectionCreateDialog.tsx:312` already uses the
  "(required)" suffix. **[check]** whether `StudioField` should render the suffix itself.
  `needs: D13`

### Host

- [x] **H1** (#5252) [seen] Sign-in error screen uses a blue button, red title and a different font from the
  rest of Studio (`H/auth/studioAuth.tsx:114` `studio-auth-retry` [verified]). (PR #5304)
- [x] **H2** (#5253) [seen] Theme builder has five header buttons and none is primary
  (`H/modules/ThemeBuilderPage.tsx:375–379` [source]). Make Save the primary and move the rest into an
  `OverflowMenu`. "Apply" at `:614` differs from "Save" at `:378`. (PR #5304)
- [x] **H3** (#5254) [seen] Modules empty state shows raw configuration keys as its only guidance. (PR #5304)
- [x] **H4** (#5255) Workspace tabs reuse transient create routes and evict the least recently visited clean background tab beyond 12. Unsaved and pinned tabs are protected. (PR #5302)
- [x] **H5** (#5256) [verified] `H/settings/SettingsShell.tsx:25` uses a `<select>` as the section switcher.
  **[check]** how it behaves at narrow widths. (PR #5304)
- [x] **H6** (#5257) [source] Hand-rolled menus and popovers: `H/components/UserMenu.tsx:40`,
  `H/components/ThemeSwitcher.tsx:57, 105`, `H/workspace/WorkspaceTabStrip.tsx:295–317`,
  `H/weaver/WeaverSurface.tsx:305–323`, the Backends switcher popover → `OverflowMenu` /
  `AnchoredPopover`. None of these was captured in the running app. (PR #5310)
- [x] **H7** (#5258) [source] Redundant `label={<><span>…</span></>}` wrappers and duplicate `aria-label` on
  `StudioField`: `H/modules/PackageFeedsPage.tsx:351–354`, `H/modules/ThemeBuilderPage.tsx:403–431`. (PR #5304)
- [x] **H8** (#5259) [source] `H/modules/ThemeBuilderPage.tsx:479–491` deletes an asset from a button.
  **[check]** that it goes through `api.dialogs.confirm`. (PR #5304)

## Documentation changes the decisions require

G1 and G2 change domain language; follow `docs/agents/domain.md`.

- [x] **G1** (#5009) `studio/CONTEXT.md` defines "Artifact Hash" as the identity of an Executable, and D9
  replaces "Artifact …" with "Executable …" in the UI. Change the glossary, or record that the UI
  label differs from the domain term, before X14.7. (PR #5276)
- [x] **G2** (#5010) `studio/CONTEXT.md` defines "Runtime Evidence" and calls "Runtime Value Evidence Level"
  user-facing, and D9 replaces "evidence" in the UI. Use the wording in D9 to update the glossary, before X14.6. (PR #5282)
- [x] **G3** (#5011) `studio/docs/ux/design-spec.md`: record the D14 exception to sentence case, for “Test Run” and “Executable Inspector”. (PR #5283)
- [x] **G4** (#5012) `studio/docs/ux/design-spec.md` or `studio/docs/module-ui.md`: record D1, D2, D7, D11, D12
  and D13 as label and interaction rules, so they outlive this backlog. (PR #5284)

## Follow-up audits

- [x] **A1** (#5261) Source audit of the extensions with no completed report: ETL, Extension Builder, Feature
  management, Identity, Console stream, OpenTelemetry, Structured logs, Weather sample, Weaver
  workflows, JavaScript and Liquid expression editors. (PR #5326)
- [ ] **A2** (#5262) Capture and review the surfaces listed as not captured under
  [Coverage gaps](#coverage-gaps).
- [ ] **A3** (#5263) Review dark mode, including dialogs.
- [ ] **A4** (#5264) Review narrow viewports.
- [x] **A5** (#5265) Run ETL, Extension Builder, External sign-in, User access and the Weather sample in a
  host that enables them, and review them visually. (PR #5326)
- [ ] **A6** (#5266) Finish reading the files listed as partially read under [Coverage gaps](#coverage-gaps).
