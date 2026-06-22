import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  build: {
    outDir: "../wwwroot/studio/modules/workflows",
    emptyOutDir: true,
    lib: {
      entry: resolve(__dirname, "src/module.tsx"),
      formats: ["es"],
      fileName: () => "module.js"
    },
    rollupOptions: {
      external: ["react", "@elsa-workflows/studio-sdk"],
      output: {
        assetFileNames: "module[extname]"
      }
    }
  },
  test: {
    environment: "jsdom"
  }
});
