# Examples

Standalone host apps that consume the published Studio npm packages **outside**
`Elsa.Studio.Web`. They exist to prove two things: (a) the packages work as ordinary npm
dependencies, and (b) the host's framework and language do not matter.

| Example | Stack | Shows |
| --- | --- | --- |
| [`react-host`](./react-host) | Vite + React 19 + TS | All four packages: the code editor, the UI primitives (with `tokens.css`), and the workflows module registered against a stub `ElsaStudioModuleApi` built from SDK factories. |
| [`vanilla-host`](./vanilla-host) | Vite vanilla-ts (no JSX) | A framework-free page that mounts Studio widgets with `createRoot`/`createElement`. React is an implementation detail of the widgets; the build output is static JS/CSS servable by any backend. |

## Workspace vs published resolution

Both apps are pnpm workspace members and depend on the Studio packages via `workspace:*`.
**In-repo that resolves to each package's raw TypeScript source**, so the examples' Vite builds
compile the Studio packages from source — intended, and why no separate package build is needed
to run them.

An **external** consumer instead installs the real npm packages:

```bash
npm install @elsa-workflows/studio-ui @elsa-workflows/studio-code-editor \
            @elsa-workflows/studio-sdk @elsa-workflows/studio-workflows
```

Two things differ between in-repo and published resolution — each app's README documents them
and marks the external form in code comments:

1. **CSS subpaths.** Published packages expose `@elsa-workflows/studio-ui/tokens.css` and
   `@elsa-workflows/studio-workflows/style.css`. In-repo those subpaths do not resolve (on-disk
   `exports` map only `"."`), so the examples import the same source files by relative path.
2. **Component visual rules.** `@elsa-workflows/studio-ui` ships the `--studio-*` token layer
   (`tokens.css`) but not the per-component visual rules (those are host chrome in
   Elsa.Studio.Web). Each example supplies its own `studio-components.css` — a faithful copy
   keyed off the `--studio-*` tokens — so the primitives look styled.

## Running

```bash
pnpm --filter @elsa-workflows/example-react-host dev
pnpm --filter @elsa-workflows/example-vanilla-host dev
```

`pnpm -r build` and `pnpm -r test` include both apps (their `test` scripts pass with no tests).
