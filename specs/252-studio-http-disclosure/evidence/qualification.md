# HTTP disclosure qualification

Local verification date: 2026-10-20. Initial implementation: `2149ad842254d0acd24e6fc314b7334fdf3c02c4`; round-1 corrections and source-built UI/runtime qualification: `cdc894e06478a2b1e711ec716a0818747ff7b833`. Base includes `main` update `33c33214f16ca8c157d2af99ab31faecb90cb9a9`. Subsequent narrow-layout hardening is qualified separately below; the composed runtime receipt remains tied to its stated source. Delivery receipts and exact-head hosted checks belong on PR #3100 and #3064.

## Completed local gates

| Surface | Command / result |
|---|---|
| Workflows | `pnpm --filter @elsa-workflows/studio-workflows test`: 2,059 passed across 161 files after round-1 fixes |
| Web | `pnpm --filter @elsa-workflows/studio-web test`: 561 passed across 49 files after round-1 fixes |
| Focused disclosure/options | 124 passed, including mounted-editor selection, current semantic errors, dictionary instances, collection removal, cleanup and JavaScript/Variable binding preservation through configured collapse |
| SDK copies | Five isolated TypeScript consumer compilations passed, including Connections; these replace 12 source-text assertions. A missing-member mutation failed and equivalent nullable-union formatting passed; declaration restored |
| Workspace types | `pnpm typecheck`: passed |
| Lint/layout | `pnpm lint`: zero errors; 48 existing warnings; CSS/layout checks passed |
| Frontend build | `pnpm --recursive --workspace-concurrency=1 build`: passed, including Web bundle budgets |
| Browser fixture | HTTP progressive-disclosure spec: 12 passed on macOS and Linux ARM Chromium 1.61.1; keyboard and full-page axe at 290/320/768/1024/1440 px in Light/Dark, plus laptop method/configuration/error recovery |
| Design backend | Release whole Design suite: 679 passed, zero skipped |
| Design API | Release whole Design API suite: 37 passed, zero skipped |
| Studio/Workbench | Both rebuilt at clean correction commit; Studio zero warnings/errors; Workbench 91 existing warnings, zero errors |
| HTTP runtime | `pwsh -NoProfile -File e2e-tests/http/Test-SendHttpRequestStatusOutcomes.ps1 -BaseUrl http://localhost:5695`: 2/2 passed, matching and unmatched published status routes |
| Architecture | Evaluated project graphs restored in locked mode; whole Release suite 578 passed, zero skipped; TRX reconciles all 569 discovery entries to 578 executed cases |
| Generated maps | Initial check identified only spec inventory/status changes; refresh reviewed, then `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` passed |

Commands are rooted as described in [quickstart.md](../quickstart.md). Whole Workflows/Web, compiler guard, types/lint, browser and builds were rerun for the review fixes. The Design/Design API/architecture results above precede those frontend-only corrections; their C# source/project graphs are unchanged. Exact-head hosted CI remains required. Root reviewed code/test repetition and extracted shared signal transitions and browser setup rather than duplicating selectors or fixtures.

## Real composed Studio

Studio and Workbench were rebuilt from the same clean correction source, then launched on owned ports 5689/5695 with new disposable content roots. No database existed in either root before first start. Existing unrelated hosts were preserved. Process IDs, DLL/config hashes, startup logs, source SHA and database preflight remain in the local execution receipt. The activity palette reported the exact `1.0.0+cdc894e...` catalog version. HTTP status-routing E2E passed again against these new hosts.

In disposable draft `14V8rt19Xfy`, node `sendhttprequest-638a1dde`:

- A newly added HTTP activity exposed URL, Method, Content and Connection directly. More options initially collapsed; the synthesized `text/plain` default did not count as authored configuration.
- At 1365 × 768, Connection remained within the first viewport. Enter/Space opened and closed More options.
- More options exposed Content Type, Request Headers, Expected Status Codes and Timeout. Authored `application/json` and `00:00:45` counted as two configured fields. Space successfully collapsed the configured section with its two-set badge still visible.
- While collapsed, GET → HEAD → POST → GET preserved the body `Reviewed body survives collapse and methods` and both advanced values. Reopening, Save, full page reload and node reselection retained all three values. The configured section initially reopened after reload, and Space could close it again.
- A keyboard-resized 301 px inspector had `clientWidth === scrollWidth === 300`; controls remained accessible without horizontal overflow. Root visually reviewed the final laptop Light screenshot and narrow Dark screenshot. Temporary viewport, inspector width and colour mode were restored afterward.

