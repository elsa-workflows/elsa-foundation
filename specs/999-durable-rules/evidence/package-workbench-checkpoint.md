# Rules package and Workbench checkpoint

This checkpoint completes T014, T015 and T031 of [the task list](../tasks.md). It does **not** close #3181 or qualify retained upgrades, all native recovery cases, or the whole program. The [checkpoint receipt](package-workbench-checkpoint-receipt.json) identifies immutable inputs, commands, failed attempts, final checks and external logs.

## Actual package deployments

The six Rules modules and their 20-archive dependency closure were packed from source `26c645ba4691ac0634ae3232f9bd3868f1f72ecd`, using publish-state `eb3e12531b0cbd594374df1e49df0fadef9a50f3`. Versions were calculated from those inputs. No packages were released.

The [Workbench qualification receipt](package-workbench-qualification.json) records a fresh external consumer referencing all six module roots, with zero direct or transitive project libraries. It checks the complete packed closure, direct execution, incompatible pin and changed deployment rejection. Removing the actual assembly-asset identity comparison makes the intended assertion fail; restoring source and deployed bytes restores successful direct execution. NuGet content hashes, raw archive digests and selected assembly hashes are recorded separately.

Workbench itself is a mixed host: all six Rules modules resolve from the same actual packages, while its other dependencies may remain source projects. The qualification creates a temporary copy of the immutable Workbench project at the same directory depth, preserves its source/configuration and ordinary references, and replaces only four direct Rules project references with six exact package references. Production Workbench references remain unconditional. The generated project is retained as evidence, the temporary source copy is removed on success and failure, and the harness checks that the retained source checkout stays clean. Each selected Rules DLL must match its archive asset; the three semantic package identities must match the direct consumer's deployment receipt before that receipt is copied to the host.

The [process qualification receipt](package-process-six-modules-qualification.json) is deliberately different: its clean consumer references the four direct runtime/storage module roots from that same packed feed, with no Design/Definitions packages. It verifies accepted and consumed boundary recovery, delayed predecessor responses, cancellation, a live-successor stale response, and all three persistence removal/restoration controls on real PostgreSQL processes. The owned database container was removed. This is same-machine process evidence, not a multi-machine cluster qualification.

## REST journeys

The [REST receipt](package-workbench-rest.json) comes from the published package-backed Workbench, an owned loopback process, fresh SQLite databases and real login/catalog/submit/publish/start/inspection APIs. Rules remains opt-in in shell configuration. The journeys demonstrate:

- Sequence: durable Rules completion, captured full result, and the following activity exactly once.
- Flowchart: durable Rules completion, result capture and following activity exactly once on a linear path.
- One-round budget: native workflow and Rules activity fault, a blocking incident, and no following activity. The exact Rules diagnostic is not available from this REST projection and is not claimed.
- Out-of-vocabulary input: retained `Refused` result, zero committed rounds, empty facts and `rules.invalid-finite-model`; the Sequence continues.

Both success cases return `Succeeded`, two committed rounds and exactly `derived,seed`. After all four journeys terminate, the runner restarts its owned host using the same explicit assembly path, reconnects, and rereads both complete success results. This is terminal persistence/readback evidence, not mid-flight recovery, a Flowchart sibling/join proof, or a native unavailable-retry test.

## Findings and corrections

The first package-host run passed packaging and direct proof but failed at Flowchart result capture. Reordering the capture immediately after Rules still failed. [Persisted diagnostics from both runs](workbench-rest-scope-diagnostics.json) identify the actual mismatch: the binding declared the outer executable node ID `flowchart-root`, while the producer's local Flowchart execution scope was `scope:root`. Correcting that binding passed without changing runtime causal/scope checks. The earlier explanation that the intermediate WriteLine caused the failure was incomplete.

The architecture guard then rejected conditional production project references and the dynamic import used for qualification. The temporary copied-project approach removes both violations without a guard exception. A later no-build architecture attempt correctly rejected the stale local Workbench assembly after its project file changed; the host was rebuilt before the final guard run.

Review also found that referencing all six modules made the process witness violate its intentional absence of Design packages. The consumer now selects its roots according to the proof being run and records them explicitly. Finally, the generated host initially left the retained source checkout unclean. Temporary-directory ownership now covers success and exception paths; a forced restore-boundary exception checks cleanup, and successful qualification checks source cleanliness.

## Compatibility evidence and limits

The full Workbench suite initially returned **61 passed, 1 failed, 0 skipped**. `Original_uncancelled_checkpoint_is_rejected_after_a_real_successor_acquires` timed out waiting for successor acquisition; retained logs show SQLite lock errors during startup before the stale-checkpoint assertion. After those diagnostics were inspected, its isolated rerun passed **1/1**. Rules features are not enabled in that fixture. This does not identify the lock owner or establish the cause, and the full suite is not described as green. The receipt preserves both runs.

The remaining native gate must exercise temporary unavailability inside real Rules bookmark resumption, subsequent recovery and proof-before-acknowledgement against persisted state. Cancellation and sibling/join source fixtures remain separate evidence. Retained native schema coexistence and Rules deployment upgrade/admission-disabled rollback are still pending. External effects retain their existing at-least-once limits. No Orleans, general task runner, workflow/permission change, or production deployment is included.
