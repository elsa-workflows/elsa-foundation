# Validation guide: HTTP progressive disclosure

Run from the repository root unless stated. Record exact commit, commands and outcomes on the PR; this guide is not evidence that any command ran.

## Focused regression, then whole affected suites

From `Studio/`, run the existing grouping suite plus added HTTP disclosure coverage before production changes, retain the expected baseline failure, then rerun after the patch:

```sh
pnpm --filter @elsa-workflows/studio-workflows test -- src/__tests__/activityPropertyGroups.test.tsx
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
pnpm --filter @elsa-workflows/studio-web test
pnpm lint
```

Use the actual test runner script semantics on the selected base; do not assume an extra argument filters the underlying workspace command without checking its output. Run the affected extension typechecks after declaration-copy edits. Run the real-panel browser sibling spec for hierarchy, keyboard, configured/error and method/value preservation, with full-page axe and Light/Dark screenshots. Keep source-composed inspector proof separate from fixture proof.

From root, run whole affected backend projects serially:

```sh
dotnet test Essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release
dotnet test Essentials/tests/Activities/Design/Api/Tests/Elsa.Activities.Design.Api.Tests.csproj -c Release
```

Retain scanner baseline failure for Connection tier and prove requiredness remains false. Keep existing normalization/catalog coverage green.

## Source-composed journey

Rebuild the owned Studio and Workbench from this source using the repository contributor quickstarts, with a fresh disposable DB. Use only owned ports/processes and preserve other sessions. In actual Studio, add Send HTTP Request, verify URL/method/connection/body prominence, configure content/type and optional values, change methods including GET/HEAD, and verify exact preservation. Exercise configured/bound/default/error scenarios, keyboard and narrow inspector layouts. Record provenance and before/after Light/Dark screenshots.

Run `e2e-tests/http/Test-SendHttpRequestStatusOutcomes.ps1` against that rebuilt server with `-BaseUrl`; follow `e2e-tests/README.md` and lifecycle ownership. This proves runtime status routing, not UI or Connection selection.

## Repository gate

Restore the evaluated architecture project graphs using the repository gate procedure, run the entire Release architecture suite without a filter and reconcile discovered/executed counts, then run maps check and diff check. Refresh genuinely changed maps as part of this authorized work and review generated findings; do not merely restamp outputs. Heavy builds/tests stay serial and honor the shared build-slot wrapper.

Final root review includes the complete diff, repeated code/test setup, actual visible hierarchy, all issue/PR comments and review threads. Record exact-head local/CI evidence and independent reviews before merge; resulting-main evidence remains a separate post-merge gate.
