# Resulting-main verification

**State:** Delivered and verified. The [final PR receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6307901682) records successful descendant gates, issue/board readback and release of claims6288815023 and6304312212. #3109 and PR #3111 have Verification Passed; #2908 and Program #2892 remain open.

## Verified revision and gates

At verification, `main` was exactly `da9a4927d98fb29eda017b4e1a74a28ee4316439`. PR [#3111](https://github.com/elsa-workflows/elsa-foundation/pull/3111) is merged as `cfff0f018bda972f1d58c022adfc4e90d6027e4c`; GitHub compare confirms the resulting main is 16 commits ahead, 0 behind, with the merge as the merge base. The single commit after intermediate `b555bd8460270476e49496e519c69b65180e9505` is PR #3143.

| Gate | Result on `da9a4927d98fb29eda017b4e1a74a28ee4316439` |
|---|---|
| [CI 39536040313](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39536040313) | Success. Build & test, Core-only, Architecture guards, generated-map publication and Generated maps fresh succeeded. Fast backend tests: 17,829 passed, 32 skipped, 0 failed across 94 assemblies, including 376 ETL tests. Architecture: 584 passed, 0 skipped/failed. Core-only: 4,167 passed, 1 skipped, 0 failed. |
| [Studio 39536040185](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39536040185) | Success. .NET: 462 passed, 0 skipped/failed. Frontend lint, typecheck and unit-test job succeeded. Chromium: 333 passed, 5 skipped. |
| [CodeQL 39536040171](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39536040171) | Success for Actions, JavaScript/TypeScript, Python and C#. |

CI skipped EF-suite selection, EF container suites, Secrets EF composition and the red-main alert job. The successful generated-maps freshness job skipped two informational refresh-instruction steps. These skipped jobs and steps are not counted as passes.

## Historical attempts retained

Exact-merge CI and CodeQL were cancelled, not passed; exact-merge Studio and generated-map freshness passed. On descendant `b555bd8460270476e49496e519c69b65180e9505`, backend attempt 1 reached its 45-minute job limit; the cause is not established. Attempt 2 was cancelled by higher-priority main-push concurrency. Partial logs from these attempts do not qualify unfinished suites. The successful resulting-main runs above complete this verification gate without changing those historical classifications.

Issue [#2293](https://github.com/elsa-workflows/elsa-foundation/issues/2293) remains open and was not changed by this closeout.

## Scope boundary

This verifies the first finite CSV-to-JSONL/native/Studio slice only. Feature #2908 and program #2892 remain open with their broader acceptance criteria. The active #3151 / spec996 work still owes the full HTTP/native/Studio journey; continuous processing, SQL, RabbitMQ and BPMN obligations remain. This evidence does not claim parent or program completion.
