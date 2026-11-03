# Actual coded business-error recovery through BPMN and native parents

Ten production Workbench journeys at **d55964a76139349eeff722501df473401cce204d** qualify matching and nonmatching coded error boundaries, directly and under native RunBpmn Root, Sequence, Flowchart and CompensationScope parents. Every case uses a genuinely published native RunEtl worker and ETL definition. One HTTP record violates its declared Int64 schema; ETL retains three accepted records, one rejected record and one safe JSONL error. The native worker completes with its typed BusinessError, which the BPMN binding reports as `etl.rows-rejected` to released Bpmn 0.5.0 semantics.

A real HTTP429 holds one shard after the sibling produces two effects. The controller quiesces only its owned host for read-only provider backups, then kills that process with SIGKILL. A fresh standard host resumes the original wait, honors the original twenty-second retry deadline and consumes the exact worker result. A third standard host preserves the same API, checked-store and physical HTTP observations. The supported production resumption sweep is configured to 0.2 seconds in these captures; no probe, state injection, clock edit or replacement parent is used.

| Authored catch code | BPMN result | Actual native parent disposition |
|---|---|---|
| `etl.rows-rejected` | Completed / Done, with the interpreter's exact ErrorCaught diagnostic and error-end route | RunBpmn and every native parent complete; the native successor runs once |
| `etl.other-error` | Faulted / bpmn.work.faulted, retaining errorCode etl.rows-rejected | Root and Sequence workflows fault with a blocking RunBpmn incident; Flowchart also faults its composite and retains both incidents; default CompensationScope absorbs the child fault and completes with Compensated and a resolved AbsorbFault incident. No forward successor runs |

Sequence's composite activity is observed Running while its workflow is Faulted. This receipt does not decide the intended status in [#3242](https://github.com/elsa-workflows/elsa-foundation/issues/3242). The existing native compensation contract explicitly defaults `faultAfterCompensation=false`; its Compensated outcome is preserved rather than relabeled as BPMN success. The model itself remains Faulted in that case.

## Runtime change and retained compatibility

The native-work profile now admits an interrupting coded error boundary attached to its single closed service worker. The library owns matching and token progression. Catch-all, timer, noninterrupting and additional event/work shapes still refuse under this profile. The library requires subtree-cancellation capability for error boundaries; this restricted adapter can retire only the worker whose authoritative terminal result has already been accepted. Its application guard refuses any cancellation command without the exact retained terminal origin, handle and element proof.

No binding DTO, model material format, state format, native BpmnProcess authoring descriptor or persisted writer was changed for this increment. Old material/profile validation and formats remain in place. All 104 frozen baseline assets and their manifest remain unchanged. This is not older-binary or final upgraded-host acceptance.

## Evidence and verification

The [manifest](manifest.json) binds ten unique cases and **204 qualifying raw assets, including 60 whole stores**. Each case retains full native/model dependency pins, the original invocation/attempt and wait registration, actual typed RunEtl truth, model consumption and native parent settlement receipts, and their physical outbox sources. ACK follows the exact consumption/settlement receipt. Both waiting and terminal interpreter states compare exactly between direct execution and all four parents after replacing only the model's self-referential producingWorkHandle. All twelve captured first-party product versions and hashes match across the ten cases. No external-library binary hash is claimed by that inventory.

Thirty Workbench processes and ten HTTP fixtures were used; all 40 listed owned children stopped. All 41 listed children from rejected/superseded captures also stopped. Three [diagnostic counterexamples](capture-summary.json) retain 46 additional raw assets and 18 whole stores at their original sources; none counts as qualifying recovery. They exposed controller assumptions: outer Completed for an unmatched error, one incident for Flowchart, and Faulted instead of default Compensated. The corrected controller preserves the actual native contracts; no runtime fault or compensation policy was changed to make those observations pass.

The standard source-matched build has zero errors. At production-code source `663464b61`, the complete BPMN suite passes400/400 (400 listed, zero skips), including nine new shape controls, and all eight actual human-work RunBpmn host regressions pass. The initial capability control failed both positive cases before the capability/terminal-proof repair. The only changes from663464b61 to the executing source `d55964a76` are the two Python journey/verifier files; those .NET results retain their source. Final portable controls pass119/119: the prior106 plus this positive matrix and 12 rehashed negative controls. Maps and all 123 test-tier registrations pass. [Qualification](qualification.json) records commands, source scopes and log digests.

Build a clean committed source through the normal build-slot wrapper, then run each parent with both boundary modes against fresh owned output directories:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py --parent BpmnModel \
  --business-error --business-error-boundary matching --embedded-parent Sequence \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> --output <fresh-owned-output>
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/business-error-native-recovery/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_*.py'
```

Omit embedded-parent for direct execution; otherwise select Root, Sequence, Flowchart or CompensationScope. Use `different` for the nonmatching-code case. Never restore captured temporary ports or rewrite retained pins.

## Remaining acceptance

This advances the business-error arms of T039/T040 only. Actual technical final fault and bounded held retry, timer/model-broadcast/race arms, other providers, retention/rollback, final affected-source upgrade and Studio, and full-PR review/required checks remain open. #2898/#2907/program acceptance remains partial. No semantic terminal revival, ETL exactly-once guarantee or completed compensation-handler effect is claimed; the qualified scope has no completed forward step to undo. HTTP effects remain at least once.
