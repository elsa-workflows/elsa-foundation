# S001 prototype receipts

Program [#3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610), spike [#3749](https://github.com/elsa-workflows/elsa-foundation/issues/3749), supporting draft [PR #3767](https://github.com/elsa-workflows/elsa-foundation/pull/3767).

The actual Studio/Workbench products and retained native harness were built from `b8cdec4c2c0961df9a2aa9077f4d99dddd9ad60e`, including descendant main `a1419b9c3`. The metadata observer verified matching full source versions and actual DLL hashes. The controller used fresh owned stores/free loopback ports, retained the normal composed features, verified product hashes before restarts and stopped all its own children. [Host products](host-products.json) retains the product/asset identities and cleanup receipt. No private stores, credentials or cookies are committed.

The temporary extraction experiment lived in commit `c48331a1632cd2d7eed031cb0916a2b1df27643d` and was exercised again at the qualified source above. It was removed after the experiment. This PR retains the supporting runtime/history fixes and proof harness; it does not ship a partly implemented extraction command.

The controller checked every browser asset individually. The committed receipt retains the complete asset inventory digest/count and per-module digests, plus relevant individual Workflows editor assets; this avoids copying thousands of unchanged icon/vendor paths into the spec. The full per-file manifest remains in the owned run's original receipt, whose digest is retained.

| Proof | Result and evidence |
|---|---|
| Crossing results | Twelve paired cases over native Sequence, linear Flowchart and BPMN process bodies; both directions return 42. |
| Parent variable reads/writes and occurrences | Two paired loop cases; root counter is 3 and exact consumer inputs are 1, 2, 3. |
| Persistence/restart | Four genuine baseline/grouped waits, Balanced and Durable; exact held occurrences survive process restart, then resume exactly once with retained values and no duplicate producer. |
| Cancellation | Two paired cases; exact held root/block/wait occurrences become Cancelled, no successor runs and late stimulus starts/resumes zero executions. Historical inspection bookmark counts are not a live index query. |
| Supported recovery | Two paired native DispatchWorkflow execution faults under an owned temporary depth restriction. Restore the original configuration, restart, reschedule the exact faulted occurrence: old leaf Superseded, successor/ancestors/caller Completed, downstream effect once, original incident retained Open. |
| Nearest control targets | Four additional paired public-API cases preserve nearest-loop Break and nearest-reusable-graph Complete; the outside caller continues and the unreachable WriteLine never runs. |
| Real Studio authoring | Extract two steps, await autosave, Undo once, await Undo's autosave, Redo, open/edit a child, return, rename, save/reload, normal Review & publish and Test Run. Four actual occurrences complete without incidents; the named block appears in immutable run inspection. |

[Native results](native-results.json) contains 22 retained-harness cases plus the four additional control cases. [Authored inputs](authored-inputs.json) retains synthetic inputs and reusable contracts; the native harness owns the full repeatable construction. [Studio journey](studio-journey.json), [saved document](studio-document.json) and [run](studio-run.json) bind the browser observations to actual resource/node/execution identities. The [native log](native-transparency.txt) and [control log](control-cases.txt) distinguish these runs from unit preparation.

[Regression receipts](regressions.json) retain baseline and guard-mutation outcomes, source revisions and complete affected-suite counts. Sequence result visibility was red on the committed baseline (3 passed, 1 failed) and green with the bridge. Removing the direct-child, committed-child or committed-parent guard makes the corresponding regressions fail. Save normalization initially failed both history cases; removing the accepted-baseline update from the final history fix makes the queued-save regression fail (4 passed, 1 failed). Original source was restored after every mutation.

An earlier attempted recovery fixture threw during input materialization. That leaves a Scheduled activity and a Blocking scheduler-poison incident, so it did not establish ordinary child-fault recovery. The retained proof instead uses a native execution fault and supported rescheduling. No materialization or recovery policy was relaxed.

Scope remains bounded: this does not qualify production selection/refusal commands, equivalent Activity Definition surfaces, promotion, BPMN XML binding retention, arbitrary fork/join result visibility or the final program acceptance matrix. Those remain the owning production leaves. Final PR-head CI/review and resulting-main qualification are separate receipts; this folder does not claim them passed.
