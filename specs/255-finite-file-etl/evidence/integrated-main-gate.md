# Integrated main validation

Runtime source checkpoint: `fdfe9ddc1846ebd7e78f513d8b94efbf14d45d94`, the normal merge of main `7fb745f8604b6ed67a2eae60266976c98bce949e` after repair commit `f68e35a69`. [Pushed checkpoint receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6304390181).

This is an in-progress validation receipt. T034–T036 remain open until the remaining checks, final review and delivery gates are satisfied. Earlier [round-1 repair evidence](round-1-review.md) retains its original source hashes and results.

## Executed on the integrated source

| Check | Result |
|---|---|
| Full Release ETL project | 376 passed, zero failed/skipped |
| Workbench Debug build | Succeeded, zero errors and 92 warnings |
| Real Workbench HTTP: Sequence | Published ETL invocation completed, then the following native activity completed |
| Real Workbench HTTP: Flowchart | Same pinned ETL publication completed, then the connected successor completed |
| Both HTTP outputs | Each run: two partitions, two accepted records, one rejected record, one filtered record; two accepted-output chunks and one error chunk with checked JSONL contents |
| HTTP fixture teardown | Owned process exited; private content root removed |

Commands:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore -c Release -p:WarningsNotAsErrors=NU1603
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore -p:WarningsNotAsErrors=NU1603 -m:2
pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1
```

Retained logs are `etl-main-integrated-full.log`, `etl-main-workbench-build.log` and `etl-main-http.log` in `/tmp/elsa-program-20261020-reprioritization/`. The HTTP assertions also checked pinned source/lookup digests and lengths, terminal counts and settled cursors. This is HTTP output/continuation evidence, not an HTTP manifest API claim or a new Studio browser run.

## Review and remaining gates

Root and bounded read-only reviews checked the repair diff, normalized publishing identity, Studio package/API tracking, compensation integration and the complete FR-001–FR-015/SC-001–SC-005 acceptance mapping. The source review found no production callback defect, but the subsequent whole compensation suite exposed omitted child scheduling metadata in the newly landed M4 direct-callback test helpers. The review therefore did not establish a clean integrated gate. The acceptance review found no missing T001–T033 implementation requirement. Reviewers ran no tests; the executed results above are root verification.

Root reconciled the introductory partition wording with FR-007 and corrected stale README progress statements and the distinction between configured compute scopes and persisted result-delivery discovery. These documentation corrections do not change the runtime source qualified above.

Round 2 integration findings and repair evidence:

- `CompensationScopeDecisionTests` constructed completion/fault callbacks using legacy overloads that supply empty child scheduling metadata. The original whole-project run finished with 20 failed and 276 passed (296 total, zero skipped); all failures were immediate missing-request assertions in that fixture class. The helpers now pass the actual scheduled metadata and deliberately unrelated delivery metadata, matching the production callback path without weakening routing. The original full-suite log is retained as `etl-main-compensation.log`. After the helper correction the whole project passes **296/296, zero failed/skipped**, matching all 296 entries from `--list-tests`; retained logs are `etl-repaired-compensation.log` and `etl-repaired-compensation-discovery.log`, with `repaired-trx/compensation.trx` confirming all 296 executed.
- Studio CI job `118952589935` passed its build, tests, pack and package-inventory steps, then failed the standalone image build because the Dockerfile omitted `Extensions/Etl/studio/`. The missing module copy is added. The asset verifier also now requires the ETL module bundle and checks every enumerated asset in its self-test. A temporary fixture with only the ETL bundle absent is accepted by the prior verifier, refused by the corrected verifier, and accepted after restoration; actual 24 generated assets and catalog links pass. No workflow or deployment setting changes. The original log is `etl-fdfe-studio-dotnet-ci.log`.

These findings keep the current review round changes-required until validation completes. Neither repair changes ETL runtime source qualified above.

Remaining: affected activity/native/runtime API suites; dependency-graph locked restores; architecture, map/filter and discovery-count checks; final review disposition; green required checks on the final PR head; normal merge and resulting-main verification. The historical browser receipt remains attributed to its own source checkpoint. Main's inspected Studio changes are confined to the diagnostics route and its selectors, with no ETL page or picker changes.

SQLite remains the qualified ETL provider. No generic worker pause, non-SQLite ETL provider, browser-driven cancellation, exactly-once physical effects or proven historical timeout cause is claimed. This finite-file slice does not complete parent #2908 or Program #2892.
