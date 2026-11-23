# S-9 retained BPMN fixtures

These are current-writer retained-state witnesses regenerated under spec1207 T026 before Elsa4's first stable release. They prove recovery and restart with the current hash recipe. They do not reassert an authentic old-writer upgrade claim.

The historical spec1017 inputs, archive pins and source-bound claims remain unchanged. The direct BPMN suite separately opens the original delayed-compensation database, verifies its original digest, and requires its obsolete model hash to be refused without advancing retained model, input, native, outbox or scheduler documents. Production has no old-hash verifier or compatibility branch.

| Scenario | Current fixture | Capture and required recovery |
|---|---|---|
| Cancelled compensation worker with a queued model result and obsolete delayed retry | `delayed-compensation.sqlite.gz`; provenance in `delayed-compensation.json` | Existing real-writer journey, captured at its `queued-result` checkpoint. Direct recovery keeps new admission disabled; standard runtime-only Workbench recovery and a second cold start retain the worker, retry, material incarnation and authority. |

The manifest pins the exact writer source and actual test/runtime/BPMN binaries, compressed and decompressed bytes, table counts, execution identity, capture test and original historical input. Generate with the existing test's `ELSA_BPMN_DELAYED_COMPENSATION_EVIDENCE` hook, then package with [record-current-bpmn-fixture.py](../../record-current-bpmn-fixture.py). The packager refuses to overwrite a retained result. After the first stable release, the [hash freeze rule](../../../../../docs/serialization.md#hash-inputs) applies.

Current acceptance is incomplete: the current fixture was captured by a passing real journey at `cc582ff479ba6171368e51b4655d6a98bd9594b7`; its new direct recovery/refusal and standard-host cold-start consumers still require execution. Other spec1017 and ETL fixture scenarios remain pending.
