import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  base: "/_content/Elsa.Admin.Web/admin/",
  plugins: [react()],
  build: {
    outDir: "../wwwroot/admin",
    emptyOutDir: true,
    rollupOptions: {
      external: ["react", "react-dom/client", "@elsa-workflows/admin-sdk"],
      output: {
        entryFileNames: "assets/[name].js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: "assets/[name][extname]"
      }
    }
  },
  test: {
    environment: "jsdom"
  }
});
