# Validation guide: Switch MatchAny case activities

Run in the isolated S-12 checkout. The coordinator approved the prepared requirements and directed implementation in the same PR #5410. Evidence remains in that PR; a command listed here is not a claim that it passed.

## Prerequisites

- Current initiative/approved spec, .NET 10, pinned Studio toolchain, Playwright Chromium and existing persistence/host prerequisites.
- Isolated checkout and disposable stores. Use the normal build-slot wrapper. On Linux inspect `uptime`/`nproc`; inspect equivalent processor/load evidence on Windows before classifying timing failures.
- Retain Windows Application Control errors and use an established permitted Linux path if necessary. Never disable policy; record remaining not-run checks.
- The default EF test-project build copies the ignored spec 1017 `runtime.sqlite.gz` baseline. If that fixture is absent, retain the copy failure. A filtered Switch recovery run may omit only that unavailable copy item through an external local MSBuild target; it must not suppress tests or qualify the unrelated baseline scenarios. The PR records the exact affected command and platform limit.

## Affected .NET checks

Run sequentially, allowing for queued slots:

```powershell
dotnet test Essentials/tests/Activities/ControlFlow/Tests/Elsa.Activities.ControlFlow.Tests.csproj
dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj
dotnet test Essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Elsa3.Activities.Design.Import.Integration.Tests.csproj --filter 'FullyQualifiedName~SwitchMatchAnyRunTests|FullyQualifiedName~ExplicitOutcomesRunTests'
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~MatchAny_recovers_after_child_commit
```

Use equivalent commands on the permitted Linux host. Record source, executable/toolchain, counts and failures. Broaden only for changed runtime mechanisms or concrete unresolved risk.

## Required oracles and negative controls

| Scenario | Expected observation | Discriminating negative |
|---|---|---|
| A=true/B=false/C=true, populated | A/C start once; B/Default never; one `[A,C]` completion after both | First-match-only selection/scheduling |
| A immediate, C bookmarked | Successor has zero executions until C resumes | Complete on first callback |
| Both bookmarked, reverse resume | Independent resume; final selection still case order | Emit completion-order labels |
| Later condition throws after true case | No child starts; ordinary input incident | Schedule during partial evaluation |
| Body changes a condition variable | Initial pinned selection remains | Re-evaluate on child completion |
| No matches/zero cases, Default present/absent | Exactly optional Default body and `[Default]` | Empty selection or missing completion |
| Matching empty case plus populated match | Empty label selected, Default suppressed, body awaited | Ignore empty match for fallback |
| Foreign parent/duplicate/cancelled/faulted row | Cannot fill a missing selected success | Count callbacks or all workflow children |
| Persisted restart after child commit | No repeated committed body effect or condition; one completion | Activation-only progress |
| Fault then sibling success | Blocking child incident; no success; existing reschedule refusal | Parallel fault policy or failed-as-success |
| Break with live wait/queued sibling | Only Break; wait cleaned up; queued start suppressed; loop exits | Propagate without cancellation |
| Enclosing/workflow cancellation | Selected waits stop; no normal routing | Omit selected subtree membership |
| Matching bodies write same variable | Existing unordered-write diagnostics/policy | MutuallyExclusive facts |
| Elsa 3 nested import→publish→run | Both bodies retained/run; Done successor once after all | Drop body, condition, mode or slot identity |
| Unsupported mode/condition/body | Stable localized refusal with guidance | Suppress diagnostic to succeed |
| Elsa 3 MatchAny body surfaces native Break through activity, literal/computed script or propagated nested label | Localized UnsupportedMode refusal with bookmarked sibling; no changed-behavior publish/run | Check only Break-activity descendants or ignore aliases/replacements |
| Ordinary If/Sequence body with declared but unreachable Break | Converts/publishes/runs without cancelling another selected wait | Reject every declared Break port blindly |
| Ordinary script outcome or nested label other than Break | Body succeeds and owning Switch still awaits its sibling | Reject every named outcome or nested Switch indiscriminately |
| Imported owning Switch labels Break/Done, ordinary bodies and Sequence successor | Localized refusal/rename guidance; ordinary-label control runs successor once | Check only case bodies and miss owning outward selection |
| Native body-containing MatchAny case label Done | Draft/publication/runtime-structure rejection before invalid multiple selection | Publish and later fault on Done plus another label |
| Elsa 3 case-local loop consumes Break | That loop ends; another selected bookmarked body remains live and is awaited | Treat every nested Break as Switch Break or blanket-refuse safe loops |
| MatchFirst/outcome-only FlowSwitch | Existing behavior unchanged | Apply body coordination indiscriminately |

Temporary mutation checks are restored before final checks and never committed. Retain representative failing controls and the restored-source passing results.

## Studio and composed acceptance

```powershell
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
pnpm --dir Studio typecheck
pnpm --dir Studio exec playwright test tests/browser/switch-conditions.spec.ts tests/browser/switch-case-outcome-ports.spec.ts
python tools/ux-acceptance/run.py --help
```

Run the Workflows client Vitest suite through its existing package script. Use the owned composed runner's supported journey selection for the registered Switch journey across intended desktop/narrow and light/dark projects, with a new output directory. The runner must build this committed checkout and own hosts/stores. Do not attach to shared listeners or substitute fixture responses. If selection is unsupported, run the full intended inventory or add explicit reviewed selection without silently omitting cases.

Through Studio, author two true populated cases without Code view, choose MatchAny by keyboard, save/reopen, reorder/rename and undo/redo, publish/run, and inspect body effects/successor timing. Choose MatchFirst and repeat. Exercise no-match/Default, validation correction, read-only, narrow readability and focus. Preserve all declared screenshots, ARIA snapshots and traces.

An independent reviewer opens exact-head screenshots/traces and records passed/failed/incomplete plus not-verified states. Keep downloadable artifacts using repository conventions; local paths alone are insufficient shared evidence.

## Evidence and exit

Post commands/results, exact head, negative controls, artifacts and independent review rounds on the implementation PR. Keep failed/not-run states until repaired/rerun. CI waits are deferred; local evidence never means CI passed. A clean independent implementation round and applicable UX acceptance precede claiming local completion. Coordinator approval/merge gates remain separate.
