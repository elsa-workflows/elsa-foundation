import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  // Needed to transform the JSX in the studio-ui primitives; the react/react-query
  // vendor shims are plain .ts and unaffected.
  plugins: [react()],
  define: {
    "process.env.NODE_ENV": JSON.stringify("production")
  },
  build: {
    outDir: "../wwwroot/studio",
    emptyOutDir: false,
    lib: {
      entry: {
        "vendor/react": resolve(__dirname, "src/vendor/react.ts"),
        "vendor/react-dom": resolve(__dirname, "src/vendor/react-dom.ts"),
        "vendor/react-dom-client": resolve(__dirname, "src/vendor/react-dom-client.ts"),
        "vendor/react-query": resolve(__dirname, "src/vendor/react-query.ts"),
        "vendor/studio-ui": resolve(__dirname, "src/vendor/studio-ui.ts"),
        "vendor/lucide-react": resolve(__dirname, "src/vendor/lucide-react.ts"),
        "sdk/index": resolve(__dirname, "src/sdk/index.ts")
      },
      formats: ["es"]
    },
    rollupOptions: {
      // `react` is provided by the import map at runtime, so keep it out of the
      // vendored studio-ui bundle (one shared React copy across host + modules). The
      // tiny jsx-runtime is bundled in and re-imports the external `react`.
      external: ["react"],
      output: {
        entryFileNames: "[name].js",
        chunkFileNames: "vendor/chunks/[name].js",
        assetFileNames: "vendor/chunks/[name][extname]"
      }
    }
  }
});
