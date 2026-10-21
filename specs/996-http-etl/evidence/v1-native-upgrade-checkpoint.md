# Frozen v1 native upgrade acceptance

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), task T003. This closes the worker/native recovery gap left explicitly open by the [retained-artifact checkpoint](v1-compatibility-checkpoint.md). It does not complete the HTTP leaf's final integration and delivery gates.

## Executed upgrade

The producer is the frozen commit `b555bd8460270476e49496e519c69b65180e9505`, in a newly created detached checkout. Only three existing test files are adapted and one opt-in test source is added. Every other tracked file, including production source, project references, build inputs and package locks, is compared with that commit before and after execution. Restore uses locked mode. The successor is `c53547883954a52c9ad903287eec149011bf23ea` plus the recorded test-only working-tree changes in this checkpoint.

The producer authors and publishes a v1 ETL pipeline and native Sequence through the real persistence/publication paths. It starts the parent, observes the actual suspended `RunEtl` activity and bookmark, then starts an independent predecessor worker. That worker writes the JSONL chunk and pauses before acceptance. The persisted run has zero accepted records, the exact owed batch and matching outstanding record/byte charges; its manifest is empty and the following activity has not executed. The worker is terminated and the native host is disposed. The handoff records the lease expiry actually persisted after the worker stops, rather than the potentially older receipt expiry.

The successor opens those same files at their original absolute paths. It does not author, republish, start a new parent, rewrite identity-bearing rows, retarget pins, or modify the clock. The design database remains offline. The test checks the exact artifact bytes, invocation/result address, binding configuration, prepared state, reservation, owner/fence and output hash before starting work. It waits for the retained lease's real expiry if needed.

A separate current worker accepts the same batch and route effect with a newer fence. After release it settles the ETL run; the current native adapter then delivers the terminal result. The same persisted Sequence and its following activity complete, one trigger receipt is consumed, its bookmark is removed, the typed `RunEtlResult` matches the durable success result, and the terminal outbox is acknowledged. Accepted records equal one, outstanding charges equal zero, and the sole manifest entry retains the original output identity and bytes (`{"name":"Ada"}\n`). This proves one persisted logical continuation, not a general exactly-once guarantee for external effects.

## Reproduce

From the repository root, with Git, Python 3.11+ and the repository's .NET SDK:

```bash
python3 Extensions/Etl/tests/Shared/test-v1-native-upgrade.py
```

An optional `--evidence-root /absolute/new/directory` selects a new evidence location. Existing destinations are refused. The runner invokes one producer test and one consumer test with `-p:EtlV1NativeUpgrade=true`; it requires exactly one passed test in each TRX, with no failures or skips. These facts are absent from normal test discovery. The ordinary published-workflow tests retain their default fixture behavior.

The runner keeps the live root unchanged between versions and archives a copy only for evidence. It records commands, SDK details, test-only patches and source hashes, each phase's assembly hashes, before/after payload hashes, SQLite integrity and migration histories. It rejects unrelated successor changes during this proof. The successful disposable predecessor checkout is removed; failure checkouts are retained for diagnosis. The runner never indexes the checkout.

## Results and evidence limits

Final runner: `/tmp/3151-v1-native-upgrade-final2`, exit **0**. Producer **1 passed**, consumer **1 passed**, no failures/skips. The predecessor had only `20261020120244_EtlRuntimeInitial`; the successor had that migration plus all **eight** additive connector runtime migrations. Integrity checks returned `ok` for the runtime, workflow and offline design databases before and after replay. Original evidence hashes remained unchanged. Root separately read the TRX counters, schema receipts and completion receipt.

An earlier complete pair also passed at `/tmp/3151-v1-native-upgrade-attempt2`. Before it, attempt1 failed compilation because the new test accessed a private nested helper; the redundant access was removed, while the existing completion wait still explicitly requires outbox acknowledgement. One subsequent run was stopped during compilation after root caught an incorrect migration-inventory directory in the runner; no acceptance result is claimed for it. Final source adds the independent review's exact byte accounting, post-exit owed-state, retained artifact, migration and typed-result assertions.

Normal regression: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -p:RestoreLockedMode=true --filter 'FullyQualifiedName~EtlV1CompatibilityFixtureTests|FullyQualifiedName~EtlPublishedWorkflowJourneyTests' --logger 'console;verbosity=minimal'` passed **7/7**, with no failures/skips (`/tmp/3151-t003-normal-regression.log`). A subsequent `--no-build --no-restore --list-tests` confirmed the opt-in producer/consumer facts are absent from normal discovery. Root and independent final review found no remaining actionable issue; three bounded review passes corrected the harness gaps described above. Generated maps also passed without changes (`/tmp/3151-t003-maps.log`); 55 relative documentation links and staged/unstaged diff checks passed. Broader full-suite, architecture/core, map, review and exact-head CI checks belong to T020–T022 after integration with current main. Earlier-head results are not that final gate.

Private databases, assembly snapshots, authentication-free local diagnostics and machine paths remain local; none are committed as new fixtures. This is a same-machine/same-path upgrade from the identified baseline. It does not claim arbitrary historical version compatibility, relocated/cross-platform fixture replay, power-loss qualification, or changed remote-effect guarantees. The existing portable static fixture remains a separate artifact/store proof. No production code or dependency changes were needed for T003.
