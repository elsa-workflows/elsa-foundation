# `@elsa-workflows/example-vanilla-host`

A **framework-free** Vite host page (vanilla-ts, no JSX) that mounts Studio widgets. It makes
one point: **React is an implementation detail of the widgets, not a requirement of the host.**

## What it shows

- `index.html` is plain HTML with a few mount-point `<div>`s and no React knowledge.
- `src/main.ts` imports the widgets from `@elsa-workflows/studio-code-editor` and
  `@elsa-workflows/studio-ui` and mounts them with `createRoot` + `createElement`. **No JSX
  anywhere.**
- React / React Dom are ordinary `dependencies` here — bundled into the app output like any
  library. `@vitejs/plugin-react` is only present to compile the packages' own JSX source.

Because the output of `vite build` is static JS/CSS, it can be served by **any** backend —
ASP.NET, Node, nginx, an S3 bucket, a CDN. The host framework and language do not matter.

## Run it (in this monorepo)

```bash
pnpm --filter @elsa-workflows/example-vanilla-host dev
# or produce static output:
pnpm --filter @elsa-workflows/example-vanilla-host build
```

## Install in your own app (external consumer)

```bash
npm install @elsa-workflows/studio-ui @elsa-workflows/studio-code-editor react react-dom
```

```ts
import { createElement } from "react";
import { createRoot } from "react-dom/client";
import { StudioCodeEditor, javaScriptLanguageAdapter } from "@elsa-workflows/studio-code-editor";
import { StatusChip, StudioToolbar } from "@elsa-workflows/studio-ui";

import "./theme.css";                          // your LAYER-1 primitive tokens
import "@elsa-workflows/studio-ui/tokens.css"; // the --studio-* token layer
```

> **In-repo vs published:** inside this monorepo the `tokens.css` subpath does not resolve (the
> package's on-disk `exports` map only `"."`), so `src/main.ts` imports the source file by
> workspace-relative path with a comment showing the external form. It also supplies the
> component visual rules in `src/studio-components.css`, which `@elsa-workflows/studio-ui`
> does not ship (see the react-host README for the full explanation).

## Peer dependencies

| Peer | Version |
| --- | --- |
| `react`, `react-dom` | `^18` or `^19` |
