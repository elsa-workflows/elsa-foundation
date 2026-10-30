import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

export default defineConfig({
  esbuild: { jsx: "automatic" },
  build: {
    outDir: fileURLToPath(new URL("../wwwroot/studio/modules/acme-home", import.meta.url)),
    emptyOutDir: true,
    cssCodeSplit: false,
    lib: { entry: "src/module.tsx", formats: ["es"], fileName: () => "module.js" },
    rollupOptions: {
      external: ["react", "react/jsx-runtime", "@elsa-workflows/studio-sdk"],
      output: { assetFileNames: "module[extname]" }
    }
  }
});
