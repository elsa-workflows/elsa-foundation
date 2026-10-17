import { defineModuleConfig } from "../../../../vite.module.base";
import { mergeConfig } from "vite";

export default mergeConfig(
  defineModuleConfig({
    root: __dirname,
    outDir: "../wwwroot/studio/modules/workflows",
    extraExternals: ["@tanstack/react-query"]
  }),
  {
    build: {
      manifest: true,
      cssCodeSplit: true,
      rollupOptions: {
        output: {
          // Keep the rich editor deferred and each downloaded chunk within the module budget.
          manualChunks(id) {
            if (id.includes("/@codemirror/view/")) return "code-editor-view";
          }
        }
      }
    }
  }
);
