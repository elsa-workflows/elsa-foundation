# Actual Studio claims and interruption (T034)

Products `26d8f3256` (Workbench, Studio.Web and the Studio browser bundle, rebuilt from that commit); harness `86438f687`, which changes only `tools/bpmn/`. The owned controller (`run_owned_studio_upgrade.py --family direct-human`) uses fresh isolated stores and owns its children. It captures coupled whole Runtime/Design stores and typed reads at every stage. The driver is `tools/bpmn/drive_claims_studio.mjs`. [Receipt](claims-studio-receipt.json).

Every claim and claimed completion is a real Studio UI action. A separately signed-in operator API client does only what Studio deliberately does not offer:
- publishing the timed fixture, because Studio has no timer authoring;
- starting the two interruption runs and cancelling one, because there is no operator console;
- recording the refusals of late requests.

1. **Authoring:** create a BPMN workflow, import the human fixture, choose Human wait and check "Require a claim before completion". Save, reload and the choice persists. Publish BPMN publishes `foundation-direct-claims-v1`.
2. **Claim:** start and open the run. It shows "Unclaimed" with only Claim work offered. Claiming shows "Claimed by you." and "Last claim: accepted." Stores captured (`claimed`).
3. **Contention after a real restart:** the backend restarts and seeds a second administrator. That fresh subject sees "Claimed by <owner subject>." with Complete work disabled ("Only the current owner can complete this work.").
4. **Completion and replay:** the owner signs in again and completes; the run shows Completed with no outstanding work. After another real restart, "Replay last completion" returns the same result, "accepted (replayed)".
5. **Timer interruption:** raw format 6 material with a PT20S duration boundary on the claimed task. Claimed in Studio; the real timer then ends the process (Completed, revision 3, no outstanding work, the claim retained as history). The owner's late claimed completion is refused with 409 `execution-terminal`.
6. **Cancellation interruption:** a second authored run is claimed in Studio and then cancelled through the authorized API. Studio shows Cancelled with no work. The owner's late claimed completion is refused with 409 `execution-terminal`.

Typed reads at every stage show frame 15 with the retained claim inventory, and lifecycles move from Running to TerminalSettled exactly as displayed. There were no page errors. Screenshots were visually inspected. The first real run exposed an oversized claim checkbox; it was fixed (`26d8f3256`) and this whole journey repeated on the fixed products. Two earlier driver runs stopped on harness errors: an expected refusal code (the timer ends the process first, so the code is `execution-terminal`) and reading a recorded response before its body settled. Both are corrected; neither was a product failure.

The finished whole stores are retained here ([runtime](finished-runtime.sqlite.gz), [design](finished-design.sqlite.gz)). Every stage's hashes are in the receipt; the remaining 16 stage stores stay local for the next evidence archive.

Service settlement and replay through Studio were proved by the actual ETL journey (T021/T025/T026: held failure, exact UI retry, lost accepted response, reload and restart replay) and are not repeated here.

Limits:
- The owner is shown as the raw subject id; resolving it to a display name needs an identity lookup outside this scope.
- A raw-published process without a saved diagram is listed without its canvas.
- Both end events report Completed/Done, so Studio does not distinguish the timeout path; the backend refusal and the absence of any completion establish it.
- Not renewed here: theme/responsive/keyboard checks for the new controls.
