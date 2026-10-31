# Authorized Studio diagnostic and closeout qualification

The owner authorized one bounded authentication diagnostic/repair pass, followed by the same two saved Studio fixtures ([receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6544070098)). Actual product/controller source `82f6202a5` passes both fixtures and the unchanged strict verifier. All 13 unchanged controls pass, with zero failures/errors/skips.

| Observed result | Evidence |
|---|---|
| Original and layout authoring open/export unchanged; only the gateway label changes; versions 3.0.0/4.0.0 publish | [Actual Studio bundle](studio/manifest.json) |
| Four new completed runs retain their distinct Published Run/Test Run kinds | [Verifier result](gates/studio-verification.json) and screenshots/DOM/API views in the bundle |
| Both real backend restarts preserve all four full API views, every original run, material incarnations and complete pins | Twelve closed coupled stores; [canonical view comparison](gates/canonical-full-view-comparison.json) |
| 84/92 original native materials and the frozen authoring fingerprint are preserved | The unchanged strict verifier and all [13 controls](gates/studio-controls.json) |
| Supported form login/session/token flow works before and after restart | Diagnostic reports retain response/status/cookie attributes without cookie or token values |
| Architecture guard passes after lossless archive repair | 594 passed, zero failed/skipped at archive-repair source `8e9b31360` |
| Whole updated hosting project | 43 passed, zero failed/skipped at source `8e9b31360` ([command/TRX](gates/hosting-whole-command.json)) |

The browser pass uses the existing configured owned return origin, waits for the supported provider navigation, and qualifies one owned pair at a time. No additional Identity product code changed. The earlier sign-in loop did not recur in this controlled pass; a single cause of all earlier failures is not established. Those failed attempts retain their original outcomes in the [preceding checkpoint](../c4cfa0736/README.md).

The previous CI failure at source `82f6202a5` came from archiving a standalone baseline `packages.lock.json` under its active filename. The repository guard correctly rejected that out-of-solution lock. Source `8e9b31360` stores the project/configuration/lock as compressed evidence, with identical decoded hashes; the guard is unchanged. The first local attempt also read the old Git index before the removal was staged and remains a red result. The subsequent whole architecture suite passes 594/594. Private restore inputs must be decompressed outside the source checkout when reproducing the API audit.

All earlier backend/frontend/API results retain their executing sources. Product and controller trees are unchanged between the actual Studio source and the archive-only correction ([source comparison](gates/execution-source-equivalence.json)). The source-bound qualification index maps the preceding 15 passing binary comparisons, frontend 4058, browser 321 cases including one isolated timeout repeat, the actual original pending-cancellation/impossible-timer checkpoints and the remaining scopes.

This proves existing saved Studio compatibility. First-class direct-model Studio, wider ETL operational acceptance and overall program completion remain open. Seven full PR reviews have completed, none clean; 16 total are authorized. Round 8, green exact-head required checks, protected merge and resulting-main verification remain delivery gates. Review/check/merge receipts belong on PR #3410; the source does not pre-claim their completion.
