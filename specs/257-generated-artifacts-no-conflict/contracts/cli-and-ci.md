# Contracts: generator CLI, change-scope classifier, CI checks

## Elsa.Maps.Generator

| Command | Before | After |
|---|---|---|
| `all` (default), `<layer>...` | writes all layers to repo paths | unchanged; the documentation outputs are now git-ignored |
| `check` | regenerates six layers and byte-compares 13 files | regenerates `dependency-map` only and byte-compares `docs/maps/dependency-map.json`; exit 1 lists the file and prints `-- dependency-map` as the fix |
| `publish <out-dir> --source-commit <sha> --repo-url <url>` | — | **new.** Generates every layer into `<out-dir>` (working tree untouched), rewrites out-of-set relative links to `<url>/blob/<sha>/…`, and writes `<out-dir>/README.md`. Exit 0 on success; exit 2 on bad arguments |
| `publish-self-test` | — | **new.** Exercises the link rewrite (in-set kept, out-of-set pinned, anchors and absolute URLs untouched) and that the working tree is untouched. Runs in `Generated maps fresh` |
| `solution-filters-check`, the `*-self-test` commands, `ef-suites-*`, `solution-filter-roots` | — | unchanged |

`architecture-reference` reads the domain and extension-point summaries from wherever the current run writes them, whether the repo paths or a redirect. It no longer reads the repo paths unconditionally.

## tools/ci/change_scope.py

```text
change_scope.py --base <sha> --head <sha> [--github-output <file>] [--summary <file>]
change_scope.py --self-test
```

- **Output keys:** `docs_only=true|false`.
- **Exit codes:** always exit 0 in classify mode. A failure to classify yields `docs_only=false`, so the script never blocks CI by itself.
- **Self-test:** exits 1 when any built-in case misclassifies.

## ci.yml check contexts

| Context | Required | PR, docs-only | PR, other | push / schedule / dispatch |
|---|---|---|---|---|
| `Build & test` | yes | classifier, Python guards, catalog link check → success | full, as today | full, as today |
| `Generated maps fresh` | yes | full (generator build, `check` for the dependency map, slnf checks, self-tests) | same | same |
| `Architecture guards`, `Core-only build & test` | no | skipped | run | run |
| `Publish generated maps` | no | not run | not run | push only |

## generated-maps branch

- **Ref:** `refs/heads/generated-maps`.
- **Writers:** `ci.yml` job `publish-maps` only, by convention, as for `publish-state`.
- **Ruleset:** `generated-maps`, with `deletion` and `non_fast_forward`.
- **Stable URLs, for `AGENTS.md` and `docs/maps/README.md`:**
  - Browse: `https://github.com/elsa-workflows/elsa-foundation/tree/generated-maps/docs/maps`
  - Raw: `https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/generated-maps/docs/maps/<file>`
  - Clone: `git fetch origin generated-maps && git show origin/generated-maps:docs/maps/<file>`
- **Build artifact:** `generated-maps-<sha>`, with the same tree.
