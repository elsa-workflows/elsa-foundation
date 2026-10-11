import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import dts from "vite-plugin-dts";

/**
 * Library build for publishing `@elsa-workflows/studio-sdk` to npm.
 *
 * The in-repo runtime path is unaffected by this: modules still externalize the SDK to the host import
 * map (`vite.module.base.ts`). This config exists only so external module authors can `npm install`
 * the SDK and compile/bundle against it outside the monorepo.
 *
 * The SDK source imports nothing external except `react` (a handful of type-only `ComponentType`/
 * `ReactNode` imports plus the React hooks/`createContext` used by the auth provider + guards), and
 * `react/jsx-runtime` (implicit under the `react-jsx` transform for the auth `.tsx` files). Both are
 * externalized as peers so the consumer supplies its own single React copy. There are no other runtime
 * dependencies to bundle or declare.
 *
 * `entryRoot` is the `Elsa.Studio.Web` directory — the common ancestor spanning both `StudioSdk/src`
 * (this package's entry) and `Client/src/sdk` + `Client/src/auth` (the re-exported source) — so
 * `rollupTypes` can walk the type graph across both trees and emit a single flattened `index.d.ts`.
 *
 * NOTE: package.json's `publishConfig.types` (`./dist/index.d.ts`) is load-bearing for this build:
 * vite-plugin-dts derives its api-extractor rollup entry from `publishConfig` first, then top-level
 * `types`. The top-level src-pointing `types` would mangle to a nonexistent `src/index.d.ts`, so the
 * dist-pointing publishConfig keeps the type rollup working (TS and pnpm workspace linking ignore
 * publishConfig; the CI prepare script replaces it at publish).
 */
export default defineConfig({
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
      external: ["react", "react/jsx-runtime"]
    }
  },
  plugins: [
    react(),
    dts({
      rollupTypes: true,
      insertTypesEntry: true,
      entryRoot: resolve(__dirname, ".."),
      include: [
        "src/**/*.ts",
        "../Client/src/sdk/**/*.ts",
        "../Client/src/auth/**/*.ts",
        "../Client/src/auth/**/*.tsx"
      ],
      exclude: ["**/__tests__/**", "**/*.test.*"]
    })
  ]
});