The initial composed check at `5e024ad...` chose a plain text fallback for the nullable Timeout descriptor (and observed status item), so an attempted malformed timeout did **not** produce a local duration error. It was replaced with a valid value. Do not cite that attempt as a reproduced private-editor error. The final build retains that existing editor-selection behavior. Built-in TimeSpan/Numeric invalid/recovered behavior is proven separately through the real-panel component/browser tests; current semantic errors, stale callbacks and unobservable-editor safe visibility also have focused regression coverage. This presentation change does not add a new validator or alter server-side parsing.

Final composed-app screenshots are retained locally as `3100-after-configured-collapsed-dark.jpg`, `3100-after-configured-collapsed-light.jpg`, `3100-after-collapsed-narrow-light.jpg` and `3100-after-collapsed-narrow-dark.jpg`. They are distinct from the browser fixture artifacts. Captures are native 1365 × 768 JPEGs; no 2× scale claim is made. Initial `3064-after-*` screenshots document the pre-review correction state; baseline screenshots from the prior owned runtime are not candidate attestation.

## Regression evidence and limitations

Retained expected-red evidence covers the original hierarchy/Connection tier, private validation reporting, effective mounted-editor/context selection, current semantic error visibility, two simultaneously mounted dictionary editor callbacks clearing one another, and configured-collapse failure in JavaScript/Variable preservation and the shared primitive. Round 1 adopted the same optional `allowCollapseWhenSet` contract present in held #3093, with other callers' defaults unchanged. Errors/fallback/failures still force visibility. The final focused suite and whole suites above pass. The StrictMode replay and dictionary blank-key open/close hypotheses passed before their proposed fixes, so neither is reported as a reproduced defect.

An additional noncanonical Web `tsc --noEmit -p tsconfig.json` probe reports 88 existing diagnostics in untouched tests/vendor entrypoints. The same command against exported base `825aa98dd624295655576f7ed299f6146b991bc8`, with the same installed dependencies, produced the exact same 88 diagnostic lines. Web has no package typecheck script; the canonical workspace typecheck above passes. This extra probe is an existing baseline limitation, not passing evidence.

Architecture discovery has two deferred theories: `Canonical_domain_api_depends_on_capability_aggregation_and_registers_one_stable_declaration` expands from one discovery entry to five executions, and `Every_collectible_api_explorer_artifact_is_rejected` expands from one to six. The TRX contains every discovered method, no unexpected methods, 578 passed outcomes and no skipped result. This explains the nine-case difference rather than treating the smaller list as a passing execution count.

The generated-map diff adds Spec 252 and increments the inventory from 247 to 248; no new architectural findings were introduced. Final independent-review/hosted-CI/merge receipts belong on the PR. Post-merge CI remains a separate gate from local qualification and PR CI.

## Narrow-layout correction after hosted CI

Hosted Studio CI at `14f495e4dea36c79ddfcdb61e43d94cee6dfd2d4` failed two of 220 Chromium cases: the 320 px Light/Dark fixture inspector overflowed internally; page-level width assertions passed. The fixture uses production property editors, but its outer and inner padding leave less space than the maximized composed inspector, so that result alone does not establish a composed Workbench overflow at 320 px.

A local geometry probe reproduced the issue at 305 and 290 px: the Connection header and native-select wrapper retained a roughly 264 px intrinsic width; at 290 px the inspector had `clientWidth=256`, `scrollWidth=280`. Explicitly sizing the property-row grid track with `minmax(0, 1fr)` and allowing the header to wrap removed overflow without clipping or removing controls. The new permanent 290 px Light/Dark cases both fail the existing internal-overflow assertion without these rules and pass with them. The full 12-case HTTP spec passes on macOS and official Playwright 1.61.1 Linux ARM Chromium. Linux ARM passed the original 320 px cases even before correction, so it is not represented as an exact reproduction of the hosted Linux failure. Fresh hosted CI must confirm that environment.

Root visually checked the 290 px Dark fixture: metadata and controls wrap while the Connection select remains within the inspector. That unmocked interactive fixture shows Connection retry because no picker backend runs on its fixture origin; successful picker data is separately supplied by the automated tests. The screenshot is fixture evidence, not the earlier composed runtime. Browser viewport and composed Studio Dark mode were restored after inspection. CSS/layout lint passes with the same 48 existing JavaScript warnings and no errors. These two CSS rules and the additional viewport cases are the only production/test changes after the qualified round-1 source.
