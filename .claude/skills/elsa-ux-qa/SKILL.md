---
name: "elsa-ux-qa"
description: "Run one pass of the recurring UX QA routine: build and start the Workbench and Studio from main, walk the golden path, explore one rotating area through one user lens in a real browser, have each finding refuted by a fresh agent, then file at most three defects and two proposals where the fix lives and append the run to the ux-qa-ledger issue. Use when the UX QA routine fires or a user asks for one QA pass."
argument-hint: "Optional area and lens override"
compatibility: "Requires elsa-foundation and elsa-foundation-studio checkouts, gh CLI, the .NET SDK, Node 22+ and network access for the Chromium download"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#ux-qa-routine"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#ux-qa-routine` and `tools/ux-qa/lib.mjs`.
2. Count open `auto-qa` + `needs-triage` issues in both repositories (no proposals at 10 or more) and each repository's `ready-for-agent` issues (at the cap of 20, defects are filed `needs-triage`).
3. Run `bash tools/ux-qa/setup.sh`; if a stage fails, report it and stop.
4. Run `node tools/ux-qa/golden-path.mjs`; a failing step is the run's first defect.
5. Derive the area and lens from the catalog's rotation (counted from Tuesday 2026-10-13; area `k mod 8`, lens `k mod 5`) unless the user input names them. Pick a persona and a goal, then explore the common path and at least one uncommon scenario with journey scripts under `/tmp`, looking at every screenshot.
6. Sort candidates into defects and proposals. Have a fresh subagent rerun and try to refute each one, and drop those that do not survive.
7. Check for duplicates in both repositories. File at most three defects and two proposals where the fix lives, using the catalog's labels and body; a defect gets `ready-for-agent` only when it passes every condition in the catalog's gate.
8. Comment the run on the `ux-qa-ledger` issue, then run `bash tools/ux-qa/teardown.sh`.

Edit no repository files, push no branches and open no pull requests. Report the area, lens, golden-path result, what worked well, issues filed, and candidates dropped.
