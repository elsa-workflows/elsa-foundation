import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The host PAGE contains no JSX and no React knowledge (see src/main.ts). But the Studio
// packages it consumes ARE authored in React/JSX, so the @vitejs/plugin-react transform is
// still needed to compile that source. React ends up bundled into this app's output like any
// other library dependency — the point of the example is that React is an implementation
// detail of the widgets, not a requirement of the host.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom"
  }
});
