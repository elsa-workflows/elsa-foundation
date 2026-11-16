# Actual pending Event-sibling cancellation recovery

Executing source: `f4c923ed9bb01f764e648ee45d1c76b081f51098`. The production files are unchanged from the sibling-cancellation fix at `c40881fd0209dd1f2e8765358c3afd3f007247ab`; this increment adds a test-only delivery pause and actual host qualification.

Five real journeys publish a model with two explicitly pinned native Event workers, directly and under RunBpmn Root, Sequence, Flowchart and CompensationScope. Both workers arm real waits. The normal native CancelWorkflow API cancels one worker. Its exact result commits model revision2 as TerminalPendingSettlement, retaining the original sibling cancellation obligation. The test build pauses that committed cancellation source immediately before the real dispatcher. The captured sibling is still Running, the cancellation claim is still fresh, and native parents retain their original RunBpmn wait. The owned process is then SIGKILLed.

A standard host recovers the same coupled stores after natural lease expiry. A higher fence reclaims the original source, cancels the original sibling and consumes its exact native result. Direct BPMN settles at revision3. Each native parent consumes the typed cancellation and settles revision4; its successor never runs. Both original Event workers are Cancelled. The original worker/model/material pins, interpreter history, consumed addresses and source identities survive. Another SIGKILL and third standard host retain identical model, worker, parent and delivery observations. No execution state, source claim or clock is seeded or edited.

[The portable manifest](manifest.json) retains 107 qualifying assets: 30 complete Runtime/Design stores and observations from 15 owned SIGKILLed hosts. All five successful runs have zero observer refusals. Three source-matched product builds have zero errors; their warnings and raw logs remain in `build/`. Thirteen portable checks pass, including 12 rehashed corrupt-copy controls over physical pending state, actual worker pins/outcomes, fresh and reclaimed source claims, ACK ordering, native parent consumption/successor suppression and stable cold recovery. The original 104 frozen baseline assets and authoring manifest are unchanged.

The first Direct attempt at `0dc96a38fa2504b54a3f2299ce2b9be4e7d52f75` recovered cancellation but its final retained SQLite backup was refused. Its 19 assets, including six complete diagnostic stores, remain under `diagnostics/observer-refusal`; that incomplete run does not qualify the journey. The collector now records refused reads, resumes a real transaction before retrying a complete coupled capture, and refuses qualification if capture remains unavailable. The successful runs needed no retries.

This completes the pending Event-sibling interruption witness. The earlier [post-consumption/pre-ACK witness](../sibling-cancellation/README.md) and [delivery-time broadcast witnesses](../model-broadcast/README.md) keep their own sources and limits. Broader retention/provider/activation/rollback checks, final affected-source upgrade and Studio, complete-PR review and required CI remain open. #2898, #2907 and program #2892 remain partially verified.

```sh
python3 e2e-tests/bpmn/verify_signal_interruption.py specs/1017-direct-bpmn-hosting/evidence/events/pending-sibling-interruption/manifest.json
python3 -m unittest discover -s e2e-tests/bpmn -p test_verify_signal_interruption.py
```

The producer is `e2e-tests/bpmn/qualify_signal_interruption.py`. Build separate standard and `-p:ElsaBpmnRecoveryProbe=true` Workbench products from one clean committed source, plus `tools/bpmn/DeliveryRecoveryRead`; pass their assemblies, the required `--parent` and a fresh `--output`. Only its own children are paused or killed. The committed stores and receipts are portable; temporary machine paths are not continuation inputs.
