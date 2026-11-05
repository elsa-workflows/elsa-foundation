# Dashboard qualification guide

**Status:** Draft validation plan. The feature is not implemented. Commands for new feature projects/probes must be added with those projects; none is implied to exist or have passed.

## Prerequisites

- Use this issue's isolated checkout and current committed revision. Read [plan.md](plan.md), [contracts](contracts/query.md) and [UX acceptance](ux-acceptance.md).
- Use owned temporary hosts/content roots, allocated local ports and disposable stores. Preserve existing listeners and databases. Never target a shared or production host.
- Install the repository-pinned SDK/Node tooling and Studio dependencies. Respect the shared-machine `dotnet` build-slot wrapper.
- Obtain the selected provider/access decision before provider-dependent implementation or live calls. Use Connections/Secrets and the applicable credential/setup skill. A local login, environment variable or connection name alone is not authorization.
- Prepare controlled real Workflow runs and an independently packaged Acme.Processing module. Do not substitute a prebuilt widget or invented workflow variable for a declared business dataset.

## Documentation checkpoint

From the repository root, run the existing guards appropriate to this docs-only design:

```sh
git diff --check
python3 tools/maps/check-catalog-links.py
python3 tools/ci/check_evidence_sizes.py
```

Check new spec-relative links and task IDs as well. These checks validate documents, not product behavior.

## Native and provider-disabled journey

The feature-specific acceptance journey must seed known authorized data, create a new question using ordinary controls, preview it, save it, add/arrange its tile, reopen after navigation/restart and refresh. Switch every compatible visualization kind, change grouping, metric/aggregation, sort, time/comparison and parameters, and exercise conflicts/cancel/focus. Disable/remove the provider after saving and verify that native editing, saving and refreshing remain successful with **zero new provider requests**.

Use an independent oracle for values and included/excluded groups. Cover no denominator, observed zero, no records and incomplete/unknown history separately. Keep current-folder/current-outcome semantics visible; do not label retained state as historical event reconstruction.

## Composed Studio proof

Extend the existing actual-host runner with the Dashboard journeys before using it as feature evidence. The committed runner currently supplies the owned-host foundation and does not call external models:

```sh
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
python3 tools/ux-acceptance/run.py --output /tmp/studio-ux-dashboard-UNIQUE
```

Choose a new output directory for each run. Final evidence needs a clean committed checkout. Retain `report.json`, Playwright report/screenshots/accessibility snapshots/traces, source and PR-head identities, all failures and skipped states. Publish evidence through existing downloadable Actions artifacts and link the precise run/artifact from the PR. An independent reviewer opens the captures and inspects the entire affected journey, including light/dark/narrow/keyboard and meaningful empty/error/read-only states.

## Live-model journey

On an owned host with the selected authorized provider, ask the workflow failure-rate question, clarify its meaning, refine current-folder grouping/test exclusion, preview numerical results, save/arrange/reopen/refresh/share/drill. Repeat with processing time by the reference business module's declared customer category.

Record actual provider/model and safe connection metadata, tool invocation correlation, saved native definitions/revisions, numerical oracle and native refresh request-count evidence. Distinguish a real model run from deterministic provider factories. Do not include credentials, private production data or unsafe trace payloads in evidence.

## Authorization and lifecycle matrix

Use users with distinct field/record/aggregate scopes, an editor and a second tenant. Independently verify each viewer's numbers, definition rights, copies/reuse/history, ACL revoke, identity-provider permission propagation and current-access drill/export. Change/remove schema/metric/module; exercise provider outage, cancellation, failed refresh and cost bounds. Reopen current Home and verify its saved layout, defaults, widgets and hidden/missing contributions survive.

## Completion record

Map all SC-001–SC-007 outcomes and UX-01–UX-11 to exact-source evidence. Run whole affected suites and required architecture/map/graph gates, complete independent reviews on the current head, satisfy required CI, merge under current rules and verify resulting main. Keep the feature open for any missing provider, UX, authorization or resulting-main evidence. Do not run a release/deployment as part of qualification.
