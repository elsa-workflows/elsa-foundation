# Actual native RunBpmn → native RunEtl → HTTP recovery

Executing source **b6e7e1f0de7367f81d35aebc5016d4b91e2d7927** qualifies actual service-model embedding under Root, Sequence, Flowchart and CompensationScope. Each parent completes and cancels the real ETL journey after HTTP429, process loss and restart. The [matrix](manifest.json) binds eight embedded journeys plus one actual direct success counterpart, **190 raw assets and 54 complete coupled stores**. All 27 owned Workbench processes and nine HTTP fixtures have stopped.

The native `RunBpmn` activity now admits the existing native-service model profile through its existing immutable model pin, dependency closure and durable bridge. The interpreter, native worker and ETL runtime retain their existing owners. No new scheduler or adapter was added. Runtime model format 5 retains both native service work and its parent invocation; format 6 retains native cancellation and its parent. Formats 1–4 keep their existing shapes. Both the parent delivery service and parent-settlement command refuse progression while actual worker cancellation is still owed.

Every journey publishes its real ETL definition, closed native RunEtl worker, BPMN service model and native parent through the owned authenticated Workbench APIs. The first host reaches an actual durable HTTP429 retry while the other shard has produced two HTTP effects. Its process is killed. Fresh standard hosts open the same stores; no state, parent, bookmark, result or receipt is fabricated.

For success, the actual typed RunEtl `Done` result completes the native worker. The exact model `native-result` receipt at revision 2 leaves the terminal model pending its native parent. Actual typed RunBpmn consumption completes the parent and its composite successor. The exact `native-parent-settled` receipt advances revision 3; parent-result acknowledgment follows that receipt. Four actual HTTP records remain, with no owed output or incidents. Another standard host retains identical views and receipts.

For cancellation, the public native CancelWorkflow alteration cancels the outer parent after restart. The actual `cancel-embedded` input at revision 2 leaves worker cancellation owed. The native worker's terminal checkpoint retains the original ETL cancellation and model result together. Actual ETL cancellation settles before exact native-result consumption at revision 3. The model still owes its parent until the exact parent-settlement receipt advances revision 4. The original parent-result source is acknowledged afterward. Both earlier HTTP effects remain; another standard host stays unchanged past the original retry deadline plus two seconds, with zero late requests. ETL terminal delivery retains its actual ProvenParentTerminal disposition.

The direct counterpart uses the same executing source and twelve identical captured first-party product hashes. Its real waiting and completed interpreter states compare exactly with all four embedded success runs, normalizing only a token's `producingWorkHandle` when it equals its own model execution ID. Diagnostics, order, token/element/flow identities and outcomes are preserved. The process definition and binding contract also compare exactly. Each isolated publication retains its own independently checked executable pins; their artifact identities are not equated. External BPMN library hashes were not collected in these process receipts.

The read-only verifier checks complete physical stores against actual API and checked-reader observations, model/parent/worker pin chains, native registrations, typed consumption, output records, cancellation order and settlement-before-ACK. Twelve new rehashed negative controls refuse missing physical parent/settlement, foreign registration, early ACK, missing dependency/typed consumption, old human-format substitution, late downstream cancellation, duplicate matrix cases, differing interpreter diagnostics, differing product hashes and a changed process definition. All portable controls pass **84/84**. [Qualification](qualification.json) records the bounded claims and check receipts.

The source-matched standard host builds with zero errors; BPMN tests pass **391/391**, including strict format and cancellation-settlement controls. All **eight actual human-model embedding host regressions** also pass with zero skips. Maps and test-tier registration pass. All 104 frozen baseline assets and their manifest remain unchanged. Two earlier Sequence captures exposed an observer polling error: a just-created model may still return HTTP404 during asynchronous admission. The controller now retries only that actual expected response; rejected stores were preserved before the correction. This was a controller fix.

## Reproduction

Build a clean committed source through the normal build-slot wrapper, then use fresh owned output directories for each command:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --embedded-parent Sequence \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> --output <fresh-output>
```

Repeat for Root, Flowchart and CompensationScope; add `--native-parent-cancel` for each cancellation arm. Omit `--embedded-parent` for the actual direct success counterpart. These are separate fresh publications and stores. The capture controller requires clean executing source; output must be outside the checkout. Verify the committed bundle with:

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/embedded-native-recovery/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

This closes the service success/whole-native-parent cancellation and same-source success-trace arms only. It does not prove a second crash while parent-result delivery or source acknowledgment remains owed, other cancellation origins, business/technical-fault mappings, held retry, worker/timer or admission races, other providers/rollback, or final affected-source upgrade/Studio. Existing human recovery receipts keep their original executing sources. T025/T027/T029/T031/T034–T041, #2898, #2907 and the program remain open/partial; full-PR review rounds remain 0 of 6.
