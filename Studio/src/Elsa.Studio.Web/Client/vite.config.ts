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
    environment: "jsdom",
    setupFiles: ["./src/testSetup.ts"],
    // Full-page jsdom renders (extension builder, module management) normally finish in well
    // under a second, but parallel workers on a loaded machine can slow them 10x+ past the
    // default 5s. Individual tests never legitimately take this long; this only delays failure
    // detection for genuinely hung tests.
    testTimeout: 15000
  }
});
