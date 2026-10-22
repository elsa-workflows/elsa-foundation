# Data model: Tiered CI with parallel test shards

This feature has no runtime entities. Its data is one committed manifest, `tools/ci/test-tiers.json`, and one per-shard result file. Both are plain JSON read by `tools/ci/test_tiers.py`, `tools/ci/test_results.py` and the generator's `test-tiers-check`.

## Tier manifest — `tools/ci/test-tiers.json`

It replaces `tools/ci/ef-suites.json` and is the single list that both tiers and every shard job read (FR-015).

```jsonc
{
  "schema": 1,

  // FR-011's switch. false: integration-tier projects still also run in the PR tier.
  // Set to true only after `Full tier` is a required context of ruleset main-2.
  "integrationLeavesPrTier": false,

  "shardTargetMinutes": 6,          // PR-tier target per shard (research R6)
  "slowTestSeconds": 10,            // FR-019 threshold
  "slowTestMode": "report",         // "report" | "fail"; "fail" needs FR-020's owner approval

  "projects": [
    { "project": "Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj",
      "tier": "integration", "trait": "child-process", "medianMinutes": 27.6 },
    { "project": "Essentials/tests/Primitives/Tests/Elsa.Primitives.Tests.csproj",
      "tier": "pr", "medianMinutes": 0.0 }
    // ... every test project, exactly once
  ],

  "splits": {
    "Essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj": {
      "groups": [
        { "name": "workbench-native-hosting",
          "classes": ["Elsa.Workbench.Tests.NativeHostingConformanceTests", "..."] },
        { "name": "workbench-rest", "rest": true }
      ]
    }
  },

  "shards": {
    "pr":          [ { "name": "pr-1", "units": ["<project>", "<project>#<group>"] } ],
    "integration": [ { "name": "int-1", "units": ["<project>#workbench-native-hosting"] } ],
    "containers":  [ { "name": "ef-1", "suites": ["migrations", "runtime"] } ]
  },

  "containerSuites": [
    { "suite": "migrations",
      "project": "Essentials/tests/Persistence/EntityFrameworkCore/Migrations/ProviderTests/Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests.csproj",
      "require": "ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX" }
    // ... the 23 suites moved from ef-suites.json, unchanged
  ],

  "slowAllowList": [
    { "test": "Elsa.Cli.Tests.SomeClass.Some_test", "reason": "starts a real host" }
  ],

  "quarantine": [
    { "test": "Elsa.Workbench.Tests.NativeHostingConformanceTests.Original_uncancelled_checkpoint_is_rejected_after_a_real_successor_acquires",
      "issue": 3147, "since": "2026-10-22" }
  ]
}
```

### Fields

| Field | Meaning | Validated by |
|---|---|---|
| `projects[].project` | Repo-relative path of a test project: a project under `Essentials/tests/**` or `Extensions/*/tests/**`, outside `studio/`, that references `Microsoft.NET.Test.Sdk` | `test-tiers-check`: each test project appears exactly once, and every entry exists and is a test project (FR-016) |
| `projects[].tier` | `pr` or `integration`; the whole project is one tier (FR-018) | `test-tiers-check` |
| `projects[].trait` | `testcontainers`, `child-process` or absent. It is never trusted: the check recomputes it. | `test-tiers-check`: a recorded trait must match the detected one. A project with Testcontainers must be `integration`; so must a project with a child host and `medianMinutes` ≥ 2 (FR-017). |
| `projects[].medianMinutes` | Median duration from the latest rebalance. It is informational and drives `rebalance`. | none (data) |
| `splits` | Class groups for projects above the shard target. Exactly one group per split project has `rest: true`. | `test-tiers-check`: every class named exists in the project, no class is named twice, and classes sharing an `[Collection]` are in the same group |
| `shards.pr` / `shards.integration` | The committed shard grouping. A unit is a project path, or `<project>#<group>` for a split project. | `test-tiers-check`: every unit of the tier appears in exactly one shard of that tier. A split project appears only through its groups. While `integrationLeavesPrTier` is false, `shards.pr` also covers the integration units. |
| `shards.containers` | Container suites per EF job | every `containerSuites[].suite` appears in exactly one shard |
| `containerSuites` | The EF container legs, moved from `ef-suites.json` with the same fields | `ef-suites-self-test` (retargeted): every Testcontainers project of the solution, plus the Secrets EF composition project, is covered |
| `slowAllowList` | Tests allowed above `slowTestSeconds` in the PR tier, each with a reason | `test_results.py` excludes them from the FR-019 report |
| `quarantine[].test` | Fully qualified name of a test method, without theory arguments | `test_results.py check-quarantine`: `issue` exists and is open (FR-013). On a PR, only for entries the diff adds or changes; on `main` and nightly, for every entry |
| `quarantine[].issue` | Its tracking issue number | same |

