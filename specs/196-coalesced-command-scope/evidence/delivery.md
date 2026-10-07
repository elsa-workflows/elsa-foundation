# Local delivery gates and remaining acceptance

This packet records the original correction evidence under Spec 194, retained when the specification was renumbered to 196 during the 7 October integration refresh. The refresh merges main `bfdde6ccdc4666479146177c1c845b7647111739`; runtime registration and test sources are unchanged from PR head `c55f6469324bb24418bda2ff40100d09573aca90`. Earlier results below remain historical evidence. Root and independent review accepted the resolved integration diff after correcting accounting-report links and stale status text. Generated Maps freshness passed (`dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check`, exit 0). Exact-head CI remains pending for the refreshed commit; resulting-main and final T17/T18 gates remain open.

Root owns integration; bounded test author used Luna Extra High and independent QA used Sol 5.6 High. Root reviewed all delegated changes and executed every build/test/host run through the queued toolchain. The only runtime source change is the default factory lifetime. Custom registrations, cadence, persistence formats and checkpoint semantics are preserved.

| Gate | Actual result | Scope/limit |
|---|---|---|
| Real-EF lifetime red/green/mutation/restored | 2 failed / 2 passed / 2 failed / 2 passed, intended lifetime oracles | No DB query; default service scope contract |
| Custom Singleton/Scoped and idempotency before correction | 2 passed | Explicit registration compatibility |
| Runtime Release suite | 2,013 passed, zero failed/skipped | Includes existing checkpoint/replay/queue/outbox and new compatibility assertions |
| Architecture initial | 611 passed, 24 failed | Required restore outputs absent in the new worktree; retained failed evidence |
| Required locked Release/isolated Debug restore | Wrapper exit 0 | Environment prerequisites only; no runtime repair |
| Architecture after restore | 635 passed, zero failed/skipped | Full local architecture gate |
| Maps initial | Exit 1; four stale outputs | Original Spec 194/provider test inventory required generated snapshot updates |
| Reviewed Maps refresh and freshness | Four files changed; check exit 0 | Manifest/spec/test inventory/v1 count; no dependency/package/schema change |
| Rebuilt PostgreSQL primary HttpEndpoint + valid REST | Two exact Completed/zero-incident/Alice Smith controls passed, wrapper exit 0 | Sequential correctness only; retained settings/source-reference/export/build evidence |
| Independent review | Design, tests/source/mutation, harness and actual evidence accepted | Local root + independent Sol QA; external PR review is pending |
| Exact-head PR CI/review | Pending | No unavailable check represented as passed |
| Resulting-main CI/Maps | Pending | Existing main 932 CI remains failed; this correction does not repair the separate SQLite finding |
| Publication boundary | Merge hold remains | Owner decision pending; no package publication or deployment authorized |

The accepted representative controls do not discharge final concurrent T17/T18 correctness or before/after measurements, and do not attribute the earlier individual HTTP500/202 responses. This correction remains open until canonical reconciliation, exact-head review/gates, authorized merge and required acceptance. T02 remains the lead program objective.

Retained local gate digests:

- t19-runtime/runtime.trx: `9b96602e4692a16ff6641b44e69597fba34532002eac30113b9691c698ad3e11`.
- t19-runtime.log: `2f9191f24d3f31580eb3f14acd16b59a9985c1d9752be47dfa5ac23ca1ab2e53`.
- t19-architecture/architecture.trx: `40c73f4cfeac94ae2d9977c47b641cd2cb562498ccbcb4505991fc3d0e7e66e8`.
- t19-architecture.log: `64e152e3aef7e68bf5895435df82e09ee982eea208202dcc8b4b8b8a048b01af`.
- t19-architecture-restore.log: `456b4a7c612a268e1f26ca51388e868af7b7979a35e990f0967dd32eb46249f0`.
- t19-architecture-restored/architecture-restored.trx: `e33382dd34f05c790caa5c0cd1d18c5e200629a393e5247b1753363480dda410`.
- t19-architecture-restored.log: `be33dd4e7657885fbbc35bf5f0d922b15ab3f6f291ba8ce4b3c97e437e9a20c9`.
- t19-maps.log: `6943f4de6a2aadb3afa27682b7d45ad9e8693369bcd473e2f2324b3fee1f7448`.
- t19-maps-refresh.log: `7d064c50c1916c048e7fe2d72a2dd4d2dacd6033d45ace418552da2c01f8fe2d`.
- t19-maps-refreshed-check.log: `692c1972a3e507f2117dbbc7f5259b56307bb4181e583044a4a50a05cf777ecb`.
- t19-workbench-build-record.json: `c36601f380ac6c9ef86480f63dda95c9a70b8db38a7585f9d8b8d9235f13bbf1`.
