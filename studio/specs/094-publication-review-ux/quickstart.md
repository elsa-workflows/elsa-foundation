# Quickstart: Validate Workflow Publication Review UX

## Prerequisites

- Run from the repository root.
- Install workspace dependencies with `pnpm install` when the lockfile/environment is not already prepared.
- Have the existing Workflows API/browser fixture available. Exact-version scenarios additionally require a Foundation fixture that advertises the exact-version relation documented in [the contract](./contracts/workflow-publication-review.md).

## Focused automated checks

Run focused Workflows tests while implementing:

```bash
pnpm --filter @elsa-workflows/studio-workflows test -- publicationReview publicationSlots workflowPublicationOperations
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
pnpm lint:css
```

Run the relevant Playwright browser spec/fixture after adding it or extending the existing one:

```bash
pnpm exec playwright test tests/browser/workflow-publication-review.spec.ts
```

Use the repository's actual focused browser path if publication coverage remains in another existing Workflows spec; do not create a redundant fixture just to satisfy this guide.

## Manual/browser acceptance matrix

### 1. Routine normal-channel publication

1. Open a valid workflow with an existing `default` publication.
2. Select Review & publish.
3. Verify the initial body identifies `default` as the normal Publication channel, says the current publication will be replaced, shows policy-assigned version/readiness, names the `default · version` baseline, and gives a compact change summary.
4. Verify technical policy/ID/concurrency evidence is hidden until Advanced details is opened.
5. Publish once.

Expected: the captured editor state is saved as part of publishing; success replaces the review body and footer visibly offers Close and Open published executable.

### 2. Existing named channel

1. Open the Publication channel selector.
2. Select an occupied named channel such as `canary`.
3. Observe Checking and disabled Publish; wait for authoritative review.

Expected: the final effect says that the named channel's current publication will be replaced, its own version is the comparison baseline, and the request remains protected against stale occupancy.

### 3. Create named channel

1. Choose Create new channel.
2. Enter a valid unused name such as `canary`.
3. Wait for automatic authoritative review.

Expected: the body says a separate Publication channel will be created and “no previous publication” exists for comparison. Publish never changes into a separate review command. Change the input while a request is pending and verify an older result cannot re-enable Publish.

### 4. Blocking evidence and constrained height

1. Use a fixture with local validation failure, server validation failure, and trigger/policy conflict.
2. At a constrained supported viewport height, open Changes and Advanced details and ensure body content becomes long.
3. Tab through channel controls, disclosures, and footer actions.

Expected: blocking reasons are visible without disclosures, header/footer stay visible, only the body scrolls, current focus remains visible, and Publish remains disabled with a useful reason.

### 5. Retained-promotion recovery

1. Use a fixture where promotion succeeds but channel activation fails or preflight becomes stale before activation.
2. Verify the recovery body names the retained promoted version and explains it was not activated.
3. Retry publication.

Expected: retry performs activation only; it does not save or promote again. Footer actions remain visible throughout.

### 6. Exact-version capability compatibility

1. Run the review against a Workflow Design capability document without the exact-version relations.
2. Verify automatic policy-assigned version publishing succeeds and Edit version is absent.
3. Run the review against a document with the relation.
4. Choose Edit version and try a valid forward prerelease, an invalid value, a duplicate, and a non-forward value.

Expected: the relation gates control visibility. The valid requested version passes authoritative review; each invalid case receives specific server-backed feedback and cannot promote. Returning to automatic mode triggers a new authoritative review.

## Visual review

Capture screenshots for normal review, long blocked review, compact success, and retained-promotion recovery in the actual themed Workflows screen. Check that hierarchy leads with channel/effect/version/readiness/baseline, labels use Publication channel, technical evidence is subordinate, and no action button is pushed outside the dialog.

## Recorded implementation evidence

- Focused Vitest: 35/35 passing.
- Playwright publication-review fixture: 4/4 passing, including constrained height, named-channel creation, capability-gated exact version, success, and retained-promotion recovery.
- Workflows typecheck, CSS lint, repository lint, package build, bundle budgets, and repository build: passing.
- The themed constrained-height review was visually inspected with the footer fully visible and the body independently scrollable.
