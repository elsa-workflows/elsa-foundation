# Validation receipt

The implementation was reconciled with main's authoring UX change #3427 (`4c79e3688`). No backend runtime or theme-store schema changed.

- Host: 598/598 tests across 53 files; discovery lists 598.
- Workflows: 2,673/2,673 tests across 197 files after reconciliation; discovery lists 2,673.
- Browser: 59/59 tests covering the new style matrix, existing layouts, theme-store selection and shell scroll containment, plus the independent reload regression (1/1). The matrix uses actual shell, authoring, runtime inspection and list components with the repository's isolated HTTP fixtures. It is UI/state evidence, not a real backend execution qualification.
- Workspace typecheck and full lint pass (56 existing ESLint warnings, zero errors); both host production bundles and the Workflows production build pass.
- Workflows budgets remain unchanged: primary JavaScript 107.30/127.50 kB, CSS 168.91/185 kB, authenticated Definitions 383.39/384.50 kB, activity upgrades 374.54/376 kB, largest chunk 357.09/500 kB. Panel-only CSS was moved into its deferred owning components.
- Foundation architecture: 592/592 tests, zero skips; discovery lists 583 entries because two non-serializable MemberData theories expand from two entries to eleven cases at execution (five domain features and six collectible metadata factories). Generated solution-filter check passes. Architecture restore used the shared build-slot wrapper.

Commands ran from `Studio/` unless stated otherwise:

