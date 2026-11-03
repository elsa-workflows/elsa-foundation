import { resolve } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The `vendor/react` entry is the shim that actually *bundles* React and is served
// as `/studio/vendor/react.js` behind the import map's `react` specifier. Every other
// vendor entry (react-dom, react-query, studio-ui, lucide-react, ...) must instead
// import the shared React copy through that import map, so for them `react` is external.
// If `react` were externalized for the react shim too, the shim's `import React from
// "react"` would resolve back to `/studio/vendor/react.js` at runtime — i.e. the file
// importing itself — producing a "Detected cycle while resolving name 'default' in
// 'react'" error. So keep `react` external for everyone except the react shim itself.
const reactShim = resolve(__dirname, "src/vendor/react.ts");

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
      // Exception: the react shim itself must bundle React (see note above), otherwise
      // `/studio/vendor/react.js` would import itself and form a `default` export cycle.
      // Only the bare `react` specifier is provided by the import map. `react/jsx-runtime`
      // has no import-map entry, so it must stay bundled (into vendor/chunks/jsx-runtime.js)
      // — do not externalize it here.
      external: (source, importer) => {
        if (source !== "react") return false;
        // Bundle React into the react shim; externalize it for every other importer.
        return importer !== reactShim;
      },
      output: {
        entryFileNames: "[name].js",
        chunkFileNames: "vendor/chunks/[name].js",
        assetFileNames: "vendor/chunks/[name][extname]"
      }
    }
  }
});
