# Round 6 repair qualification after main dec598d99

Executing source is **4dc934c444705ddf40707b8eeb1991e1949fefa9**, which adopts #3498's Identity/login and Linen Studio changes. [Source and counts](source-and-counts.json) and [command results](gates/gate-results.json) bind the completed local runs. This is repair qualification, not another complete-PR review. Six rounds remain completed, none clean; the existing owner review-route decision is pending before any seventh round.

| Scope at source4dc | Result |
|---|---|
| Whole frontend | 4012 cases /307 files pass; three no-test-file scripts separately recorded |
| Changed appearance/account browser surfaces | 121 pass, zero skips/retries |
| Whole Identity / Studio / architecture | 532 /645 /594 pass, zero skips |
| Actual old accepted timer input and direct restart journey | 1 pass, zero skips; three owned SIGKILL exits137 and three coherent closed Runtime stores |
| Supported typecheck/lint/production build, maps/tier/docs/workflow checks | All pass |
| Public binary API comparisons | 12 pass, resolved references, no suppressions |
| Actual existing Studio compatibility | Both families, four completed new runs,12 whole stores;13 unchanged verifier controls pass |

The [actual host case](actual-host/case.json) uses the unchanged source9c accepted-input fixture. Its original pin/incarnation/accepted receipt remain intact, with a visible durable fault and no stranded queue/work/outbox. New impossible publication refuses422. A normal direct model starts, waits, survives process loss, resumes through its supported human command, completes and survives another restart. No SQL authority/state edit.

The [strict Studio bundle](studio/manifest.json) retains exact opening JSON/XML/layout exports, label-only edits, reviewed3.0.0/4.0.0 publications, truthful Published Run versus Test Run identities, and exact original saved executions/material/migration history. [Chronology](chronology/) separately records new-run API views before any restart command, followed by equality after the actual restart. Twelve screenshots were visually inspected. All104 frozen baseline assets remain unchanged. These are existing Studio/native authoring journeys; first-class direct-model Studio acceptance remains #2907's own work.

[Host products](gates/host-products-after-studio.json) match the bytes frozen before all Studio attempts. Every owned attempt process stopped. Two earlier setup attempts are [not counted as qualification](diagnostics/studio-setup-attempts.json): direct publication omitted review-dialog confirmation, then a replay omitted its pre-restart new-run API reads. Both were repeated from fresh original inputs. A separate launcher diagnostic is retained; the final standalone13-control invocation and corrected dotnet metadata read pass.

Earlier [BPMN499 and whole host42 qualification](../qualification-2191/README.md) retains its daac/2191 sources. [Execution source comparison](gates/execution-source-equivalence.json) proves no runtime/BPMN code or test changes during main adoption; [prior scope comparison](gates/unchanged-prior-qualification.json) preserves the source04c Runtime/Design/Dispatch/ETL suites within their unchanged scopes. The new publication state leaves all12 audited baselines unchanged. API shape does not settle the owner's version-magnitude decision.

The command files retain actual invocation receipts, including temporary output/cache paths. Portable reproduction uses committed source4dc, the hosting test's committed original input, both frozen coupled Studio stores, the standard `run_owned_studio_upgrade.py` controller, normal browser actions, and the unchanged Studio verifier/controls. Published API baseline URLs/hashes/closure inputs remain in the [round5 audit](../../../round-5/qualification/api-binary/); temporary machine paths are not authoritative inputs.

T041/T043 qualification is restored. T044/spec completion preparation remains pending the owner review route. Existing owner magnitude/needs:decision, deferred GitGuardian39189012, exact required CI/protected merge/resulting-main and broader program gates remain open. No seventh review or merge started. #2898/#2907/#2908/#2892 remain open/Partial; existing deferrals unchanged.
