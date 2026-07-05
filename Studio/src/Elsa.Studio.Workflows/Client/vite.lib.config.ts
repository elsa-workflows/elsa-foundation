import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import dts from "vite-plugin-dts";

/**
 * Library build for publishing `@elsa-workflows/studio-workflows` to npm.
 *
 * This is separate from the module build (`vite.config.ts` -> `../wwwroot/studio/modules/workflows`,
 * driven by the root `vite.module.base.ts`), which stays the in-repo runtime artifact. This config
 * emits an npm-consumable `dist/` for external hosts that compose the workflows module themselves.
 *
 * Entry is the real public module entry `src/module.tsx` (exports `register`, the connect-end helpers,
 * and `WorkflowConnectSource`); no new barrel is invented.
 *
 * CSS: `cssCodeSplit: false` merges the module's own `styles.css` + `activityAvailability.css` into a
 * single asset, pinned to `dist/style.css` via `assetFileNames` so the published manifest's
 * `"./style.css"` subpath is deterministic across builds. `@xyflow/react/dist/style.css` is deliberately
 * kept as an external import (see externals below) so the consumer's bundler pulls it from the shared
 * `@xyflow/react` copy rather than us inlining a second, drift-prone copy of the flow chrome CSS.
 *
 * Externals: every shared/runtime-provided dependency, so the consumer supplies a single copy. This
 * mirrors the module build's externals plus react-dom (used by the test entry / host root) and the
 * cross-package workspace SDKs, which must stay `import type`-level external references in the rolled-up
 * d.ts rather than inlined copies (vite-plugin-dts keeps externals external in the type rollup).
 *
 * NOTE: package.json's `publishConfig.types` (`./dist/index.d.ts`) is load-bearing for this build:
 * vite-plugin-dts derives its api-extractor rollup entry from `publishConfig` first, then top-level
 * `types`. The top-level src-pointing `types` would mangle to a nonexistent `src/*.d.ts` entry, so the
 * dist-pointing publishConfig keeps the type rollup working (TS and pnpm workspace linking ignore
 * publishConfig; the CI prepare script replaces it at publish).
 */
export default defineConfig({
  plugins: [
    react(),
    dts({
      rollupTypes: true,
      insertTypesEntry: true,
      entryRoot: "src",
      exclude: ["**/__tests__/**", "**/*.test.*"]
    })
  ],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true,
    cssCodeSplit: false,
    lib: {
      entry: resolve(__dirname, "src/module.tsx"),
      formats: ["es"],
      fileName: () => "index.js"
    },
    rollupOptions: {
      external: [
        "react",
        "react/jsx-runtime",
        "react-dom",
        "react-dom/client",
        "@elsa-workflows/studio-sdk",
        "@elsa-workflows/studio-ui",
        "@elsa-workflows/studio-code-editor",
        "@xyflow/react",
        "@xyflow/react/dist/style.css",
        "@tanstack/react-query",
        "lucide-react"
      ],
      output: {
        assetFileNames: "style.css"
      }
    }
  }
});
