# HTTP disclosure qualification

Local verification date: 2026-10-20. Implementation commit: `2149ad842254d0acd24e6fc314b7334fdf3c02c4`. Source-built UI/runtime qualification: `5e024ad06256df7eb2a88ade0c07b9bd71993be4`, which also includes the documentation-only `main` update `33c33214f16ca8c157d2af99ab31faecb90cb9a9`. Delivery receipts and exact-head hosted checks belong on the implementation PR and #3064.

## Completed local gates

| Surface | Command / result |
|---|---|
| Workflows | `pnpm --filter @elsa-workflows/studio-workflows test`: 2,058 passed across 161 files |
| Web | `pnpm --filter @elsa-workflows/studio-web test`: 567 passed across 49 files |
| Focused disclosure/options | 123 passed, including mounted-editor selection, current semantic errors, dictionary instances, collection removal, cleanup and lossless state |
| SDK copies | 12 passed, including Connections declarations |
| Workspace types | `pnpm typecheck`: passed |
| Lint/layout | `pnpm lint`: zero errors; 48 existing warnings; CSS/layout checks passed |
| Frontend build | `pnpm --recursive --workspace-concurrency=1 build`: passed, including Web bundle budgets |
| Browser fixture | HTTP progressive-disclosure spec: 10 passed; keyboard and full-page axe at 320/768/1024/1440 px in Light/Dark, plus laptop method/configuration/error recovery |
| Design backend | Release whole Design suite: 679 passed, zero skipped |
| Design API | Release whole Design API suite: 37 passed, zero skipped |
| Studio/Workbench | Both source-built Release hosts succeeded; Studio zero warnings/errors; Workbench 101 existing warnings, zero errors |
| HTTP runtime | `pwsh -NoProfile -File e2e-tests/http/Test-SendHttpRequestStatusOutcomes.ps1 -BaseUrl http://localhost:5695`: 2/2 passed, matching and unmatched published status routes |
| Architecture | Evaluated project graphs restored in locked mode; whole Release suite 578 passed, zero skipped; TRX reconciles all 569 discovery entries to 578 executed cases |
| Generated maps | Initial check identified only spec inventory/status changes; refresh reviewed, then `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` passed |

Commands are rooted as described in [quickstart.md](../quickstart.md). Root reviewed code/test repetition and extracted shared signal transitions and browser setup rather than duplicating selectors or fixtures.

## Real composed Studio

Studio and Workbench were rebuilt from the same clean source, then launched on owned ports 5689/5695 with new disposable content roots. No database existed in either root before first start. Existing unrelated hosts were preserved. Process IDs, DLL/config hashes, startup logs, source SHA and database preflight remain in the local execution receipt. The activity palette reported the exact `1.0.0+5e024ad...` catalog version.

In disposable draft `14V76UOp7VU`, node `sendhttprequest-cd6d085f`:

- A newly added HTTP activity exposed URL, Method, Content and Connection directly. More options initially collapsed; the synthesized `text/plain` default did not count as authored configuration.
- At 1365 × 768, the Connection control occupied y=578–614, within the first viewport. Enter and Space opened/closed the initially unconfigured section.
- More options exposed Content Type, Request Headers, Expected Status Codes and Timeout. Authored `application/json`, status `200` and `00:00:30` counted as three configured fields and held the section open.
- The body `UX3064 body preserved across methods` and those advanced values survived GET → HEAD → POST → GET, Save, full page reload and node reselection. The section reopened with its three-set indicator.
- A keyboard-resized 312 px inspector had `clientWidth === scrollWidth === 311`; controls remained accessible by keyboard and vertical scrolling. Root visually reviewed laptop Light and narrow Dark screenshots. Temporary viewport, inspector width and colour mode were restored afterward.

The real runtime currently chooses a plain text fallback for its nullable Timeout descriptor (and the observed status item), so an attempted malformed timeout did **not** produce a local duration error. It was replaced with the valid value above. Do not cite that attempt as a reproduced private-editor error. Built-in TimeSpan/Numeric invalid/recovered behavior is proven separately through the real-panel component/browser tests; current semantic errors, stale callbacks and unobservable-editor safe visibility also have focused regression coverage. This presentation change does not add a new validator or alter server-side parsing.

Actual composed-app screenshots are retained locally as `3064-after-laptop-dark.jpg`, `3064-after-laptop-light-configured.jpg`, `3064-after-narrow-light.jpg`, `3064-after-narrow-dark.jpg` and `3064-after-narrow-dark-options.jpg`. They are distinct from the browser fixture artifacts. Captures are native 1365 × 768 JPEGs; no 2× scale claim is made. Baseline screenshots came from the prior owned runtime and are not candidate attestation.

## Regression evidence and limitations

Retained expected-red evidence covers the original hierarchy/Connection tier, private validation reporting, effective mounted-editor/context selection, current semantic error visibility, and two simultaneously mounted dictionary editor callbacks clearing one another. The final focused suite and whole suites above pass. The StrictMode replay and dictionary blank-key open/close hypotheses passed before their proposed fixes, so neither is reported as a reproduced defect.

An additional noncanonical Web `tsc --noEmit -p tsconfig.json` probe reports 88 existing diagnostics in untouched tests/vendor entrypoints. The same command against exported base `825aa98dd624295655576f7ed299f6146b991bc8`, with the same installed dependencies, produced the exact same 88 diagnostic lines. Web has no package typecheck script; the canonical workspace typecheck above passes. This extra probe is an existing baseline limitation, not passing evidence.

Architecture discovery has two deferred theories: `Canonical_domain_api_depends_on_capability_aggregation_and_registers_one_stable_declaration` expands from one discovery entry to five executions, and `Every_collectible_api_explorer_artifact_is_rejected` expands from one to six. The TRX contains every discovered method, no unexpected methods, 578 passed outcomes and no skipped result. This explains the nine-case difference rather than treating the smaller list as a passing execution count.

The generated-map diff adds Spec 252 and increments the inventory from 247 to 248; no new architectural findings were introduced. Final independent-review/hosted-CI/merge receipts belong on the PR. Post-merge CI remains a separate gate from local qualification and PR CI.
