# `@elsa-workflows/example-react-host`

A minimal **Vite + React 19 + TypeScript** app that consumes all four published Studio
packages outside `Elsa.Studio.Web`. It proves the packages work in an ordinary host app.

## What it shows

| Section | Package | Demonstrates |
| --- | --- | --- |
| 1. Code editor | `@elsa-workflows/studio-code-editor` | `StudioCodeEditor` editing a JS snippet with `javaScriptLanguageAdapter` (lazy CodeMirror + fallback textarea + diagnostics). |
| 2. UI primitives | `@elsa-workflows/studio-ui` | `Toolbar`, `Tabs`, `StatusChip`, `DataGrid` styled by the `--studio-*` design tokens. |
| 3. Workflows module | `@elsa-workflows/studio-workflows` + `@elsa-workflows/studio-sdk` | Building an `ElsaStudioModuleApi` from SDK factories, calling `register(api)`, and rendering a registered route. |

## Run it (in this monorepo)

```bash
pnpm --filter @elsa-workflows/example-react-host dev
```

The workspace deps resolve to the packages' raw TypeScript source, so Vite compiles them
from source — that is intended for the example. `pnpm --filter … build` and `… test` also
work (`test` passes with no tests).

## Install in your own app (external consumer)

```bash
npm install @elsa-workflows/studio-ui @elsa-workflows/studio-code-editor \
            @elsa-workflows/studio-sdk @elsa-workflows/studio-workflows
```

### Peer dependencies

| Peer | Required by | Version |
| --- | --- | --- |
| `react`, `react-dom` | all four packages | `^18` or `^19` |
| `@tanstack/react-query` | `@elsa-workflows/studio-workflows` | `^5` |
| `@xyflow/react` | `@elsa-workflows/studio-workflows` (workflow canvas) | `^12` |

### CSS imports

```ts
// 1. Provide your own LAYER-1 primitive tokens first (--background, --primary, ...).
//    tokens.css aliases the semantic --studio-* layer ONTO these. See src/theme.css.
import "./theme.css";

// 2. The semantic --studio-* token layer.
import "@elsa-workflows/studio-ui/tokens.css";

// 3. The workflow module's styles (only if you use @elsa-workflows/studio-workflows).
import "@xyflow/react/dist/style.css";
import "@elsa-workflows/studio-workflows/style.css";
```

> **In-repo vs published:** inside this monorepo the published CSS *subpaths* above do not
> resolve (the packages' on-disk `exports` map only `"."`), so `src/main.tsx` imports the same
> source files by workspace-relative path with comments showing the external form. In your own
> app you use the subpath imports exactly as written above.

> **Component visual rules:** `@elsa-workflows/studio-ui` ships `tokens.css` (the token layer)
> but **not** the per-component visual rules — in Elsa.Studio.Web those live in the host's own
> stylesheet. This example supplies them in `src/studio-components.css` (a faithful copy keyed
> off the `--studio-*` tokens) so the primitives look styled. Adapt them to your own design.

## The stub module API

`@elsa-workflows/studio-workflows`'s `register(api)` touches only:

- `api.dialogs` — passed to the module's internal dialog wiring.
- `api.featureAreas.add(...)` — registers one "Workflows" feature area with nav + routes.

Registered route components then read `api.backend` / `api.ai` / editor registries at render
time. `src/stubModuleApi.ts` builds a **real** `ElsaStudioModuleApi` from the SDK's exported
factories (`createContributionRegistry`, `createAiContributionApi`, `createDialogController`,
`createEndpointContext`) — nothing is faked. It mirrors what Elsa.Studio.Web's own host does
in `Client/src/app/registry.ts` (that host-assembly factory is intentionally not part of the
published SDK surface).

The rendered piece is the **"Workflow runs"** route (`/workflows/instances`), chosen because
it has the fewest runtime requirements. It fetches from the backend at `http://localhost:5000`;
with no backend running it renders its own error/empty state — the module still mounts.