### Derived rules

- **Tier of a project with no trait**: `pr`, unless after splitting it would still exceed `shardTargetMinutes` on its own (research R5). No project meets that today.
- **Which shards run where:**

  | Event | Shards run |
  |---|---|
  | Pull request (`ci.yml`) | `shards.pr`. While `integrationLeavesPrTier` is false, those shards include the integration units. |
  | Full tier on a pull request (`ci-full.yml`) | `shards.integration` + `shards.containers` |
  | Push to `main` | `shards.pr` + `shards.integration` (no containers, as today) |
  | Nightly, `workflow_dispatch tier=full` | `shards.pr` + `shards.integration` + `shards.containers` |
  | `workflow_dispatch tier=pr` | `shards.pr` |

- **The switch (FR-011)**: flipping `integrationLeavesPrTier` to `true` removes the integration units from `shards.pr` in one reviewed diff. On the pull request that changes the field to `true`, `test_tiers.py select` refuses unless ruleset `main-2` lists `Full tier` as a required context; it reads that through `gh api repos/{owner}/{repo}/rulesets`, which needs only read access. It does not check on other runs, so the network call cannot become a standing flake source.

## Shard result — `test-outcomes.json` (artifact per shard)

Written by `test_results.py evaluate`, read by `ci-flake-record.yml` and by the aggregate jobs' summaries.

```json
{
  "schema": 1,
  "shard": "pr-2",
  "headSha": "<40 hex>",
  "failedTwice": ["Ns.Class.Method"],
  "passedOnRetry": ["Ns.Class.Method"],
  "quarantinedFailed": [{ "test": "Ns.Class.Method", "issue": 3147 }],
  "slow": [{ "test": "Ns.Class.Method", "seconds": 12.4 }],
  "aborted": null
}
```

- A shard **fails** when `failedTwice` is non-empty, or when `aborted` names a hung test or a crashed test host.
- **Validation in `ci-flake-record.yml`**: before any name is written to the flake-tracking issue, each one must match `^[A-Za-z_][A-Za-z0-9_.]*$`, and `headSha` must be 40 hex characters. An artifact that fails validation is dropped and logged.

## State: a pull request's checks

| PR state | `Build & test` | `Full tier` |
|---|---|---|
| draft, manifest untouched | runs (PR tier) | not reported; the aggregate reports as `Full tier (not run: draft)`, so the required context is missing and the head is not mergeable |
| just marked ready, full-tier run not started yet | unchanged | still missing, so the merge stays blocked |
| draft, diff touches `tools/ci/test-tiers.json` or the tier tooling | runs | runs for real |
| opened as ready, reopened while not a draft, or marked ready | runs on open and reopen; not re-run on `ready_for_review` | runs on that head |
| push while not a draft | runs | runs |
| documentation-only diff (spec 257) | fast path, success | fast path, success |

## Job budget accounting

FR-022 counts test jobs.

- **PR tier**: 3 shards, `Architecture guards` and `Core-only build & test`, so 5. `Plan tiers` and the `Build & test` aggregate are bookkeeping jobs of under a minute.
- **Full tier**: 1 plan, 4 integration shards and 3 EF container shards, so 8. The `Full tier` aggregate is bookkeeping.
- **Peak per PR with both tiers**: 5 + 8 test jobs, plus 4 bookkeeping jobs, plus `Generated maps fresh`. That makes 18 for a short moment, and 14 for most of the run. This meets SC-006's "one PR-tier run and one full-tier run at the same time" within 20.
