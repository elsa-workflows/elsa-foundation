# Source-bound BPMN delivery evidence

The raw historical proof at commit `f1546a7f8c3e0a522bff1db1bd98bae6b824ae0a` is retained as verified assets on the existing contributor preview release. [archive.json](archive.json) pins the public asset IDs, sizes, SHA-256 digests, source commit and original frozen manifest. Both archives were downloaded again and every member was compared with the original bytes before raw files were removed from the PR's final tree. The complete archive retains all 9,410 original files, including failed attempts, actual legacy checkpoints, whole stores and screenshots. Its separate index supports inspection without downloading the complete archive.

Restore the 94 unchanged inputs required by the .NET test projects before a full solution build or those projects' tests:

```sh
python3 tools/bpmn/restore_evidence.py
```

CI runs this explicit prerequisite before building the relevant test projects. It downloads the 20.2 MB fixture archive, verifies its pinned digest, and restores the original paths. Tests run against the original captures; no state is reconstructed or replaced with synthetic fixtures. Subsequent restores verify existing inputs and reuse the cached archive.

For historical qualification scripts, strict verifier controls, or inspection of raw paths linked from older receipts, restore the complete archive:

```sh
python3 tools/bpmn/restore_evidence.py --all
```

The complete restore preserves current tracked receipts and source files. Hydrated raw files are ignored by Git; existing files with different bytes are refused rather than overwritten. The original frozen authoring manifest remains unchanged. A file-only receipt or successful restore does not establish upgraded-host recovery, Studio compatibility, or any broader program acceptance: follow each qualification's declared executing source and limitations.

[qualification.md](qualification.md) and the current round-10/round-11 correction receipts retain the production evidence and its scope. Earlier raw links use the original archived layout after restoration. The author review loop and current delivery state remain on [PR #3410](https://github.com/elsa-workflows/elsa-foundation/pull/3410).

The pinned `sourceHistory` Git bundle also preserves the original branch and executing source commits. In a normal Foundation clone, download that bundle, verify its pinned SHA-256, run `git bundle verify <bundle>`, then use `git fetch <bundle> refs/codex/verification/bpmn-source-f154:refs/archive/bpmn-source-f154` when reproducing an older source. Routine test/CI restoration does not fetch that historical Git pack.
