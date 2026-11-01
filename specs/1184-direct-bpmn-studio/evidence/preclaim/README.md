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
