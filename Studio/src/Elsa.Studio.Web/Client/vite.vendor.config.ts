import { resolve } from "node:path";
import { defineConfig } from "vite";

export default defineConfig({
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  build: {
    outDir: "../wwwroot/studio",
    emptyOutDir: false,
    lib: {
      entry: {
        "vendor/react": resolve(__dirname, "src/vendor/react.ts"),
        "vendor/react-dom-client": resolve(__dirname, "src/vendor/react-dom-client.ts"),
        "vendor/react-query": resolve(__dirname, "src/vendor/react-query.ts"),
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
