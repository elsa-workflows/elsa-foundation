# Qualification guide: workflow and activity testing

**Status:** Planned journeys/commands. The testing API/CLI does not exist on the baseline; these examples are not executed evidence.

## Prerequisites

Use this feature worktree, owned disposable Workbench/Studio hosts/stores, committed source, locked Studio dependencies and Playwright Chromium. Use the development seed and owned test-system mappings only. Follow [backend e2e guidance](../../e2e-tests/README.md) and [Studio acceptance](../../docs/contributing/studio-ux-acceptance.md). Never attach to/restart an unowned host.

## Selected activity

Select a nested calculation beside unrelated predecessor/successor controls. Change an unsaved configured value and open Tests. Supply explicit context; prove missing setup refuses execution, then inspect output with No verdict. Verify zero unrelated invocations. Choose assertions, save, reload/reopen/edit/rerun. Repeat for native composite and reusable call-site with nondefault inputs. Remove/move a target in a new candidate; require deliberate rebind/new revision and preserve old history.

## Workflow and dependencies

Save an order case with approval action/event, expected wait, controlled time and payment non-invocation assertion. Prove rejection/settlement without payment. Introduce wrong output/forbidden invocation: completed execution must fail the saved assertion. Expected fault may pass. Compare controlled HTTP substitution with an explicit real owned endpoint integration; prove actual request ledger and zero live calls for controlled/missing-boundary cases.

## Library and headless parity

Save/reload a mixed suite and export exact revisions/candidate. Import into a fresh authorized library with explicit mappings. Execute the packaged runner against the same owned backend, using the planned command shape:

```text
dotnet-elsa test run --server <owned-backend> --bundle <export.json> --candidate <candidate.json> --environment <test-mappings.json> --json <results.json> --junit <results.xml>
```

Finalize/document syntax and authentication references during implementation; no credentials in portable files. Compare exact manifests/verdicts with Studio. Restart the disposable authoring host and verify durable history. Prove exits 0/1/2/130 and detailed member reports, including intentionally skipped/unavailable/incomplete/all-skipped/unasserted controls.

## Isolation and lifetime

Start two same-name test waits and an ordinary execution on the disposable host. Address one exact wait, retry ambiguous delivery idempotently and prove no cross-scope delivery. Advance one scoped clock and preserve other timers/wall expiry. Cancel active/waiting work, inspect intent versus settlement, closed admission/child cleanup and no false pass. Interrupt/expire a runner. Fail fixture preparation halfway and clean only its resources; concurrent attempts must retain separate fixture/substitute state.

## Current-commit evidence

Run touched whole suites, relevant REST e2e and an external packaged CLI consumer. Extend/run the owned composed acceptance inventory for every [UX journey/state](ux-acceptance.md). Retain source/head identities, screenshots, accessibility snapshots, traces and JSON/JUnit outputs as downloadable artifacts. Independent review opens the evidence and inspects complete changed experience. Re-run affected suites/journeys after fixes. Record required exact-head checks, review/merge and resulting-main qualification before closing either feature.
