# Business decisions qualification

Run from the repository root with .NET 10, Node/pnpm and Playwright Chromium installed. Use the normal build-slot wrapper. All host witnesses own temporary processes, home directories and SQLite stores; they do not use a running developer host.

```sh
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
pnpm --dir Studio --filter @elsa-workflows/studio-decisions test
pnpm --dir Studio --filter @elsa-workflows/studio-decisions typecheck
python3 tools/ux-acceptance/run.py --journey 'business decisions authoring and explanations' --output /tmp/decisions-studio-new-run
python3 tools/decisions/qualify-execution.py --output /tmp/decisions-execution-new-run
```

Both host runners require committed clean source and a new output directory. The Studio runner rebuilds both hosts and the real frontend, executes all four desktop/narrow light/dark projects once, and retains PNGs, accessibility snapshots, traces and source identities. Machine success is separate from independent visual acceptance. CI runs the complete composed journey inventory and uploads `studio-ux-<tested SHA>-<attempt>`; its nested `decision-execution` directory contains the execution/permission witness. Exact run/artifact links and independent review are recorded on [PR #3689](https://github.com/elsa-workflows/elsa-foundation/pull/3689).

The execution runner compares direct, Sequence, Flowchart, public dynamic-input and ordinary BPMN bound-task results and actual rule traces. It publishes changed rules while another caller waits, stops the owned process, disables Decisions API/Design/EF/workflow-design features, and resumes with the original frozen artifact. It also provisions seven real restricted users across two tenants, exercises independent grants and output/source disclosure, revokes Evaluate through the persisted Identity role store, and proves refusal after restart. Private credentials are deleted with its disposable directory; only public receipts leave it.

Run each complete Decisions test project under `Extensions/Decisions/tests/` with `dotnet test <project> -c Release -p:RestoreLockedMode=true -m:1 /nr:false`, and compare `--list-tests` with executed counts. The suites cover all five selected policies, typed results, null/no-match, exact 34-digit values, bounds/cancellation, actual trace, unsupported dormant cells, CAS/publication/replay/retention, final service cardinality, authority, protected dynamic inputs and workflow disclosure. Causal red/green controls and counts are summarized in the [implementation receipt](evidence/implementation-checkpoint.md).

The [cold package witness](evidence/standalone-package/README.md) records the actual computed-version package consumer and dependency closure. The [pinned interchange fixture](evidence/interop/README.md) provides its own runnable external-editor/reference-engine reproduction, finite normative cases and explicit disagreements. Neither source-project references nor an XML rendering alone establish those outcomes.

After reference changes, run locked solution restore, the complete architecture project and generated-filter/tier checks:

```sh
dotnet restore Elsa.Server.slnx --locked-mode -m:1 /nr:false
bash tools/architecture/restore-ci-project-graph.sh -p:RestoreLockedMode=true -m:1 /nr:false -v quiet
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -m:1 /nr:false
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check
```

## Acceptance map

| Issue success criterion | Witness |
| --- | --- |
| Studio create/edit/validate/test/publish/reuse | Four-project real-host journey; workflow version picker component suite; normal publication/execution witness |
| DMN round-trip/result equivalence and explicit unsupported cases | Pinned dmn-js/dmn-moddle edited round trips; 56 finite evaluator expectations, 27 reference equivalents and 29 visible differences/outside-profile cases |
| Same direct/native/BPMN decision and explanations | Cold package consumer; Sequence/Flowchart/dynamic-input/BPMN host matrix and process-restart receipt |
| Data/explanation only, effects with caller | Core dependency envelope and evaluator suite; ordinary caller owns subsequent SetOutput/activity execution |
| Saved-case deliberate regression | Seven saved cases, exact expected results, threshold regression/repair and frozen-candidate comparison in the composed journey |
| Version/concurrency/input/policy/error/dependency/bounds | Complete engine/lifecycle/API/adapter suites, real SQLite races, publication refusal and runtime-only recovery |
| Tenant/permissions/sensitivity | Real 37-check Identity matrix; API/adapter/disclosure suites for unsupported identities, protected inputs and retained model dependency traversal |
| Packaged/normal-host/conformance qualification | Linked receipts above plus exact-head required CI and resulting-main verification on PR #3689 |

Qualification is bounded: SQLite behavior and all four generated migration SQL scripts were exercised; PostgreSQL/SQL Server/MySQL database behavior is not inferred. The default-host matrix has no resolvable captured decision-activity result payload, hosted-model BPMN HTTP execution or external-provider identity; those paths remain explicitly outside that host witness, with relevant fail-closed contracts covered by focused suites. The supported profile does not claim full DMN/FEEL or automatic vendor decisionRef bindings. Delivery remains gated on independent acceptance, exact-head required CI and resulting-main verification.
