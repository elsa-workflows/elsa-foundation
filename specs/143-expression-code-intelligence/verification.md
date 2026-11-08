# Expression Code Intelligence Foundation — Verification

## Restart note — 2026-10-23

The owner restarted Program #2310 on 2026-10-23. The planning branch was merged with current `main`, where the spec-status map is no longer committed (spec 257 publishes generated maps on the `generated-maps` branch). Source paths now live under `Essentials/` and `Studio/`. The 2026-10-06 snapshot below is historical and does not describe the current head. Review and merge follow the ADR 0080 D7 review protocol (Issue Bus Lite); there is no separate owner-acceptance step.

## Historical integration snapshot — 2026-10-06

The existing Foundation expression stack is being synchronized with main `2a0d1f10fb91bf6928203430d938d99d183c5cda`. The root planning branch changes no production, test, SDK or workflow file relative to that main. Its generated spec-status map retains the reopened expression work (25 completed tasks and 6 open tasks) and the two new draft specs. Root and independent diff review pass; authoritative combined-head Maps and CI remain pending. Historical baseline results below do not establish acceptance of this synchronized stack or a new Studio/Foundation pair.

The owner permits preview NuGet/npm publication only to feedz.io and separately permits Docker images at any endpoint; this delivery keeps the existing Docker Hub workflows. Native Chrome/Safari VoiceOver is **SKIPPED BY OWNER DECISION — NEVER PASSED**. At that time all program PRs remained draft/unmerged pending technical/review gates and explicit final owner acceptance; that acceptance step was dropped on 2026-10-23. Required resulting-main checks and public completion records remain open.

## Retained historical baseline

Last reconciled: 2026-10-23 for the Program #2310 restart; the baseline evidence below dates from 2026-10-02 planning and current-head extension evidence is pending

## Program #2310 status

The passing evidence below is historical baseline evidence. It does not prove the current normal host, effective runtime Liquid metadata, runtime-compatible JavaScript help, or the real persisted producer-consumer lifecycle requested by Program #2310. T026 host evidence is recorded in this PR against current main; T027-T031 and the corresponding live Studio journey remain open.


## Program #2310 M1 rebuild — T026 host evidence (PR 2) — 2026-11-08

Cookie-authenticated Workbench host evidence for expression tooling, rebuilt from #2373 without the bearer-hash / rotateBearer cases (those stay for a later PR). Recorded against main `c1f35d2dc73201df0e3890e1f0c851d9ddd52a37`. The test source is unchanged from the prepared patch; assertions were not weakened.

- New `Essentials/tests/Workbench/Tests/ExpressionToolingHostEvidenceTests.cs` starts a real Development Workbench via `WorkbenchProcess`/`WorkbenchShell`.
- Stock case: JavaScript + Liquid composed, EF design persistence (`elsa.db`), capability discovery of all six `expressions.tooling.v1` links (descriptors, context, symbols, completions, hover, validate), anonymous denial, then context/symbols/completions/hover/validate against a freshly reopened persisted draft for both languages.
- Missing-Liquid negative control: removes only `Liquid` from a copy of `shells.json` for that run; stock Workbench still composes Liquid on main (#3277). Descriptors advertise JavaScript and omit Liquid.
- Workflow create asserts HTTP 200 to match current main (not 201).
- No changes to `ExpressionToolingApiHandlers.cs`, bearer revision hashing, #3204 / ADR 0089, or Studio.

Local commands on this PR (SDK 10.0.300, locked restore):

| Command | Result |
|---|---|
| `dotnet test Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj --filter "FullyQualifiedName~ExpressionToolingHostEvidenceTests" --list-tests -p:RestoreLockedMode=true --nologo` | 2 tests listed |
| same filter, without `--list-tests` | 2 passed, 0 failed, 23.3s |
| Workbench `workbench-rest` shard (excludes `WorkbenchBackgroundActivityRecoveryTests`, `NativeHostingConformanceTests`, `NativeHostingGracefulHandoffTests`, `WorkbenchProcessTests`): `--list-tests --no-build` then `--no-restore` | 55 listed, 55 passed, 0 failed, 7m 57s |
| `bash tools/architecture/restore-ci-project-graph.sh` then `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --list-tests` | restore succeeded; `--list-tests` enumerated 602 |
| same architecture project without `--list-tests` | 617 passed, 0 failed, 1m 13s. The VSTest `--list-tests` count (602) did not match the executed count (617); nothing was skipped. |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` | `Generated solution filters still describe the project graph.` |

Not run here: the Workbench native-hosting split, whole-solution tests, Studio, bearer/rotateBearer coverage, RS0016 PublicAPI (no public API change), hosted CI. Bearer / rotateBearer coverage remains out of scope. T027-T031 remain open.

## Passing evidence

| Suite | Result |
|---|---:|
| `Elsa.Expressions.Tests` | 108 passed |
| `Elsa.Workflows.Design.Tests` | 344 passed |
| `Elsa.Workflows.Design.Api.Tests` | 89 passed |
| `Elsa.Workflows.Publishing.Api.Tests` | 459 passed |
| Expression-tooling and custom-host architecture filters | 4 passed |
| `dotnet build Elsa.Server.slnx --no-restore --verbosity minimal` | Passed with 0 errors |

The focused evidence covers exact per-expression-type provider routing, JavaScript runtime globals, Liquid symbols, dotted/nested value shapes, authoritative persisted context, policy filtering before paging, host-replaceable authorization and revision fingerprints, descriptor/capability composition, semantic validation state mapping (including validator faults and caller cancellation), publication fail-closed behavior, and Test Run acknowledgement/metadata.

## Full architecture suite

The complete `Elsa.Architecture.Tests` run passes all 320 tests.

The three custom-host composition failures initially exposed by this run were corrected by making the persisted authoring-context source safely optional when a host omits draft persistence. All affected composition tests now pass.

## Contract audit

- Context and symbols are server-authoritative, permission/host-policy filtered, metadata-only, bounded, and `no-store`; hosts can replace `IExpressionAuthoringAuthorizationPolicy`, and its opaque revision participates in stale-result protection.
- Catalog paging occurs after filtering.
- Value-shape members are inlined to depth four; lazy retrieval is explicitly unsupported by v1 descriptors.
- JavaScript, Liquid, and future providers own their own globals/functions/variables; there is no generic “Elsa globals” catalog.
- Known validation errors gate Test Run/publication; unavailable Test Run validation supports explicit acknowledgement only when the result contains no error diagnostics; publication remains fail-closed.
