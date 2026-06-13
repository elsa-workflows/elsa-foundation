import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot/admin/modules/dashboard",
    emptyOutDir: true,
    lib: {
      entry: resolve(__dirname, "src/module.tsx"),
      formats: ["es"],
      fileName: () => "module.js"
    },
    rollupOptions: {
      external: ["react", "@elsa-workflows/admin-sdk"],
      output: {
        assetFileNames: "module[extname]"
      }
    }
  },
  test: {
    environment: "jsdom"
  }
});
