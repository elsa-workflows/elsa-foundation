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

The examples use the same public CSS subpaths as published consumers: `studio-ui/styles.css`,
`studio-code-editor/style.css`, and (for the React workflow host) `studio-workflows/style.css`.
Shared component styles are shipped by the package; there is no copied host stylesheet.
Import your theme overrides after the public UI styles. See [module UI guidance](../docs/module-ui.md).

## Running

```bash
pnpm --filter @elsa-workflows/example-react-host dev
pnpm --filter @elsa-workflows/example-vanilla-host dev
```

`pnpm -r build` and `pnpm -r test` include both apps (their `test` scripts pass with no tests).
