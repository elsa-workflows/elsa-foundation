# Actual Studio default-flow journey

This is browser proof for T028/T030/T031 on the second ordered switch PR, not final adoption.
Studio source `90feccfedfa5d4c32a25bf2fc00da0ab6666d9b7`; backend source
`b2fc515ffc213e7ba96f4040769269f43b4c00d8`. The latter's actual published BPMN descriptor
projection is in [the source-host receipt](../source-host/published-contracts.json), which matches the
unchanged frozen fingerprint2/2. Both application informational versions were checked.

[Build and configuration identity](build.json) records the binaries and actual served Workflows
entry digest, which equals the source-built module. Frozen pnpm11.9.0 installation, full frontend
workspace build and a subsequent Release Studio.Web build succeeded. The entire Workflows Studio
suite passed2493 tests in183 files, zero failures/skips.

The browser plugin exercised normal authentication and UI on isolated owned source hosts, without
route mocks, state injection, API substitution for browser steps or changes to the committed shell
configuration. Port5089 belonged to another process and was left alone. The owned Studio used5092;
only that explicit origin was added to the disposable backend return-origin list. Browser URLs used
consistent localhost names so normal cookie authentication worked. CORS was already configured for
that Studio origin; no authentication/cookie policy was weakened. The earlier default-composition
HTTP receipts remain bound to their original configuration digest.

## Browser steps and outcomes

1. Sign in with the repository's development admin to the paired owned hosts.
2. Create **BPMN library adoption · Studio default flow** using the normal BPMN root option.
3. Choose **Import BPMN** and upload [the portable fixture](../../../../e2e-tests/bpmn/fixtures/studio-default-flow.bpmn).
4. Select the gateway. **Default flow to Default timer** is checked; the other edge retains its outcome condition.
5. Change the gateway name to **Choose default route** and the other condition to **Never**. Save.
   The default remains checked: [edited gateway screenshot](default-flow-edited.png).
6. **Review & publish** reports Ready to publish. Publish normally to the default slot;
   [publication confirmation](published.png) reports version1.0.0 active.
7. **View published workflow**, then **Run executable artifact-3e510474765a**. This is a
   PublishedRun, not a transient test. Open run14eFVUdk0rQ; the process and actual Delay complete,
   with zero incidents: [run screenshot](published-run-completed.png), [normal API receipt](published-run.json).
8. Return to the definition and reload it from the server. The edited name/condition and checked
   default marker survive. The [persisted authored node](persisted-authored-v1.json) retains
   `elsa.bpmn.structure`1.0.0, `childNodeId`, exact catalog child references and `isDefault`/`defaultFlowId`.
   These agree with Studio's existing `bpmnTypes.ts` mirror; no library bindingRef envelope replaces it.
9. Choose **Export BPMN** and retain the actual downloaded [XML](exported-default-flow.bpmn).
   Parse/assert its default reference, edited name/condition and original PT1S timer descriptor;
   [receipt](default-flow-receipt.json) records the XML digest and exact identities.

The frontend supports the preserved authored envelope in this tested journey. This does not claim
all historical documents, arbitrary Studio editing, CallActivity escalation/error, ordinary-task
compensation, retry/re-arm/partial delivery, combined BPMN durability, cleanup guards or final
review/checks. T029 and the remaining runtime/deletion/adoption gates stay open. #2898/#2907 remain
open; ADR0063 is still proposed.
