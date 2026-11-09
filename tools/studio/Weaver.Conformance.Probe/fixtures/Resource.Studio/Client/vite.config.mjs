import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

export default defineConfig({
  // The host import map shares React and the SDK. Emit calls through the existing
  // React import rather than introducing a bare automatic-runtime dependency.
  esbuild: { jsx: "transform", jsxFactory: "React.createElement", jsxFragment: "React.Fragment" },
  build: {
    outDir: fileURLToPath(new URL("../wwwroot/studio/modules/acme-home", import.meta.url)),
    emptyOutDir: true,
    cssCodeSplit: false,
    lib: { entry: "src/module.tsx", formats: ["es"], fileName: () => "module.js" },
    rollupOptions: {
      external: ["react", "@elsa-workflows/studio-sdk"],
      output: { assetFileNames: "module[extname]" }
    }
  }
});
