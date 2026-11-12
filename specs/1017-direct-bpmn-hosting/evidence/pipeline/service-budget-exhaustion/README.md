# Actual service budget exhaustion

Executing source: `f781ae7750d1d21d55793e11a4a781fecf0c90a0`. The collector and verifier change in this increment; production policy/driver/API remain at `6f532c6edfe4931ce0d574e0c98c5d93cb012d81`.

Five standard owned-host journeys run directly and through native RunBpmn Root/Sequence/Flowchart/CompensationScope. The immutable service policy allows two total physical attempts. A real HTTP429 wait and first SIGKILL precede a retained-lookup snapshot read fault. Attempt 1 remains Faulted with `etl.snapshot-read-failed`; model revision 2/WaitingIncident retains its exact active semantic token and pre-failure snapshot. A second SIGKILL preserves this held state. Repairing the unchanged snapshot and starting a third host does not revive the failed worker.

Before the authorized retry, the owned original bound lookup source is temporarily renamed without changing bytes or its binding. The new worker/run genuinely faults during preparation with `etl.binding-read-failed`, before runnable partitions or new HTTP effects exist. The old prepared snapshot remains available. Attempt 1 becomes Retried and attempt 2 Final; both failed native/ETL invocations and their exact result consumption/ACK remain retained. The model feeds the final uncoded technical fault to the interpreter and settles at revision 4, or revision 5 after native parent settlement. The coded business-error boundary does not catch it.

Retrying the actual final incident returns 409 and retains Rejected/execution-terminal without changing model revision or admitting a third worker. Restoring the unchanged bound input after the failed host stops and starting a fourth standard process preserves identical API/checked views and wire effects. The original two successful HTTP effects remain; no new effect appears.

Native Root/Sequence/Flowchart workflows retain Faulted. Sequence's composite activity remains Running under its existing fault disposition; Flowchart faults both activities. Default CompensationScope retains Completed/Compensated with Resolved/AbsorbFault. Every original RunBpmn registration consumes the typed result, no forward successor runs, and parent-return ACK follows its exact settlement receipt. These observed dispositions preserve compatibility; they do not decide #3242.

The [manifest](manifest.json) binds 176 raw assets and 45 consistent whole stores. It retains 20 Workbench processes/five HTTP fixtures and ten actual SIGKILL exits; all 25 listed children stopped. All 12 captured first-party product versions/hashes match. Direct and embedded waiting/held/final interpreter histories compare after only self work-handle normalization. The verifier checks both physical fault roots, immutable material/budget/dependency closure, model state/receipts, exact start/result/parent sources, original ETL snapshot and actual replacement preparation failure, native incident inventories and parent root shapes. No SQL state, clock or ETL classifier is edited.

```sh
python3 e2e-tests/etl/verify_service_budget_exhaustion.py --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/service-budget-exhaustion/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

To reproduce, build the hosting test project on a clean committed source with `-p:ElsaBpmnRecoveryProbe=false`. Run `qualify_http_recovery.py --parent BpmnModel --technical-fault --business-error-boundary matching --hold-service-retry --held-action exhaust`, supplying the built Workbench/DeliveryRecoveryRead assemblies and a fresh owned output directory. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for the native cases. Historical paths and ports are provenance; do not restore their loopback bindings or edit captured stores.

This proves exhaustion of the pinned two-attempt budget. Timer interruption, competing retry/cancel commit orders, obsolete-result and authorization controls, other providers, retention/rollback and final affected-source upgrade/Studio acceptance remain open. T036/T037, #2898/#2907 and program #2892 remain incomplete.

[Qualification](qualification.json) binds full validation logs: source-matched standard host build 0 errors, portable 176/176 with 15 new positive/corrupt-copy controls, maps and 123 test-tier registrations, zero skips. All 104 frozen baseline assets and manifest remain unchanged. Earlier BPMN 411/human-host 9 retain source `d2e62a1ab`; they are not relabeled as this controller-only increment.
