# Actual bound Event-worker sibling cancellation

The original behavior is reproduced at `8d5ed3e03b228d866b26606a81e063839af183f6`: a normal native CancelWorkflow operation cancels one real Event worker, the direct model becomes TerminalSettled at revision2, and the other Event worker remains Running. The red store retains an empty model work list, no sibling cancellation obligation and the orphan's real wait. It is not called a passing journey.

The fixed journey executes at `c40881fd0209dd1f2e8765358c3afd3f007247ab`. It restores both complete actual waiting stores captured by the [empty-cohort journey](../model-broadcast/README.md) at791dc1701; no execution state or clock is seeded or edited. The new host preserves the original model/material incarnation and both worker pins. After another process loss, the same normal native cancellation operation cancels one worker. The model records revision2 as TerminalPendingSettlement and durably owes cancellation of the other original worker. The sibling's exact Cancelled result then settles revision3. Both native workers are Cancelled.

The second SIGKILL occurs with the settled model/result receipts present while the sibling-cancel source and both native-result sources remain FailedRetryable and unacknowledged. A third standard host acknowledges those same sources after their exact receipts, without changing the model. The actual stores prove this post-consumption/pre-ACK cut. They do not prove a crash while model settlement itself remains pending.

The fix reuses the existing native cancellation source, result address, settlement gate and opaque cancellation frame4. Whole-model cancellation and child-originated cancellation now use the same private staging helper. No public API, new state format, common ledger or duplicate BPMN token semantics is added; original interpreter state remains historical on cancellation.

[Seventeen raw assets](manifest.json) retain six successful complete Runtime/Design stores across three owned SIGKILLed processes, plus the original four-store red reproduction. The host regression fails before the fix and passes after it. Whole BPMN456/456 and the actual cancellation host1/1 pass, zero skips. Ten portable checks pass, including nine rehashed corrupt-copy controls over pending settlement, obligation state, source ACK ordering, native pins/outcomes and the unhidden red result. All104 frozen baseline assets remain unchanged.

This qualifies one direct model with two real Event workers restored from its actual captured state. Embedded/native-parent sibling interruption, a crash during pending model settlement, broader faults/retention/providers/rollback and final affected-source upgrade/Studio remain separate gates. Previous finite and broadcast receipts keep their executing sources.

```sh
python3 e2e-tests/bpmn/verify_sibling_cancellation.py specs/1017-direct-bpmn-hosting/evidence/events/sibling-cancellation/manifest.json
python3 -m unittest discover -s e2e-tests/bpmn -p test_verify_sibling_cancellation.py
```

The producing host regression is `SignalCancellationWorkbenchTests`. It restores repository-owned actual captures into fresh private stores and only stops its own Workbench children. Temporary machine paths are not continuation inputs.
