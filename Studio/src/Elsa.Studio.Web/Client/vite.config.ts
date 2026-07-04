import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  base: "/studio/",
  plugins: [react()],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  build: {
    outDir: "../wwwroot/studio",
    emptyOutDir: true,
    rollupOptions: {
      external: ["react", "react-dom", "react-dom/client", "@elsa-workflows/studio-sdk", "@tanstack/react-query", "lucide-react"],
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
