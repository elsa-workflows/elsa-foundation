# Actual pre-claim writer checkpoints

Production source `1d77ed131de3305da8b8236f739c63085bff3e73`, before changing human claims. Collector executed from spec-only `e2caae036c88ed9cb9625b0db88e65f53a8cb22e` and verified production-tree identity.

[Archive](https://github.com/elsa-workflows/elsa-foundation/releases/download/contributor-preview-2026-10-07.1/elsa-bpmn-human-preclaim-1d77ed131.zip):24 files including collector/source/product hashes, API observations/XML negative, consistent stopped Design/Runtime stores at actual wait and completion. [Receipt](receipt.json) records checksum/upload/download/member verification. No new release/tag.

Observed: draft save/reopen/promote passed; direct human wait → actual restart → completion → replay passed. Both documents use adapter format1, revisions1/2; pin/Authority/incarnation match; native workflow/activity/bookmark rows zero. Candidate metadata survived JSON but **was lost on XML export/import**. This is not direct Studio, new authored publication or claim acceptance.

Reproduce on the pinned source or production-tree-identical checkout, after building existing host/reader:

```sh
python3.14 specs/1184-direct-bpmn-studio/evidence/preclaim/capture.py \
  --repo . \
  --workbench-assembly Essentials/apps/Elsa.Workbench/bin/Release/net10.0/Elsa.Workbench.dll \
  --reader-assembly tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll \
  --output /tmp/elsa-human-preclaim
```

Collector refuses changed production trees, owns/stops only its children and snapshots whole stores after stopping them. Excludes private identity/config/logs. Preserve archived inputs unchanged for upgraded-reader checks.

## Old-input controls before the claim writer (T027)

The two actual pre-claim captures are confirmed and now guard the current host. [fixtures.json](../fixtures.json) pins 16 exact members of this archive and of the [Studio P1 archive](../human/README.md). Each pin matches that archive's own index. `tools/bpmn/restore_evidence.py` verifies each archive and member digest and restores them into the ignored `evidence/fixtures/` folder, which CI already restores before building.

`DirectHumanPreClaimUpgradeTests` restores each capture's wait and terminal Runtime/Design stores into the current Workbench:

- **Wait store:** the restored view, the current work or the full inspection equals the capture. An obsolete attempt is refused without changing state. The original completion command returns the original Accepted response and then its replay, both deep-equal to the captured responses. Revision, result and pins match the captured terminal. The replay survives a restart.
- **Terminal store:** the view and inspection equal the capture. The original command and its command record replay the captured result. A new request is refused as `execution-terminal`, and both survive a restart.
- **Both:** material rows keep their exact bytes and incarnations, and native workflow/activity/bookmark rows stay zero.

At `bc5f464af` the four controls and the existing direct-human witness pass. A temporary changed request id, with the original attempt id, fails all four. See the [receipt](upgrade-controls.json). The claim writer has not changed. T028–T029 must keep these controls passing unchanged, and future claim captures are added beside them, never in place of them.