```sh
pnpm install --frozen-lockfile --offline
# In each affected host/workflow Client directory:
pnpm exec vitest run --maxWorkers=2 --minWorkers=1
pnpm exec vitest list --json
# From Studio/:
pnpm typecheck
pnpm lint
pnpm --filter @elsa-workflows/studio-web build
pnpm --filter @elsa-workflows/studio-workflows build
pnpm test:browser tests/browser/workspace-appearance.spec.ts tests/browser/theme-layouts.spec.ts tests/browser/theme-source-modes.spec.ts tests/browser/studio-scroll-containment.spec.ts --workers=2
# From the repository root:
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --nologo
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-build --no-restore --list-tests --nologo
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Eight new mounted appearance regressions failed before implementation and passed afterward. A deliberate source mutation subsequently removed the hidden-filter reset, short-menu scrolling and graph focus outline; all three rendered regressions failed, then passed with the implementation restored. No retries, quarantines or skips were introduced. Initial locator failures were corrected to the actual button/radio roles and the display-name label that includes its character counter.

Independent PR review round 1 found two further defects: neutral button treatment replaced semantic confirmation fills, and a legacy preset-specific publication correction did not follow an independently selected palette. Both gained rendered regressions in all three styles. The correction preserves semantic fills and uses one contrast-safe publication text mix across palettes, removing the palette-specific component selector. Contrast is checked after the dialog's entry animations finish. Existing settings and menu keyboard tests now follow the explicit preset disclosure and independent palette semantics. Final aggregate counts and review status are on the PR.

An additional ad hoc host-wide `tsc` probe fails on existing SDK resolution, asset/env declarations, agent diagnostics types, component barrel default export and React ref types. Those offending declarations are unchanged from main. The supported workspace typecheck and host Vite builds pass; the ad hoc probe is not reported as green.

Rendered screenshots use the same Drift palette to make workspace differences comparable: [Quiet Studio](quiet-studio.jpg), [Focus Canvas](focus-canvas.jpg), [Open Studio](open-studio.jpg), and the [light list](open-list-light.jpg). The developer's running Studio host was not restarted. Exact PR revision, independent-review results and hosted CI state are recorded on the PR.

## Draft editor header follow-up, 30 October

Owner feedback identified the competing back label, uppercase Draft eyebrow, title and green save pill in the Activity Definition draft editor. The header now groups an accessible back arrow with the title and a secondary `Draft · Saved` line. Publication no longer reserves a blank feedback row; secondary actions share one row beneath publication on narrow screens. Long titles wrap instead of being truncated. Existing navigation locks, save failures/conflicts, publication and exact identities remain unchanged. Obsolete graph-only header rules were removed in favor of the owning authoring stylesheet.

- The new rendered geometry regression fails on the original header: save status appears at y94.73 while the title ends at y118.30. It passes with the repaired hierarchy.
- Complete Workflows suite: 2,673/2,673 across 197 files, no skips. Complete authoring browser spec: 12/12, including six new style/mode cases, desktop and narrow long titles, keyboard focus and visible action bounds. Existing variable, contract-editing and unavailable-capability journeys still pass.
- Full workspace typecheck and lint pass (the same 56 existing warnings, no errors); Workflows production build and unchanged bundle budgets pass. Authenticated Definitions 383.39/384.50 kB; Activity upgrades 373.73/376.00 kB.
- The .NET Studio host builds with zero warnings/errors; architecture graph restore and 592/592 architecture tests pass; generated solution filters are current.
- The owned preview on port7031 was rebuilt and restarted. A fresh signed-in browser opened the owner's exact draft on the real backend without network or page errors; its title and `Draft · Saved` status were inspected. Only the owned preview was restarted. [Actual live header, Classic Dim](activity-draft-header-dim.png).

Independent review of the updated head and its hosted checks are tracked on PR #3449. The separate baseline test-host repair remains PR #3460; pending gates are not reported as passed.

## Expanded property editor stacking follow-up

The expanded property editor was rendered inside its inspector row. In Focus Canvas the inspector is a stacking context below the resize handles, so the dialog's existing z-index could not place it above those handles. The dialog now portals to the nearest resource token/page root, preserving React state and scoped theme variables while escaping the inspector's stacking and containment. Standalone panels fall back to their owner document's body. No z-index was increased.

- A rendered Activity Definition / Focus regression fails before the portal repair: `elementFromPoint` reaches a resize handle within the dialog's rectangle. All eight new workflow/activity × legacy/Quiet/Focus/Open cases pass afterward. They verify handle interception by the dialog or scrim, theme-colored background, Escape/Close behavior and edited expression persistence across reopening.
- The complete adaptive-panel, expression-code-intelligence and dictionary browser specs pass: 27/27, no skips. The complete Workflows unit suite passes 2,673/2,673 across 197 files. Existing standalone test queries now follow the portaled dialog; the property-group fixture supplies the same scoped token root as an embedded resource. Value, validation and masked-input assertions were retained.
- Full workspace typecheck and lint, production build and unchanged bundle budgets, .NET host build, architecture 592/592 and generated-map check pass.
- The owned preview at port 7031 was rebuilt. A fresh signed-in browser opened the owner's actual While / Condition dialog in Focus Canvas: both resize-handle positions hit the dialog or its scrim, with no page/network errors. No expression was edited in that live verification.
- [Rendered Focus Canvas property editor fixture](activity-property-overlay-focus.png). The committed capture uses synthetic fixture data; the live check retains no authored expression in published evidence.

The initial unhandled-Escape probe was corrected to focus the dialog's Close button before pressing Escape: the code editor intentionally consumes its own Escape shortcut. Eight older unit queries initially assumed that expanded content stayed within the property row's render container; they were reconciled to the scoped overlay owner without changing their behavioral assertions. Independent review and exact-head CI results are recorded on PR #3449.

### Modal interaction corrections from review round 4

Correctness review found that the initial portal escaped the inspector's disabled fieldset. Resource read-only state now flows explicitly through both normal and intrinsic inspectors into each property row, including its memo comparison. Portaled editors disable their contributed and native controls when a draft locks, while Close remains usable. The panel also rejects late editor callbacks using the current lock state. A new mounted regression fails without the lock propagation; removing the late-callback guard independently makes it fail as well.

A further rendered probe showed that a nested syntax popup's default body portal put it below the newly lifted modal. `AnchoredPopover` now accepts an optional portal container; the property picker supplies its own dialog, preserving focus and layering. Its default destination stays undefined until a modal scope exists, avoiding extra closed-popover layout effects when an unscoped trigger first mounts. Existing popovers keep their body destination. A new host test covers the optional container, and all eight browser cases now hit-test and activate the nested menu as well as the modal.

Final local evidence:
- Workflows: **2,674/2,674**, 197 files, one worker. Host: **599/599**, 53 files. No skips or quarantines.
- Complete adaptive-panel, expression-code-intelligence and dictionary browser specs: **27/27**.
- Workspace types and full lint pass; the same 56 existing warnings remain. Both host bundles/vendor assets and the Workflows production build/budgets pass. Studio .NET host builds with zero warnings/errors; architecture **592/592** and generated filters pass.
- Fresh live preview verification opened the actual Condition dialog and its syntax popup: handle positions hit the dialog/scrim and the popup option receives its own hit-test, with zero page/network errors. No expression was changed.

The first post-correction Workflows runs exposed an async disclosure assertion while the absent popup destination changed from undefined to null as its ref mounted. The absent destination is now normalized, and the unchanged six-test options suite and complete serial Workflows suite pass. Those earlier failures remain in the review history; no assertion was relaxed or test skipped. Exact updated-head review and hosted checks remain recorded on the PR.

### CI and review follow-up (30 October)

The hosted browser job at `a343886` failed eight publication-geometry cases and two snapshot-editor count assertions. The same ten failures reproduce locally. The publication controls now use stable grid columns with feedback in a final row only when needed; a two-line narrow heading prevents a draft-label edit from moving the actions. All eight original 320/768/1024/1440 light/dark geometry assertions pass unchanged. The snapshot assertion now scopes its count to the workflow JSON editor, while additionally rejecting any editable inspector editor in the captured snapshot.

Code Review's remaining theme findings were verified and addressed together. Legacy elevation, type and legend recipes apply only without an explicit workspace style, using a zero-specificity `:where` guard so high-contrast precedence is preserved. Stable `--studio-border` keeps the palette's control boundary; decorative separators continue using `--studio-edge-subtle`. Workspace previews use an explicit prop rather than the accessible group name. A definitively removed palette clears the override and inherits the base preset, while delayed and failed stores continue preserving unresolved custom ids.

Six rendered cases across all styles and light/dark fail before the recipe isolation and pass afterward, comparing the same Meridian palette under Classic, Signal, Atelier and Elsa Cloud base presets. They also check the legacy control boundary. A mounted removed-palette regression fails before the preference correction and passes afterward. Complete Host tests: **600/600**, 53 files. Existing source-level legacy theme assertions now include the workspace exclusion while retaining their recipe/density checks. Ref-based portal destinations are explicitly documented as interaction-time reads; standalone embeddings use the existing `.wf-tokens` scope contract in `styles.css`, with document inheritance only when no resource scope exists.

The first complete browser follow-up executed 129 cases: 123 passed; six header probes still measured the wrapper changed to `display: contents`. Those probes now measure the visible publication button, preserving the compact-height assertion. The final complete rerun of the five affected specs passes **129/129**. Complete Workflows: **2,674/2,674**, 197 files. Workspace typecheck, full lint (zero errors, 56 existing warnings), both host production builds, Workflows build and unchanged bundle budgets pass. After integrating the cleanup branch, the Studio .NET host builds with zero warnings/errors, architecture passes **592/592**, and generated-map checks pass. Exact-head hosted results are recorded on PR #3449. Earlier failed runs remain in the qualification history. No tests were skipped, quarantined or given weaker geometry tolerances.

### Acceptance round 6: active navigation contrast

Acceptance review at `86e4be338` found one P2: the selected navigation label used the accent as text over an accent wash. In the real shell, Meridian Dark measured 3.60:1 and Dim 2.59:1. Six new axe color-contrast cases (Quiet/Focus/Open × Dark/Dim, expanding Focus navigation) independently reproduce all six failures before the fix. The selected label now uses the readable semantic text token; all six pass afterward. The complete five-spec browser rerun passes **135/135**; complete Host **600/600**, full lint and host app/vendor builds pass again, and the .NET host rebuild has zero warnings/errors. Workflows 2,674, architecture592, typecheck, maps and unchanged Workflows budgets remain as verified above; their code did not change in this correction. The refreshed7031 preview returns HTTP200.

Correctness and standards reviews at `86e4be338` were clean. Acceptance independently passed nine mounted and fourteen geometry/presentation cases, and reported the contrast finding. Its first browser probe lost a shared fixture server; a controlled rerun passed, so the interrupted probe is not counted as geometry evidence. Root's final135-case rerun owns a separate4186 fixture server. The author's loop ends at round6 with the verified contrast fix submitted for fresh formal Code Review; no clean review of the earlier head is treated as review of this newer correction.

### Packaged consumer menu qualification follow-up

The Studio .NET job for head `31d666b3f` passes all **637** .NET tests and archive/API/Hosting controls, then fails composition theme-restart qualification at the browser selector. Run `40993133930`, job `123859999034`, artifact `12950348574` preserves the failing observation: `Composition owned` is queried as `menuitem`, while the redesigned color picker correctly exposes `menuitemradio`. The candidate archives record the workflow merge commit `c67e62d39f6558e94e4710f01d2d4776594c0080`.

The driver now queries the correct accessible role and additionally requires `aria-checked=true`. Existing root/deep-route markers, shared React/SDK, host identity, persisted default, uploaded material bytes and unchanged source checks remain. Using the exact original failing hosted Studio/Modularity archives, targeted actual package-only Themes and reference-style consumers both pass the existing restart journey with fresh browsers, no browser/missing-asset errors and no consumer frontend invocations. Build feed, consumer sources and caches are hidden before the restart journey. This is targeted local evidence, not a claim that the complete hosted consumer aggregate passed; fresh CI must run the complete aggregate.

Formal review round3's Low toolbar-grid note is documented next to its four columns. Its navigation nit is verified in all six Meridian Dark/Dim workspace cases: selected navigation keeps `aria-current=page`, its wash differs from an inactive hovered row, screenshots show the distinct treatments, and axe contrast remains clean. The fixture now loads the actual Settings stylesheet when directly embedding Appearance. The complete workspace-appearance spec passes **60/60**, and full lint passes with zero errors and the same56 warnings. The consumer driver passes Node syntax validation. Product behavior is unchanged by this follow-up; changes are qualification and a CSS comment. Fresh formal review is requested for the new head rather than restarting the capped author loop.

[Rendered Meridian Dim navigation](navigation-selected-hover-meridian-dim.png) shows the selected Settings row and hovered Home row together, with the real Appearance styles loaded.

### Formal round 4 follow-up: settled hover observation

The Low timing note applies to the existing `.nav-section a { transition: all var(--transition-fast) }`, not only a possible future transition. An isolated fixture mutation forces inactive hover to use the selected accent wash. All six earlier assertions incorrectly passed while that transition was in flight. Waiting for the hovered element's animations to finish makes the same six mutations fail specifically at the background equality assertion. With the mutation removed, the complete actual workspace spec passes **60/60**, including the six contrast/selection/hover cases; full lint passes with the same56 warnings. The temporary mutation fixture was removed, and the committed screenshot now shows the settled hover state. Product CSS did not change.

Dependency #3460 has merged as `927f91e517596bf2c129c704d11d91eaf671074e`, with all required and Studio checks green on reviewed head `5c7bc2848`. Its permitted merge commit preserves that head as the redesign's common ancestor, and its main content tree equals the already integrated dependency tree. No source-identical base replay is needed; cleanup files have dropped out of the redesign diff. Post-merge main CI is a separate pending gate.

### Hosted removed-palette assertion timing

Head `f93f9f609`'s frontend job (`40997038339`, job`123872736453`) executes Host600 and fails only the removed-palette case: preferences were already cleared but the follow-up DOM effect still showed Classic. The harness's three zero-delay timer turns do not establish that React's store reconciliation and DOM effect have committed. The async case now waits for the exact existing storage-clear and Drift-palette assertions together, adding the Drift base-theme assertion; production theme logic is unchanged, with no increased timeout or relaxed expected value.

All nine appearance cases and the complete Host suite **600/600**, 53 files, pass locally with the corrected assertion. Mutating the provider to retain the removed palette ID after the definitive response fails the corrected case with the same Classic-versus-Drift error; restoring it yields green. The temporary provider mutation is restored. Full lint passes with zero errors and the same56 warnings. An initial `act` experiment was discarded because the Host Vite configuration defines React production mode, where `act` is unavailable; its failures are not counted as regression evidence, and no test-runtime/product configuration was changed.

The existing hosted full browser job at the previous head is still running. The local correction is held for batching until that job finishes before requesting the final available formal review round; the author loop remains capped and is not restarted.

The retained hosted browser observation completed successfully at `f93f9f609`: run`40997038339`, job`123872736424`, **580 passed**, **6 existing opt-in screenshot cases skipped** (29.8minutes). No skip/fixme directives were added or changed in this PR. That result clears the remaining browser observation for batching; the prepared palette-assertion correction is now submitted for fresh exact-head CI and formal round6. Earlier-head browser and .NET/package success are retained as diagnostics, not substituted for the next head's required checks. Main is still `927f91e5`; no additional base integration is needed.

### Main integration after the completed review loop

The previous reviewed head `7404d271d` reached green `Build & test`, `Generated maps fresh`, `Full tier`, and all three Studio jobs. Later main commit `e81401851b077a37f8e5c6f20e38fc1864a26705` (#3448, module-owned Weaver contributions) introduced an adjacent-import conflict in `App.tsx`. Merge commit `40c689ebdc2af7bcfca36e0b18965b5f49649a51` keeps both the workspace stylesheet and the current Agent import. All other intervening main changes, including list, variable-button and slot-designer work, are retained.

Combined-source local verification:
- Complete Host **644/644**, 55files; Workflows **2,699/2,699**, 198files.
- Six complete affected rendered specs, including assigned slot designers: **159/159**.
- Studio .NET project **642/642**, zero failed/skipped; host build zero warnings/errors.
- Architecture **592/592** after the prescribed locked Release/Debug graph restore; generated filters pass. The first attempt omitted the new Agent project's evaluated Debug assets and failed three EF guards; no guard was changed, and that failed attempt remains recorded.
- Workspace typecheck, full lint (zero errors,53 existing warnings), host app/vendor and Workflows builds pass. Against current main's budget settings, authenticated Definitions is384.02kB/387.50kB and upgrades374.52kB/379.00kB; no budget limit was changed by this integration.
- Refreshed7031 preview verifies the actual Condition dialog and nested syntax popup: both resize handles covered by dialog/scrim, menu option receives its hit-test, zero browser/network errors. No expression changed; developer7030/Workbench7243 were preserved.

All six formal reviews approved the earlier reviewed commits, with no blocking finding left at round6. This merge necessarily changes the head and therefore needs fresh exact-head review and CI. The org protocol limits the loop to six rounds; this draft is prepared and tested for an owner decision authorizing one additional review of the integration, without silently resetting either review counter or reusing the previous head's clearance.
