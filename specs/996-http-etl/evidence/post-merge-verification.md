# HTTP ETL delivery verification

Implementation: [PR #3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156), reviewed head `adbc7417c42055e32437eddaeb2f80c980d4c666`, merged normally by the Elsa Workflows Codex App on 21 October 2026 at 18:09:14 UTC as `9462cb743c08a515e9b8973c57110e3f44df0f7f`. [Merge receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6323105067). This closes implementation and T021; T022 remains in progress until the resulting-main gate and bookkeeping below complete.

## Exact reviewed-head gate

Four bounded self-review rounds ended clean. Concrete findings were repaired and the affected checks repeated; no independent approval is inferred from skipped scanners. The owner's standing self-review/normal-merge authority and current repository rules were checked before merge. No force, admin option or branch-protection bypass was used.

[CI 39641906120](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39641906120) passed at the reviewed head. Completed logs show 95 backend suite summaries: **18,863 passed, zero failed, 32 skipped**. Included suites were ETL 1,009; workflow runtime 2,530; runtime EF 1,002; HTTP integration 94; Workbench 62; import integration 44; architecture 584. Separate core-only validation passed 4,199 with one skip; separate architecture passed 584 with no skips. These repeat some backend tests and are not additive unique coverage.

[Studio CI 39641905938](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39641905938) passed: .NET 462; browser 361 with five opt-in screenshot skips; ETL frontend 69; workflows frontend 2,285, plus three shuffled repetitions of that same workflow suite. Lint, typecheck, assets, package/image build checks and the workflows bundle budget passed. [Platform 39641905931](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39641905931) passed on Ubuntu and Windows. Maps/filter validation passed.

Backend skips were 30 Cluster conformance cases, one Cluster EF conformance case and one opt-in SQLite serial-open stress case. The core-only run skips the same stress case. The local macOS core run had 4,198 passes/two skips because a Linux-only case was also unavailable there. Scheduled-only EF container jobs were skipped; they are not provider qualification. A queued nonrequired CLA check was not treated as a pass and no CLA was signed. Skipped CodeRabbit/Greptile bot-author reviews were not independent approval. [Full exact-head receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6323084601).

## Resulting-main gate

All entries below refer to exact merge `9462cb743c08a515e9b8973c57110e3f44df0f7f`, not an earlier PR head or an unverified descendant.

| Gate | Current evidence |
|---|---|
| [CI 39650145673](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650145673) | **In progress.** Live output shows ETL 1,009 and runtime EF 1,002 passing, but final backend/core/architecture summaries are not yet available. T022 cannot complete on partial logs. |
| [Studio CI 39650145447](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650145447) | Passed. Completed logs: .NET 462, browser 361/five skipped, ETL frontend 69 and workflows frontend 2,285 plus three shuffled repetitions. Lint/typecheck/bundle and host/package/image checks passed. |
| [CodeQL 39650145255](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650145255) | Passed for Actions, C#, JavaScript/TypeScript and Python. |
| [Generated maps fresh](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650145673/job/119396032059) | Passed. The generator/filter/manifest contracts completed. |
| [Published maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650145673/job/119396031964) | Passed. The completed log identifies this exact source SHA and maps commit `3da767fe1bd99d79a3d9e777d18dbf46721452b5`. |

The five Studio browser skips are opt-in screenshot capture cases, not ETL acceptance. Scheduled-only EF jobs in the main CI run are skipped, not passed. Existing automatic [Packages](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650145685) and [Docker Images](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39650487584) workflows succeeded; no manual release or deployment was performed.

## Acceptance boundary and remaining work

The [integrated local checkpoint](final-integration-checkpoint.md) links the real native Sequence/Flowchart continuation, six connector/composite REST journeys, authenticated Studio acceptance, two-process HTTP recovery, direct source/error-route Retry-After restart, frozen-v1 native upgrade and failing-then-restored regression controls. This report reconciles delivery evidence; it does not substitute generic green CI for those behavioral proofs.

The current qualified ETL persistence profile is SQLite with same-machine process/HTTP witnesses. Design/runtime store qualification on all four first-party EF engines remains a [parent obligation](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6323246976), distinct from PostgreSQL/SQL Server source/destination connectors. Continuous processing, remaining file directions, SQL, RabbitMQ, BPMN invocation, agent collaboration, the independently packaged fourth model and production presentation acceptance remain in the program. Workflow/queue pause and a general task runner are a later follow-up.

Physical remote effects remain at least once. Historical receipt/HTTP timeout causes remain unproved; passing later checks does not establish causality. [Main-red issue #2293](https://github.com/elsa-workflows/elsa-foundation/issues/2293) remains open independently.

## Bookkeeping

GitHub auto-closed #3151 on the implementation merge, but its final gate criterion, `status:in-review` label, project Review/Verification Running state and retained claim intentionally still show T022 pending. Parent #2908 and Program #2892 remain open. A final receipt, reconciled status/acceptance and explicit claim release follow only after completed resulting-main evidence. #3181 remains queued and unclaimed.
