# Complete PR3410 round5

Reviewed head `1e707d58fc2148be3715d50616dfdb69dc82ae65`, base `7b49513650665606e5f52fdc08c5831f60802dac`. Three independent whole-PR lenses completed. **Four P2 findings are adopted; none dropped. This round is not clean.** Five full rounds have completed, none clean; one remains under the six-round bound. The PR conversation is the authoritative counter.

- C1: fully settled models retain live in-process mailboxes indefinitely. Eight actual completions retain eight mailboxes; explicit passivation releases them without losing exact retained replay.
- S1: shared owned model-envelope decoding exposes raw JSON errors without execution/input/work context before claim. The actual EF queue remains unclaimed.
- S2: model-only checkpoint provider failure bypasses the existing normalization boundary. Actual SQLite rollback is correct; the original error escapes without model execution/input/scope/commit context.
- A1: startup skips an actual old embedded fault still pending parent settlement. One ordinary upgraded host delivers the parent ACK and leaves another Event worker Running with all sources Delivered and empty queues. A second startup clears it, proving the missing trigger.

The actual old pending-parent whole store is captured through normal old publication/native/model/result ports before any affected correction. The decoded input hash is `0a5ea64058dcdb950fdda9e5b792264ceec2f6ca19fa9398a7733f6c62ea097e`. The producer uses retained e3ec219fc production binaries with verified old hashes. Current upgrade and second-start control hosts execute exact1e707d58. Original fault/interpreter state, pin/incarnation/authority and remaining physical wait are retained. No state/queue/receipt/authority was edited through SQL. Both current hosts are stopped.

Reports and source-bound diagnostics remain under each lens. Standalone probe projects are archived as `.csproj.gz` to keep historical reference inputs out of the active solution. Logs retain their original bytes and existing trailing blank lines. Temporary project paths are provenance; captured closed inputs and supported explicit-product reproduction adapters are the portable boundaries. Live databases, sidecars, bin/obj, private host roots and identity stores are excluded.

Correctness independently passes481 BPMN and50 focused EF tests at its explicitly equivalent source. Acceptance independently passes259 ETL and13 current Studio controls; standards' migration generator tests pass4/4. Their probes/review do not replace affected fix qualification or exact-head CI. Earlier6200/0 and full Studio receipts retain their executing sources.

T041/T043/T044 reopen for the affected corrections and qualification. Owner API/version-magnitude/needs:decision, deferred GitGuardian39189012, exact required checks, protected merge and resulting-main verification remain open. #2898/#2907/#2908/#2892 remain open/Partial. First-class direct-model Studio, wider/upgraded-host ETL, non-SQLite actual-host acceptance, native fault policy#3242 and Orleans/task-runner deferrals remain unchanged.
