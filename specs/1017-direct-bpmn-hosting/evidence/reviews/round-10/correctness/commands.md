# Commands and source boundaries

Working directory: /Users/sipke/.codex/worktrees/f236/elsa-foundation.

Read-only candidate verification:

```sh
git rev-parse HEAD origin/main
git status --short
git diff --name-status e26c11bd94bc7a99d6cd0c522ada89961ef72aa5 5ff0d52c6245ab89bc673e45da683a0f6c3cc141
git diff --check e26c11bd9 5ff0d52c6 -- Essentials/src Studio/src
git diff --exit-code 43741654e 5ff0d52c6 -- Essentials/src
git diff --name-only 43741654e 5ff0d52c6 -- Essentials/tests/Activities/Bpmn Essentials/src/Activities/Compensation Essentials/src/Workflows/Runtime/Resumption
/Users/sipke/.local/bin/gh-codex api repos/elsa-workflows/elsa-foundation/pulls/3410 --jq '{head: .head.sha, base: .base.sha, draft: .draft, state: .state}'
```

The two source-equivalence commands produce no diff. The recorded Release product file versions/hashes are in referenced-release-products.json. No referenced production assembly was executed by the round10 private helper.

Three private bootstrap build attempts used the same command with sequential edits limited to /tmp/elsa-2898-review10-correctness/probe:

```sh
dotnet build /tmp/elsa-2898-review10-correctness/probe/probe.csproj -c Release --nologo
```

All three failed before any behavioral execution: build.log misses the composition module references, build-2.log misses the Resumption reference, build-3.log reaches local helper type/import and constructor/request signature defects. The build-slot wrapper was honored. No fourth build or runtime invocation was performed. The parent explicitly directed completion through read-only review and retention of the three setup logs. These failures are not product negative controls, failed regression assertions or proof of the static finding.

Before the planned probe, Python gzip.decompress copied all three complete original files listed by original-inputs.json to delayed-retry/{elsa,etl-runtime,etl-design}.db and copied the original process receipt. Nothing opened those stores through runtime services. Their original hashes are rechecked in untouched-input-check.json. The incomplete Program.cs is a proposed private producer, not a reproduction claimed to run.

Independent Python read-only SHA256 checks verify all156 assets in the committed round9 correction qualification manifest, including decoded hashes where supplied, and all104 baseline assets. qualification-manifest-check.json and baseline-manifest-check.json retain the results. The discussion cache was read only after parent confirmed its refresh complete; discussion-receipt.json hashes every refreshed export. Existing full-history discussion and prior findings were reconciled; new/updated comments since the prior review are additionally retained in new-updated-discussion.json. No current-round peer output was read.
