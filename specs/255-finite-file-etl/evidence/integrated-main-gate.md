# Integrated main validation

Runtime source checkpoint: `fdfe9ddc1846ebd7e78f513d8b94efbf14d45d94`, the normal merge of main `7fb745f8604b6ed67a2eae60266976c98bce949e` after repair commit `f68e35a69`. [Pushed checkpoint receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6304390181).

Final local source checkpoint: `8a2ad77d9b83b645db59bdd2e1586c42189fbc76`. The repair changes only compensation test fixtures, Studio packaging/verification inputs and documentation; ETL runtime code is unchanged from the integrated source above. T034/T035 are complete; final-head CI, merge and resulting-main verification remain open under T036. Earlier [round-1 repair evidence](round-1-review.md) retains its original source hashes and results.

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

## Review, repairs and delivery gates

Root and bounded read-only reviews checked the repair diff, normalized publishing identity, Studio package/API tracking, compensation integration and the complete FR-001–FR-015/SC-001–SC-005 acceptance mapping. The source review found no production callback defect, but the subsequent whole compensation suite exposed omitted child scheduling metadata in the newly landed M4 direct-callback test helpers. The review therefore did not establish a clean integrated gate. The acceptance review found no missing T001–T033 implementation requirement. Reviewers ran no tests; the executed results above are root verification.

Root reconciled the introductory partition wording with FR-007 and corrected stale README progress statements and the distinction between configured compute scopes and persisted result-delivery discovery. These documentation corrections do not change the runtime source qualified above.

Round 2 integration findings and repair evidence:

- `CompensationScopeDecisionTests` constructed completion/fault callbacks using legacy overloads that supply empty child scheduling metadata. The original whole-project run finished with 20 failed and 276 passed (296 total, zero skipped); all failures were immediate missing-request assertions in that fixture class. The helpers now pass the actual scheduled metadata and deliberately unrelated delivery metadata, matching the production callback path without weakening routing. The original full-suite log is retained as `etl-main-compensation.log`. After the helper correction the whole project passes **296/296, zero failed/skipped**, matching all 296 entries from `--list-tests`; retained logs are `etl-repaired-compensation.log` and `etl-repaired-compensation-discovery.log`, with `repaired-trx/compensation.trx` confirming all 296 executed.
- Studio CI job `118952589935` passed its build, tests, pack and package-inventory steps, then failed the standalone image build because the Dockerfile omitted `Extensions/Etl/studio/`. The missing module copy is added. The asset verifier also now requires the ETL module bundle and checks every enumerated asset in its self-test. A temporary fixture with only the ETL bundle absent is accepted by the prior verifier, refused by the corrected verifier, and accepted after restoration; actual 24 generated assets and catalog links pass. No workflow or deployment setting changes. The original log is `etl-fdfe-studio-dotnet-ci.log`.

Round 2 required these changes. Root's round 3 reviewed the integrated scope and repaired diff, acceptance mapping, retained negative controls and all completed validation. No actionable in-scope finding remains. Bounded reviews informed the root decision; they did not substitute for root verification or become a required independent approval.

The repaired source passed the following serial checks:

| Check | Result |
|---|---|
| Compensation | 296 passed; 296 discovered |
| Activity Runtime | 630 passed; 628 discovery entries, two theory placeholders expand to two rows each |
| Workflow Runtime | 2,504 passed; 2,493 discovery entries, three theory placeholders expand to 2, 4 and 8 rows |
| Runtime API | 199 passed; 199 discovered |
| Architecture | 583 passed; 574 discovery entries, two theory placeholders expand to 6 and 5 rows |
| ETL discovery | 376 entries, matching the integrated full run above |
| Dependency graph | Full server locked restores passed in Release and isolated Debug |
| Maps and filters | Regeneration reviewed; committed dependency data and solution filters unchanged and fresh |
| Catalog links | Zero broken links across 140 files |
| Studio image | Complete local Docker build/export passed; ETL assembly and browser bundle present |

Every executed suite above has zero failures/skips. TRX results and discovery listings reconcile the runtime counts without omitting discovered cases. Architecture's two expanded `MemberData` sources were inspected to reconcile its count. Generated findings have no missing catalogs; ETL design-to-runtime reference signals match the reviewed publication bridge. Ignored documentation maps/findings were not staged.

All 17 stages in `etl-repaired-validation-stages.json` passed; commands and logs are retained in `/tmp/elsa-program-20261020-reprioritization/`. The local image is `sha256:aea6752d9be293ad59edbeab1e648bed527d7fb9a30ec601999cac4756e70e4c`. A network-disabled, automatically removed inspection container confirmed `/app/Elsa.Studio.Etl.dll` and `/app/wwwroot/_content/Elsa.Studio.Etl/studio/modules/etl/module.js`. This local build uses existing generated frontend assets; final CI rebuilds those assets from source. No application deployment or registry publication occurred.

Remaining: green required checks on the final PR head, normal merge and resulting-main verification. The finishing changes after the qualified source are documentation/spec status only; local code suites are not rerun for those edits. The historical browser receipt remains attributed to its own source checkpoint. Main's inspected Studio changes are confined to the diagnostics route and its selectors, with no ETL page or picker changes.

SQLite remains the qualified ETL provider. No generic worker pause, non-SQLite ETL provider, browser-driven cancellation, exactly-once physical effects or proven historical timeout cause is claimed. This finite-file slice does not complete parent #2908 or Program #2892.
