# Actual duration boundary recovery without a native carrier

Executing source: `a2c2e47751d595c9489f889593ed4d7427ef6076`. Two authenticated production Workbench journeys qualify human-first and timer-first decisions in runtime-only deployment. The host has no Design storage. All checked and physical stores have zero native workflow, activity and bookmark rows.

The process binds one human review and an interrupting boundary with a real30-second duration. Foundation records its deterministic invocation/attempt, full model pin and deadline in private state11, with a source committed through the existing delayed outbox. Its `NotBefore` equals the accepting checkpoint's UTC time plus30 seconds. The original source and both waits survive the first SIGKILL/restart without being recreated or repinned.

In human-first, normal HTTP completion accepts revision2 before the deadline. That winner survives another process loss while the timer source is still pending. The third standard host delivers the original timer after its deadline; its exact retained receipt is Rejected/execution-terminal at revision2. In timer-first, the normal delayed delivery accepts revision2 and the library routes the timeout path. After another process loss, normal HTTP completion of the original human attempt is rejected at the same revision. Both cases preserve the actual winning library diagnostics and suppress the other route. Exact source acknowledgment follows the timer input receipt. A fourth process retains identical checked model/input/source/queue and API views.

The [manifest](manifest.json) binds28 raw assets and eight whole Runtime stores at before, decided, finished and retained boundaries. All eight listed Workbench children are stopped; each actual exit is137/SIGKILL. The checked observer reads through the real EF provider in read-only mode. Portable checks additionally inspect physical model state, exact input receipts/fingerprints/attempts, immutable timer source/deadline/ACK, explicit material binding inventory and absence of native carriers. The observer and seven captured first-party host products match the executing source; released BPMN Model/Semantics0.5.0 binary hashes are retained separately.

Definition4 adds explicit duration bindings; state11/12 distinguishes new timed direct/embedded frames so older drivers refuse unsupported data. The existing format1–3 material wires and old state families remain intact. The released library still owns interruption/token semantics. Human completion excludes timer listeners. Model input validation independently refuses early, wrong-scope, wrong-attempt and changed-deadline timer inputs, even if a caller knows the retained source address.

The eligibility regression fails when only the elapsed-deadline guard is removed, then passes after exact source restoration. Initial development controls also found and corrected native-only dependency injection and the timed human-completion admission guard. These are development results; the qualifying actual journeys above ran afterward on the named committed source. No execution state or clock was edited in these journeys. Controlled-clock provider tests are separate from the actual wall-clock/process evidence.

Run from any checkout:

```sh
python3 tools/bpmn/verify_duration_timer.py --manifest specs/1017-direct-bpmn-hosting/evidence/races/human-duration/manifest.json
python3 -m unittest discover -s tools/bpmn -p 'test_verify_*.py'
```

To reproduce, build the hosting test project from a clean committed source with `-p:ElsaBpmnRecoveryProbe=false`. Set `ELSA_BPMN_HOST_SOURCE_REVISION` to that commit and `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY` to a fresh output directory, then run the `DurationTimerWorkbenchTests` filter with `--no-build`. The controller creates private stores/configuration and owns only its own processes. Historical paths, ports and process IDs are provenance only.

[Qualification](qualification.json): BPMN431/431, actual timer host2/2, existing human-host9/9, native-service host1/1, portable tools27/27 including13 new controls, existing ETL controls208/208, maps and123 test-tier registrations pass with zero skips. All104 frozen baseline assets and the original manifest remain unchanged. This qualifies same-source human/timer recovery only. Real worker/timer orders, held-failure interruption, simultaneous retry/cancel commits, direct model broadcast/provider/retention/rollback and final affected-source upgrade/Studio acceptance remain open. T020/T023/T024, #2898/#2907 and program#2892 stay incomplete. Full-PR review rounds remain0 of6.
