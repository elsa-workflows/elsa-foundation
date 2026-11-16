# Actual cancellation of a held service fault

Executing source: `a4098bd1a23a1e975c82ebefb7334ba7c9a2bfd4`. This increment changes the journey collector and verifier; the production policy/driver/API remain at `6f532c6edfe4931ce0d574e0c98c5d93cb012d81`.

Five standard owned Workbench journeys run directly and through native RunBpmn Root/Sequence/Flowchart/CompensationScope. A real HTTP429 wait and first SIGKILL precede an authentic retained-lookup read fault. The original native RunEtl worker and ETL run become Faulted; HoldForRetry retains revision 2/WaitingIncident with its exact active semantic token, pre-failure snapshot and failed-result consumption. A second SIGKILL preserves that held state. The unchanged external lookup snapshot is restored before cancellation, without reviving the failed attempt.

The ordinary public cancel command retires model work at revision 3. The incident becomes Retired; its failed worker, ETL invocation, native fault incident, original accepted result receipt and semantic history remain unchanged. No new physical worker or ETL run is created. No cancellation is sent to the already-faulted worker, and no new external HTTP effect appears. The original two successful effects remain.

Every original native RunBpmn registration consumes cancellation. Parent workflows become Cancelled, their forward successors do not run, and the actual parent settlement reaches model revision 4. The parent emits its normal CancelEmbedded source; exact terminal model responsibility acknowledges it without an extra model input. The original parent-return source is acknowledged after the exact settlement receipt. Native activity dispositions are preserved as observed; this does not decide #3242.

Retrying the actual retired incident returns 409 and retains Rejected/execution-terminal at the same terminal model revision. A fourth standard process preserves identical API/checked views, retired incident and wire effects. All 25 listed children stopped: 20 Workbench processes and five HTTP fixtures, with ten actual SIGKILL exits. The supported resumption sweep is 0.2 seconds, with no recovery probe.

The [manifest](manifest.json) binds 166 raw assets and 45 consistent whole stores. The read-only verifier checks full model/worker/native-parent pins and dependency closure, original typed result consumption, source ACK, exact cancellation/rejected-retry receipts, protected native headers, immutable failed ETL invocation/lookup preparation, original fault incident, real native parent root shapes and cancellation settlement. Direct and embedded interpreter histories compare after only self work-handle normalization. All 12 captured first-party product versions/hashes match. Historical paths and ports are provenance; reproductions use fresh private resources.

```sh
python3 e2e-tests/etl/verify_held_service_cancellation.py --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/held-service-cancellation/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

Reproduce from a clean committed source by building the hosting test project with `-p:ElsaBpmnRecoveryProbe=false`, then running `qualify_http_recovery.py --parent BpmnModel --technical-fault --business-error-boundary matching --hold-service-retry --held-action cancel`, with the built Workbench/DeliveryRecoveryRead assemblies and a fresh owned output directory. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for the parent cases. Captured stores are read-only evidence; do not restore historical loopback bindings or edit their rows.

This proves cancellation while held and refusal after retirement. Budget exhaustion, timer interruption, competing retry/cancel commit orders, obsolete-result and authorization controls, other providers, retention/rollback and final affected-source upgrade/Studio acceptance remain open. T036/T037, #2898/#2907 and program #2892 remain incomplete.

[Qualification](qualification.json) binds the full source-matched standard-host build and portable validation logs: 161/161 controls, including 12 new positive/corrupt-copy controls; maps and all 123 test-tier registrations pass with zero skips. All 104 frozen baseline assets and their manifest remain unchanged. Earlier BPMN 411/human-host 9 results retain source `d2e62a1ab`; they are not relabeled as this controller-only increment.
