# Round9 correction qualification

The owner [authorized bounded verification continuation](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6546678880). The authority assertion now uses the existing canonical value comparison (c41a49714). All previous failures remain in [the earlier receipt](../in-progress/README.md); the three-run hold is resolved without weakening the test or any merge gate.

The production correction daa912bfc checks the native worker's earlier physical outbox responsibility before consuming its Cancelled result, including an authentic result already queued by the old writer. It uses one additive, default-refusing read on the existing lookup family. The subsequent [projection correction](../projection-controls/README.md), source43741654e, checks bounded physical-key pages before excluding status/kind, refusing corrupt owned indexes instead of declaring false absence. No schema, state format, queued result, source identity or cancellation authority changes.

## Actual original queued-result recovery

The original input is the unchanged [round9 queued-result whole store](../../correctness/prior-cancellation/deliver-worker-result.sqlite.gz), compressed SHA256 `87a7a76fdc45f7cc7c2c0a4f1ac94361c8108a33c1cc1633cafcc49f7487773c`. It was captured before the correction through ordinary publication/actor/queue/checkpoint/outbox ports. No execution, receipt, claim or clock was seeded. The result's old empty `cancellationSources` payload remains unchanged.

The stable-source regression passes atc41a49714 and in the subsequent whole507 BPMN runs. It first proves that the authentic queued input cannot advance the outer model while the earlier source is owed, then lets the existing positive claim expire naturally and verifies convergence of the same input, authority, full pins/material incarnation and Accepted receipt.

The standard runtime-only Workbench first passes atd21f5131c. Its two distinct-PID cold hosts retain a byte-identical whole Runtime database, SHA256 `4fbec462750d923b0f495eee16e42b48eaf83f9b8e2ea25c43234d52389301d5`.

After projection hardening, **all six affected actual old-fault/queued-result host cases pass at36831a014**, zero skips. The original queued-result case preserves semantic state, exact old result bytes, original native worker header, authority, scope, executable pins and material incarnations. The original prior cancellation is Delivered before the outer terminal decision, which precedes acknowledgement of the original worker result. Different host PIDs98390/99236 retain identical full observations and a byte-identical whole Runtime store, SHA256 `55aee80c772fa5ca26f228e97658212eef2f356cc8ef9360675c5a7851a27569`. No Design store is created; the new host admin receives404 for the original caller's execution.

These are actual standard-host recovery and cold retention over SQLite for a derived published SDK fixture. They are not a new ETL, direct-model Studio, other-provider or broader program acceptance run. The original whole input and all six final cases' complete stores/observations are portable below `final-host-evidence/`.

## Whole projects and compatibility

| Verification | Exact executing/repository source | Result |
|---|---|---|
| Whole BPMN | 43741654e | 507/507, zero skipped |
| Whole Runtime persistence | 43741654e | 1811/1811, zero skipped; all21 lookup controls included |
| Whole Runtime | d21f5131c, service/test source unchanged afterward | 3083/3083, zero skipped |
| Whole standard hosting | d21f5131c | 45/45, zero skipped |
| Affected old-fault/queued-result standard hosts | 36831a014 | 6/6, zero skipped |
| Architecture | repository36831a014, unchanged compiled guard source82f6202a5 | 594/594, zero skipped |
| Maps, tiers, tier docs/workflows | repository36831a014 | all pass |
| Public binary API comparisons | 36831a014 | all15 pass; resolved references, parameter-name rules, no suppressions |
| Workflows frontend project | d21f5131c, Studio source unchanged afterward | 2812/2812 |
| Affected Studio browser cases | d21f5131c, Studio source unchanged afterward | 59/59, zero skipped |
| Studio typecheck/lint | d21f5131c, source unchanged afterward | pass; lint retains55 warnings, zero errors |

The whole45-host run remains sourced21, before projection hardening; it is not relabelled as a whole368 run. The final six targeted host cases and whole changed backend projects qualify the hardened path. Discovery and executed methods reconcile; Runtime's3072 discovered entries expand to3083 executed cases through three dynamic theories (+11). Raw results, exact arguments, products, source relations and decoded hashes are retained. The normal protocol requires fresh CI on the final reviewed head separately.

All104 frozen baseline assets rehash unchanged. The strict saved-Studio verifier and13 controls revalidate the actual earlier b7 two-family/four-new-run/twelve-store proof, preserving84/92 materials, original authoring/XML/layout/full pins and original run observations. It remains sourceb7 live UI evidence. The saved Runtime stores contain zero model executions/inputs, so this native-model result guard does not participate in those native-only journeys. Current Studio source differs fromb7 only in the list CSS qualified by the current browser/frontend checks; it is now identical to adopted main. No new current-source live Studio or first-class direct-model Studio acceptance is claimed.

Main45b534710's Studio qualification changes and main e26c11bd9's Agents documentation are adopted. The latter introduces no production, host-test, BPMN-tool or Studio delta. Publish-state remains51337684e. T041/T043 are evidenced; T044 prepares Implemented to take effect at this implementation merge. Nine full reviews are complete, none clean; sixteen total are authorized, with full round10 next. T045–T047, exact-head required checks, normal protected merge and resulting-main verification remain open. #2907/#2908/#2892, broader acceptance and existing deferrals remain open.

[Manifest](manifest.json) is the portable asset ledger. Private machine paths in raw commands are historical provenance; reproduction builds current source and uses the committed tests, original fixture and fresh owned directories.
