import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import dts from "vite-plugin-dts";

/**
 * Vite config for the PUBLISHED npm library artifact of
 * `@elsa-workflows/studio-code-editor`.
 *
 * This produces `dist/` (ESM `index.js` + a single flattened `index.d.ts`) that is
 * shipped to npm. The committed `package.json` deliberately keeps `exports`/`types`
 * pointing at `src/index.ts` so workspace consumers typecheck against source; the
 * `publishConfig` block carries the `dist/` paths that apply at publish time. Note:
 * vite-plugin-dts derives its `rollupTypes` entry from `publishConfig.types` (falling
 * back to the top-level `types`, which points at src and would break the rollup), so
 * `publishConfig.types` is also load-bearing for this build — do not remove it.
 * Nothing here changes the in-repo dev flow — `build` (`tsc --noEmit`) and `test`
 * are untouched.
 *
 * `@codemirror/*` and `@uiw/*` are real runtime dependencies installed by consumers,
 * and `react` / `react/jsx-runtime` are peer-provided, so all are externalized rather
 * than bundled.
 */
export default defineConfig({
  plugins: [
    react(),
    dts({
      entryRoot: "src",
      insertTypesEntry: true,
      rollupTypes: true,
      tsconfigPath: resolve(__dirname, "tsconfig.json"),
      exclude: ["src/**/__tests__/**", "src/**/*.test.*"]
    })
  ],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true,
    lib: {
      entry: resolve(__dirname, "src/index.ts"),
      formats: ["es"],
      fileName: () => "index.js"
    },
    rollupOptions: {
      external: ["react", "react/jsx-runtime", /^@codemirror\//, /^@uiw\//]
    }
  }
});
