# Saved broadcast recovery with the admitted header schema

Executing source **4716b51ec75284efa2b85fdf3b36a5125571335b** fixes and requalifies the legacy-broadcast failure from [CI at aef0d539b](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40925038408/job/123628697925). That job failed the same saved recipient twice; focused local execution reproduced it. Both original waits remained Suspended and the native workflow remained Running even though the broadcast source was Delivered.

The [actual failed API/store capture](failure/manifest.json), from standard-host source fe3b77bcc, retains the blocking incident. Exact bookmark resume touches the native owner header's CAS revision even when the checkpoint changes only activity state. That metadata-only write kept the legacy header stamp `1.0.0`, while this host's admitted family writer was `2.0.0`. The schema gate correctly refused the write. Source acknowledgment proved delivery acceptance; it did not prove recipient progression.

The fix uses the existing family version selector for metadata-only owner-header touches in both exact bookmark resume and model-parent wait admission. It preserves the schema guard, supported-version floor, native JSON, live-owner checks and CAS behavior. It introduces no new schema protocol or migration override. Ordinary participant staging already uses that selector; the revision-only path had missed it.

The new provider regression passes under a held v1 gate with storage stamp 1 and under the active v2 gate with stamp 2. In both cases native `ContentJson` remains byte for byte identical and the original CAS revision advances exactly once. The active-v2 case failed the real gate before the fix. Focused EF schema/resume/model tests pass **106/106**, zero skips.

The [successful upgrade matrix](manifest.json) reuses all four original whole-store captures: before first selection, accepted cohort, after recipient resume before source ACK, and authoritative empty cohort. Standard hosts recover the exact original recipients and keyed starts, preserve original snapshot/source fingerprints, exclude newly re-armed listeners, and retain immutable pins, artifacts and migration history. Original infinite loops and excluded listeners retire through existing native control paths. Three real Workbench processes per cut preserve recovered and final truth. All twelve qualified hosts have stopped.

Eight complete upgraded stores, source-bound HTTP/checked observations and the process receipt are retained. Both actual published BPMN descriptors match the frozen authoring fingerprint. All 104 baseline assets and their manifest remain unchanged. [Qualification](qualification.json) records the failed counterexample, regression and actual recovery scope. The original failed store is separate from the qualifying successful bundle.

Verify the committed recovery bundle:

```sh
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/broadcast-header-schema/manifest.json
```

To reproduce on a clean source, use `resume-upgrade --family broadcast` with the frozen baseline manifest and a fresh output directory. It runs the standard host test and checks the original inputs before and afterward. No timeout, retry, quarantine, schema hold or guard bypass makes this receipt pass. All eight actual RunBpmn human-model host regressions also pass on the fixed source, zero skips.

This requalifies the legacy-broadcast arm only. Other legacy families and final affected-source upgrade/Studio, direct model broadcast, fault mappings, held retry and remaining provider/rollback acceptance stay open. #2898/#2907/program remain incomplete; full-PR review rounds remain 0 of 6 and remote checks must pass on the final reviewed head before merge.
