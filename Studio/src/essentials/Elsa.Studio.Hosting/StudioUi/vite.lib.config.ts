import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import dts from "vite-plugin-dts";

/**
 * Library build for the published `@elsa-workflows/studio-ui` package.
 *
 * SHELL PACKAGE / SIBLING SOURCE — this package is a thin shell. Its `src/index.ts` is a
 * single `export * from "../../Client/src/app/ui/shared"`: the real primitives live in the
 * sibling private app package (`../Client/src/app/ui`). See `src/index.ts` for why (the
 * shared UI barrel is the single source of truth; module clients import from here for
 * types at build time, then mark this package external so the runtime copy resolves
 * through the host import map).
 *
 * In-repo consumers resolve `./src/index.ts` directly (package.json keeps `private: true`
 * and points `exports`/`types` at src) and never run this config — the normal dev flow is
 * unchanged. This config only produces the publishable `dist/` (a CI-only step flips the
 * manifest at publish time). The tokens step (`dist/tokens.css`) is run separately by the
 * `build:lib` script via `scripts/build-tokens.mjs`.
 *
 * WHY `rollupTypes: true` IS REQUIRED — the type sources span TWO directories: this
 * package's `src/` and the sibling `../Client/src/app/ui`. Without rollup, vite-plugin-dts
 * emits a mirrored tree whose entry would re-export from `../Client/...`, i.e. paths that
 * climb OUT of `dist/` and point at source that is not shipped. `rollupTypes: true` runs
 * the API Extractor pass that FLATTENS every referenced declaration into one
 * `dist/index.d.ts`, so the published types are self-contained. `entryRoot` is set to the
 * common ancestor of both source trees (`Elsa.Studio.Web`) so the intermediate emit is
 * valid before it is rolled up.
 *
 * DO NOT REMOVE `publishConfig.types` FROM package.json — it is load-bearing here. With
 * `rollupTypes: true`, vite-plugin-dts derives the API Extractor entry point from
 * package.json via `findTypesPath(pkg.publishConfig, pkg)`: publishConfig FIRST, then
 * top-level `types`. Top-level `types` must stay src-pointing (`./src/index.ts`, for
 * in-repo consumers), which the plugin would mangle into the nonexistent `src/index.d.ts`
 * and abort. `publishConfig.types: "./dist/index.d.ts"` steers the rollup to the correct
 * entry; TypeScript and pnpm workspace linking ignore publishConfig entirely, and the
 * CI-only prepare script rewrites the manifest at publish time. The plugin's
 * `rollupConfig` option cannot override this (mainEntryPointFilePath is spread after it).
 */
export default defineConfig({
  plugins: [
    react(),
    dts({
      // Flatten the two-directory type surface into a single self-contained declaration.
      rollupTypes: true,
      insertTypesEntry: true,
      // Common ancestor of StudioUi/src and ../Client/src/app/ui.
      entryRoot: resolve(__dirname, ".."),
      tsconfigPath: resolve(__dirname, "tsconfig.json"),
      // The package tsconfig's `include: ["src"]` scopes the normal `tsc --noEmit` build
      // to this package. The dts plugin must additionally see the sibling primitive source
      // it re-exports, so widen coverage here (without touching tsconfig semantics).
      include: [
        resolve(__dirname, "src/**/*.ts"),
        resolve(__dirname, "../Client/src/app/ui/**/*.ts"),
        resolve(__dirname, "../Client/src/app/ui/**/*.tsx")
      ],
      // Host-only `DialogHost` (under `dialog/`) is excluded from the public surface, as
      // are tests.
      exclude: [
        "**/__tests__/**",
        "**/*.test.*",
        "**/dialog/**"
      ]
    })
  ],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true,
    lib: {
      // Rollup follows the relative import in src/index.ts into ../Client/src/app/ui and
      // bundles those primitives into dist/index.js — that is intended.
      entry: resolve(__dirname, "src/index.ts"),
      formats: ["es"],
      fileName: () => "index.js"
    },
    rollupOptions: {
      external: ["react", "react/jsx-runtime"]
    }
  }
});
