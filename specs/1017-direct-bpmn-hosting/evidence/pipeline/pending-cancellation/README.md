# Actual process loss with cancellation/result deliveries still owed

Executing source **b2251d591944fd89663748edac064b07cea89aa4** qualifies two pending-delivery cuts on a real direct BPMN → native RunEtl → HTTP ETL journey. Both use published material, authenticated public APIs, unedited owned stores and standard Workbench hosts. No new production semantic change or architecture was needed.

Each first host runs the real pipeline until one shard has produced its two HTTP effects and the other retains its original durable HTTP 429 retry. That host loses its process through SIGKILL. The second host opens the same stores with the normal recovery sweep configured to 300 seconds, keeping the subsequent cancellation/result deliveries owed while the public cancellation request runs.

| Pending cut | Actual state at the second SIGKILL | Recovery on the third host |
|---|---|---|
| Model cancellation before worker delivery | Public model cancel has committed revision 2 / TerminalPendingSettlement and its exact native-cancel source is pending. The original native worker remains Running with its original suspended RunEtl activity/bookmark. ETL is still running with the unchanged retry and two accepted records. No native result exists. | The retained model cancellation reaches the same native worker. Its terminal checkpoint retains the actual ETL-cancel source and native result together. ETL cancellation settles before exact native-result consumption at model revision 3; only then is the model-cancel source acknowledged. |
| Native terminal before result consumption | Public native CancelWorkflow alteration has committed the actual Cancelled native header/root and the same checkpoint's original ETL-cancel and BPMN-result sources. Both are still pending. The model remains Running at revision 1 with no native-result receipt. ETL is still running; the result's cancellation inventory retains that exact owed ETL source. | The retained ETL cancellation reaches its immutable invocation. The actual ETL result uses ProvenParentTerminal, preserving the already-Cancelled native truth. Exact native-result consumption then settles the model at revision 2 and permits result-source acknowledgment. |

The second host is also killed with SIGKILL, exit -9. The third host uses the normal 0.2-second sweep and settles all three engines from these unedited pending states. A fourth host preserves the exact final views, full pins, original native activity and ETL invocation, model input receipts and delivery sources through the original HTTP retry deadline plus two seconds. The two earlier HTTP effects remain; the waiting shard never retries and no owed output remains. Cancellation does not invent a typed native completion or silently rebind an ETL address.

The [matrix](manifest.json) binds **51 raw assets and 18 complete coupled stores**, including both second-crash cuts. Its two cases use eight Workbench processes and two HTTP fixtures; all ten owned children have stopped. Paths/ports/process IDs are observations; reproduction creates fresh owned bindings. [Qualification](qualification.json) records exact source, build and portable checks. The earlier [admission race](../native-admission-race/README.md), queued-start retirement and compatibility receipts retain their own executing sources.

## Reproduction and verification

On a clean committed source, build through the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --model-cancel --pending-cancellation-crash \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>
```

Repeat with `--native-cancel --pending-cancellation-crash` in a separate fresh directory. Portable verification:

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/pending-cancellation/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

All 61 portable tests pass, zero skips. The new positive matrix and nine rehashed negative controls reject a clean shutdown substituted for process loss, premature model/native-result acknowledgments, an already-consumed result, dropped ETL cancellation inventory, changed physical ETL terminal state, a missing terminal checkpoint, physical delivery disagreement and a rewritten retry deadline. Existing native/direct/worker/model cancellation and admission-race archives still pass. The current standard host build has zero errors; no .NET production behavior changed in this increment. Earlier BPMN 384/384 and EF model checkpoint 43/43 checks retain source 54d8532b0, rather than being relabelled as this source's test runs. All 104 frozen baseline assets and the authoring fingerprint remain unchanged.

## Remaining acceptance

These captures prove pending cancellation and native terminal-result recovery **before** result consumption. Process loss after model consumption but before source acknowledgment is a separate remaining cut. RunBpmn service embedding, held-fault retry, other provider/rollback qualification and final affected-source upgrade/Studio remain open. T034/T035/T038–T041, #2898, #2907 and program #2892 remain incomplete. PR #3410 stays draft; full-PR review rounds remain 0 of 6.
