# Quickstart: Validate Expression Code Intelligence

## Prerequisites

- Node.js and pnpm versions supported by the repository
- .NET 10 SDK
- Playwright Chromium, Firefox, and WebKit installed
- For authoritative semantic scenarios, an Elsa Foundation backend containing work unit `143-expression-code-intelligence`

## Install and build

```bash
pnpm install
pnpm typecheck
pnpm build
dotnet build Elsa.Studio.slnx
```

## Focused automated checks

```bash
pnpm --filter @elsa-workflows/studio-code-editor test
pnpm --filter @elsa-workflows/studio-expression-editors-javascript test
pnpm --filter @elsa-workflows/studio-expression-editors-liquid test
pnpm --filter @elsa-workflows/studio-workflows test
pnpm test:browser:expression-matrix
pnpm test:browser:performance
dotnet test tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj
```

## Repository gates

```bash
pnpm lint
pnpm typecheck
pnpm test
pnpm build
dotnet test Elsa.Studio.slnx
```

## Manual acceptance

On the real workflow activity-properties inspector:

1. Select JavaScript, enter a single-line expression, invoke completion automatically and with Ctrl/Cmd+Space, then press Enter with and without an active completion.
2. Expand and collapse the property; verify source, selection, diagnostics, and undo remain continuous.
3. Paste multiline source into compact editing; verify exact text is preserved and the expanded editor opens.
4. Select Liquid and verify variables, nested members, filters, and tags follow Liquid syntax.
5. Verify Tab indents in both profiles and Escape then Tab or Tab-focus mode leaves either editor.
6. Introduce local syntax and backend semantic errors. Verify draft save remains possible, Test Run/publication gating follows the specification, and stale results never replace current diagnostics.
7. Disable tooling capability links and verify syntax-aware/generic editing remains usable with an accurate unavailable message.
8. Exercise unauthorized and incompatible fixtures; verify protected source/catalog data is absent, not merely hidden.
9. Run the named keyboard/screen-reader flows and capture the browser/AT combinations and results.

## Performance evidence

Run the Playwright benchmark fixture in current stable Chromium with 4× CPU throttling:

- 50 visible properties: 10 rich-capable, 40 ordinary;
- expression lengths up to 2,000 characters;
- 30 cold and warm activations;
- 100 representative keystrokes.

Record runner class, browser version, fixture revision, p95 activation, and p95 longest typing task. Warm means the editor engine and catalog are already memory-cached; cold means a fresh page with neither cache.

## Expected outcome

All package, repository, browser, accessibility, and backend contract checks pass. JavaScript and Liquid expose only their advertised capabilities, compact and expanded results match for each advertised capability, exact source is never lost, and every unavailable/unauthorized/incompatible state is distinguishable.
