# S-9 retained BPMN fixtures

These current-writer witnesses were regenerated under spec1207 T026 before Elsa4's first stable release. Each row states its scope: retained execution recovery/restart or material/hash publication. They do not reassert an authentic old-writer upgrade claim.

The historical spec1017 inputs, archive pins and source-bound claims remain unchanged. The direct BPMN suite separately opens the original delayed-compensation database, verifies its original digest, and requires its obsolete model hash to be refused without advancing retained model, input, native, outbox or scheduler documents. Production has no old-hash verifier or compatibility branch.

| Scenario | Current fixture | Capture and required recovery |
|---|---|---|
| Cancelled compensation worker with a queued model result and obsolete delayed retry | `delayed-compensation.sqlite.gz`; provenance in `delayed-compensation.json` | Existing real-writer journey, captured at its `queued-result` checkpoint. Direct recovery keeps new admission disabled; standard runtime-only Workbench recovery and a second cold start retain the worker, retry, material incarnation and authority. |
| Format-2 material without a service fault policy | `current-model-v2.json`; independent preimage in `current-model-v2.golden.json` | Actual ordinary BPMN publication at `d031d98efef7bb705adc0f1245962dfd62ae3638`, referencing a newly published worker whose hash is verified. This is a material/hash witness, not execution or historical writer recovery. Paired current-pin, archived mismatch, precise refusal-before-write and related policy controls passed 14/14 at `638b92ee2ed8801b021d326e14d6a55eb7463117`; Python integrity/corruption controls passed 6/6. |

The format-2 capture retains all model preimage fields, its complete dependency pin and capture-time source/binaries.
[Its packager](../../record-current-bpmn-v2.py) independently calculates the opaque-identity canonical projection and
SHA-256, and pins the unchanged historical JSON's Git blob rather than platform-specific checkout line endings.
The initial container packaging attempt could not resolve Windows worktree Git metadata; rerunning against the
owned native Git checkout succeeded. That environment error did not produce a fixture.

The capture process binds its clean source, executing test binary's source version, actual binary hashes and snapshot bytes in `delayed-compensation.capture.json`. The manifest verifies that capture-time receipt and pins the packager separately, along with compressed/decompressed bytes, table counts, execution identity and original historical input. A stale capture cannot acquire a newer writer label during packaging. Generate with the existing test's `ELSA_BPMN_DELAYED_COMPENSATION_EVIDENCE` hook, then package with [record-current-bpmn-fixture.py](../../record-current-bpmn-fixture.py). The packager refuses to overwrite a retained result. After the first stable release, the [hash freeze rule](../../../../../docs/serialization.md#hash-inputs) applies.

The provenance-bound fixture was captured by a passing real journey at `41db8d5ff5b0a34237f06eba0be07c6af352658a`; the precise original-hash refusal passed there too. At `47fddb78063207132f98c2062a4256e4a284cf45`, the new bytes passed direct recovery/refusal (2/2) and real runtime-only Workbench recovery with a second cold start (1/1). Independent scoped review of that head found no remaining provenance or consumer findings. The earlier compile failure and weaker-provenance attempt remain in the PR receipts. Other spec1017 and ETL fixture scenarios remain pending; this does not complete their acceptance.
