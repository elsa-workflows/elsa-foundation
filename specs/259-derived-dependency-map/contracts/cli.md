# Contract: calculator and generator command lines

## Calculator: `--dataset`

```text
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  (--record <file> | --record-ref <revision> [--record-path <path>]) [--commit <revision>] [--repo <dir>] [--output <file>] \
  [--pack-properties <file> --branch <name>] [--force-advance <ids> --force-reason <text>] \
  [--dataset auto|committed|derived]
```

| Value | Every snapshot in the computation reads its dataset from |
|---|---|
| `auto` (default) | the committed `docs/maps/dependency-map.json` when the commit has one, else derived from the commit's tree |
| `committed` | the committed file; a commit without one exits 2: `Commit <sha> has no docs/maps/dependency-map.json; use --dataset auto or derived.` |
| `derived` | the commit's tree, through the shared scanner, even when a committed file exists |

- An unknown value exits 2 with the usage text.
- Output: unchanged (`VersionComputation.ToJson`, schema 1). The output does not record the source, so the two runs in research R8 can be byte-compared.
- A derivation failure exits 2: `Cannot derive the dependency map at <sha>: <scanner message>`. The scanner message names the file and the reason, as the generator's does today.
- The Publisher (`Elsa.Versioning.Publisher`) gets no new option. It always computes with `auto`.

## Generator

| Command | Before | After |
|---|---|---|
| `dependency-map` | writes `docs/maps/dependency-map.json` (committed) | writes the same bytes at the same path (git-ignored) |
| `all` | every layer, the dataset included | unchanged |
| `check` | regenerates the dataset and byte-compares it with the committed file | checks the committed solution filters (same as `solution-filters-check`); exit 0 fresh, 1 stale, 2 error |
| `publish <out> --source-commit <sha> --repo-url <url>` | the markdown set, manifest and findings | the same set plus `docs/maps/dependency-map.json`; relative links to it stay relative |
| `project-graph-self-test` (new) | — | pins that `RepoLayout.IsProjectPath` selects what `git ls-files -- <ProjectPathspecs>` selects, on the live tree and on synthetic nested and Studio paths; exit 0 pass, 1 fail. Working-tree vs git-tree equality is a calculator xunit test, because the git-tree view lives in the calculator |

## CI: `Generated maps fresh` (job id `generated-maps`)

The name is unchanged, and the job stays required in ruleset `main-2`.

| Steps | |
|---|---|
| Removed | "Check the committed dependency map against the tree" (`-- check`) and "Explain how to refresh the dependency map" |
| Added | "Exercise project graph contract" (`-- project-graph-self-test`) |
| Kept | build the generator; publish link rewrite self-test; publish into scratch; branch-writer self-test; project facts and feature facts self-tests; `solution-filters-check`; solution-filter self-tests; EF suite self-test; .NET CLI filter validation |
