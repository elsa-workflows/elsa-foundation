# Bounded closeout after current-main integration

The round 7 C1/S1 fixes remain in `9032ca8e4`. Integration `1cee5acd4` adopts production main `c4cfa0736`; `356c1d9d7` preserves the older public constructor and corrects the template-history assertion. Executing product source `0f5f6c6e` also includes main `a455c1299`'s three documentation updates. Controller source `39f9b48f2` registers its own temporary Studio login-return origin; product source is unchanged from `0f5f6c6e`.

The corrected runtime and API checks pass. **Fresh post-restart Studio verification remains open: supported browser sign-in repeatedly returns to the login page.** The owner has been asked for one bounded diagnostic pass under the org's repeated-check stop condition. Seven full PR review rounds have completed, none clean; 16 total are authorized. Round 8 has not started. Required current-head CI, clean review, protected merge and resulting-main verification remain open.

## Recorded checks

| Executing source | Result |
|---|---|
| `1cee5acd4` | Whole BPMN 504, Behavioral 220, migrations 508, Design 655, Design EF 147, Design API 233, Runtime 3083, Publishing API 906, Studio .NET 663 and architecture 594 pass. Shared EF: 710 pass and one existing opt-in pool-stress case NotExecuted. |
| `1cee5acd4` | Frontend 4058 assertions pass. Browser: 321 unique cases; 320 first-pass successes and one timeout that passes its isolated lower-load repeat with the normal timeout unchanged. No browser skips. |
| `1cee5acd4` | Whole hosting: 42 pass, one failed template-history assertion, zero skips. The failed run is retained. |
| `0f5f6c6e` | Whole Design EF 147 and Design API 233 pass. Three actual-host cases pass: cumulative template-writer upgrade; original pending nested cancellation; original accepted impossible timer plus direct start/wait/restart/resume/complete. Closed upgraded stores are retained. |
| `0f5f6c6e` | All 15 directional binary API comparisons pass with resolved references, parameter-name rules and no suppressions. Maps, tiers, documentation/workflow tier guards and read-only version computation pass. |
| `39f9b48f2` | Both product builds report success. The owned Studio return origins match their actual temporary URLs. Browser sign-in still fails to reach the editor in the corrected-origin attempt; fresh Studio recovery is unverified. |

[Summary](summary.json), [commands](commands/), [discovery/TRX reconciliation](initial/test-reconciliation.json), [browser reconciliation](initial/browser-reconciliation.json), [source boundaries](correction/source-equivalence.json) and the [hash ledger](manifest.json) retain the distinctions between sources and outcomes. Discovery counts differ where xUnit expands theory data during execution; all discovered method groups are represented. The pool-stress opt-in was not enabled for this closeout.

## Compatibility repairs

The incoming cumulative workflow-library migration becomes the actual additive library delta while retaining its upstream migration ID. September/template IDs remain, all four final model snapshots match upstream, and the internal history bridge follows the declared three-generation lineage. Logical aliases leave existing physical history rows unchanged; normal library upgrades append their own row. The focused lineage control is six failures/one pass before correction, then seven passes. The earlier diagnostic with an overly narrow operation-shape assumption remains separate.

The actual template-writer host case successfully recovered its template/wait and completed across restarts, then failed because its final assertion expected only the original history row. Its corrected assertion requires exactly that original row plus the library row; the current actual-host repeat passes and retains both whole stores.

Published Design EF 4.0.25 also removed the five-argument constructor present in 4.0.24. The published-to-published control reproduces that inherited binary break. The small delegating overload restores compatibility; all 15 corrected comparisons pass. [Latest public package/closure inputs](api-latest/) add three current baselines to the earlier 12 comparisons retained in rounds 4/5. Reproduction restores those exact public packages in a private directory and substitutes fresh local paths in the recorded commands. Compiled machine-local DLL paths are provenance, not portable inputs. Version computation uses publish-state `51337684e`; the existing scoped 4.0-preview exception remains settled.

## Studio limitation

The first original-fixture attempt opened the actual saved definition, exported unchanged authoring/XML, changed only the gateway label, published 3.0.0, and completed Published Run `14nx3CzbvR6` and Test Run `14nxH9tOSAh`. Both full API views remain identical after a real backend restart. The browser could not complete post-restart sign-in, so this is not a completed Studio proof. Its original screenshots, exports, API observations and coupled stores are retained under [the first attempt](studio-setup-attempt-1/).

The copied default login-return origins did not include the owned random Studio port. Controller `39f9b48f2` corrects that setting using the existing Identity option. The [corrected-origin configuration](correction/studio-owned-return-origin-config.json) is verified, but the [fresh sign-in attempt](studio-origin-corrected/) still returns to the login page. The cause is not established. Neither attempt replaces the earlier successful Studio evidence at its original source, and neither establishes current or first-class direct-model Studio acceptance.

All 104 original baseline assets and the frozen manifest fingerprint remain unchanged. Broader ETL/Studio production acceptance, Orleans, the task-runner follow-up and alteration/migration retain their existing status and deferrals. The program remains incomplete.
