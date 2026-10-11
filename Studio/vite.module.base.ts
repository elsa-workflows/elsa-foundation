import { resolve } from "node:path";
import { defineConfig, type UserConfig } from "vite";
import react from "@vitejs/plugin-react";
import { moduleCssContractPlugin } from "./vite.module.css-contract";

/**
 * Shared vite config for Studio module clients.
 *
 * Every module is built as a single ESM `module.js` that is loaded at runtime by the
 * host and marks the host-provided dependencies `external` so they resolve through the
 * import map (`Elsa.Studio.Web/Client/index.html`) to one shared copy. This removes the
 * ~9x copy-paste between the per-module `vite.config.ts` files.
 *
 * @param root           The module client directory (pass `__dirname`).
 * @param outDir         Build output dir, relative to `root` (e.g. `../wwwroot/studio/modules/x`).
 * @param extraExternals Additional bare specifiers to externalize on top of the shared
 *                       set (e.g. `@tanstack/react-query` for modules that use it).
 */
export function defineModuleConfig({
  root,
  outDir,
  extraExternals = []
}: {
  root: string;
  outDir: string;
  extraExternals?: string[];
}): UserConfig {
  // Shared, import-map-provided dependencies. Externalizing a specifier a module does
  // not import is harmless, so keeping this list uniform is intentional.
  const sharedExternals = [
    "react",
    "@elsa-workflows/studio-sdk",
    "@elsa-workflows/studio-ui",
    "lucide-react"
  ];

  return defineConfig({
    plugins: [react(), moduleCssContractPlugin()],
    define: {
      "process.env.NODE_ENV": JSON.stringify("production")
    },
    build: {
      outDir,
      emptyOutDir: true,
      lib: {
        entry: resolve(root, "src/module.tsx"),
        formats: ["es"],
        fileName: () => "module.js"
      },
      rollupOptions: {
        external: [...sharedExternals, ...extraExternals],
        output: {
          assetFileNames: "module[extname]"
        }
      }
    },
    test: {
      environment: "jsdom",
      setupFiles: [resolve(__dirname, "vitest.setup.ts")]
    }
  });
}
