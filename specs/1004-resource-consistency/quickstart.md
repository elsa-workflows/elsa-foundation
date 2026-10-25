# Verification Guide: Studio resource-list consistency

This guide defines the implementation and exact-head verification recipe. Actual results, failed attempts, revision bindings and pending gates are recorded on #3071 and PR #3313 rather than cached in this recipe. Root owns implementation integration and final qualification.

## Prerequisites

- Work only in the selected feature branch and confirm the candidate commit is the current implementation head.
- Refresh the shared gate matrix and CI requirements before running the full gates.
- Use the repository's existing package scripts and build-slot wrapper. Do not start overlapping builds.
- Use a fresh disposable private Workbench/Studio host for normal-host proof. Use synthetic values only, bind every test Connection to loopback, and record no request body, header, credential value, or secret value.

## Focused behavior checks

Run the existing focused Vitest files from the repository's Studio package context:

```text
Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretsPage.test.tsx
Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionsPage.test.tsx
Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/homeTaskGroup.test.tsx
```

Add and exercise causal negative controls for:

- Secrets' primary Edit and secondary operations, 100/101 coverage and search, absent loaded scope, and accepted/draft/failed query semantics.
- Connection creation under an accepted non-empty query, the exact notice, same-query reload, and no selection when the created name is absent from returned items.
- Home's authenticated runtime-read allowed/denied behavior, auth-disabled behavior, matcher semantics, exact destination, and unchanged Continue catalog.
- Every attempted fixture POST, PUT, and DELETE being counted, including a thrown/rejected handler, without reading payload data.

Run the existing browser specs:

```text
Studio/tests/browser/secret-scopes.spec.ts
Studio/tests/browser/connections-management.spec.ts
Studio/tests/browser/home.spec.ts
```

The focused browser work should cover keyboard operation access, confirmations and cancellation, focus return, error states, and existing page filters. Full Studio Chromium remains a separate gate.

## Static checks and builds

On the exact candidate head, refresh and run the applicable checks from the current gate matrix:

- Studio `pnpm typecheck`, including `tests/live`.
- Affected module lint, style, and layout checks.
- Affected Secrets and Connections client builds.
- Five existing build groups: Dashboard client, Workflows client, Studio app client, Studio host, and Workbench host.
- The three required full Workflows package shuffle seeds: 1, 11, and 29.
- Applicable architecture guards, solution-filter check, generated-map freshness, and map publish contract checks.
- Full Chromium browser suite.

Run the separate Workflows `build:lib` only if an implementation change touches that package's published library surface; its declaration emission is not covered by ordinary typecheck or package build.

Preserve each failed, incomplete, skipped, unavailable, or stale-head result with its actual status. Do not treat previous #3069/#3310 receipts as current feature evidence.

## Safe private-host scenarios

On a fresh disposable normal host, seed 101 Secrets with synthetic values; keep values out of all artifacts. Verify the first-page limit, displayed/loaded/total counts, server search for an item beyond the first loaded page, local loaded-scope wording, accepted-query refresh, and retry behavior. Exercise a safe successful encrypted-store operation and a deterministic missing-configuration failure; verify rotate, revoke, and delete, including confirmation and cancellation. Confirm strict-write behavior, value masking, authenticated allow/deny including 403, and cleanup.

Register only Connection descriptors that return from the isolated Workbench. Use synthetic API-key/Bearer/Basic material only in that private host, constrain any endpoint to loopback (for example `127.0.0.1`), and make no external calls. Verify create under a non-empty accepted search, returned-item selection rules, Details, local readiness Test, Revoke/Delete confirmation and cancellation, query retention, and focus return. Do not describe the local HTTP readiness check as an outbound service test or Revoke as remote credential revocation.

## Visual and accessibility review

Capture comparable before/after states at 1600×1000 @2x in Classic Light and a dark theme. Also inspect Dim, High Contrast, 1280 px light, 1440 px dark, and 390 px narrow layouts. Review loaded, empty, loading, failed, and action-error states; keyboard reachability; accessible names; destructive-action separation; and focus restoration. Screenshots must contain no secret values.

## Independent and delivery gates

Obtain three independent review axes against the complete draft PR. Once the implementation is ready, request the actual Full tier on the exact head, secure exact-head application approval, and deliver only under root ownership. After merge, check resulting-main CI, Maps, and Studio. Keep the review and gate evidence distinct; a skipped or unavailable check is not a pass.
