# Actual direct and embedded partial-fork recovery

Four actual Workbench pairs at **c34c1f4e1f279c66598a5ab83a8c2cc05e9b4a72** execute the same immutable format1 BPMN process directly and through native Root, Sequence, Flowchart and CompensationScope parents. Both paths start two human waits, complete the left branch, and lose the first owned host through SIGKILL. A fresh host preserves that accepted progress and the original right wait. Completing the right branch creates exactly one after-join wait; completing it finishes both models and returns once to the native parent. A third cold host preserves all terminal observations. Repeated left/right commands replay their original outcomes without advancing the model, including after completion.

The [capture manifest](manifest.json) binds **52 raw assets**, including **24 whole Runtime/Design snapshots** from twelve owned Workbench processes. All twelve qualifying hosts and four separately recorded diagnostic hosts were [absent at the final process check](children-stopped.json). Original invocation identities, complete publication pins and material incarnations survive each restart. Physical rows verify each accepted human invocation/attempt, the native parent kind, exact start/result payloads, one parent-settlement receipt, and source acknowledgement after settlement. The expanded whole hosting suite is a separate regression receipt; its result is not inferred from these four cases.

Released-interpreter states compare at both-waiting, partial, recovered, joined, final and retained boundaries. The only normalized field is `tokens[*].producingWorkHandle`: an owning model ID becomes `$model`; an **observed model-local human invocation** becomes its exact element/binding/token/iteration tuple. Completed work is retained in the observation ledger and bound to its physical input receipt. Foreign handles remain unchanged and different local work stays different. Token IDs/order, flows, diagnostics, sequence, outcomes and all other semantic fields compare exactly.

Two earlier diagnostic attempts are retained separately. At [33f3ffe94](diagnostics/33f3ffe94/run.json), all four comparisons failed after the first branch; only logs/TRX were retained. At [53c64a05a](diagnostics/53c64a05a/run.json), the same four failures also retained eight whole stores. Their [closed-store comparison](diagnostics/53c64a05a/diagnostic-comparison.json) isolated the difference to the owning model and completed local work handles. The observer had assumed the earlier single-human normalization covered a partial fork. The corrected fixture uses the actual work ledger; **no runtime implementation changed**. Neither diagnostic attempt qualifies recovery.

[Portable verification](verification.txt) and [13 controls](controls-fixed.txt) pass: one positive and twelve rehashed corrupt-copy controls. They reject semantic/token-order changes, foreign or wrong local work handles, an invented ledger token, two matching false traces inconsistent with physical state, a deleted physical receipt, premature source acknowledgement, duplicate process identity, a relabelled native parent, a different interpreter version and an omitted parent. The initial [control failure](controls.txt) came from recompressing a negative SQLite copy before closing its connection; the corrected control closes the copy and asserts its deleted row count. The positive capture was never edited.

```sh
ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=<fresh-output-directory> \
ELSA_BPMN_HOST_SOURCE_REVISION=<exact-source-commit> \
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false --filter FullyQualifiedName~RunBpmnParallelWorkbenchTests

python3 tools/bpmn/verify_parallel_embedding.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/embedding/parallel-native-recovery/manifest.json
python3 -m unittest discover -s tools/bpmn -p test_verify_parallel_embedding.py -v
```

[Qualification](qualification.json) records executing source, raw commands, counts, verifier hashes and limits. All104 frozen baseline assets and the authoring fingerprint remain unchanged. This receipt closes the paired partial-fork trace gap in SC001/T019/T029 on owned SQLite hosts with the built-in single-node actor. Service, signal, legacy upgrade and Studio evidence retain their separate sources. It does not establish non-SQLite host recovery, distributed placement, direct-model Studio acceptance or completion of the broader program.
