import { resolve } from "node:path";
import { defineConfig } from "vite";

export default defineConfig({
  build: {
    outDir: "../wwwroot/admin",
    emptyOutDir: false,
    lib: {
      entry: {
        "vendor/react": resolve(__dirname, "src/vendor/react.ts"),
        "vendor/react-dom-client": resolve(__dirname, "src/vendor/react-dom-client.ts"),
        "sdk/index": resolve(__dirname, "src/sdk/index.ts")
      },
      formats: ["es"]
    },
    rollupOptions: {
      output: {
        entryFileNames: "[name].js",
        chunkFileNames: "vendor/chunks/[name].js",
        assetFileNames: "vendor/chunks/[name][extname]"
      }
    }
  }
});
