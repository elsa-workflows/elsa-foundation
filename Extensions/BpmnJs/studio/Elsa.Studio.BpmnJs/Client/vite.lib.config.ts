import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import dts from "vite-plugin-dts";

export default defineConfig({
  plugins: [react(), dts({
    rollupTypes: true,
    insertTypesEntry: true,
    entryRoot: "src",
    exclude: ["**/__tests__/**"],
    afterDiagnostic(diagnostics) {
      if (diagnostics.length) throw new Error("BPMN module declaration diagnostics.");
    }
  })],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true,
    cssCodeSplit: false,
    lib: { entry: resolve(__dirname, "src/module.tsx"), formats: ["es"], fileName: () => "index.js" },
    rollupOptions: {
      external: [
        "react", "react/jsx-runtime", "react-dom",
        "@elsa-workflows/studio-sdk", "@elsa-workflows/studio-workflows",
        // Preserve upstream relative font URLs for the consuming application bundler. Library mode
        // otherwise inlines every legacy font format into one large stylesheet.
        "bpmn-js/dist/assets/diagram-js.css",
        "bpmn-js/dist/assets/bpmn-font/css/bpmn.css"
      ],
      output: {
        manualChunks(id) { if (id.includes("/node_modules/diagram-js/")) return "bpmn-js-diagram"; },
        assetFileNames: asset => asset.names.some(name => name.endsWith(".css")) ? "style.css" : "assets/[name]-[hash][extname]"
      }
    }
  }
});
