import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Plain application build. Unlike the Studio module clients (which build a library and
// externalize react / @elsa-workflows/* so the host provides one shared copy at runtime),
// an end-consumer app like this one BUNDLES everything it imports. The workspace
// dependencies resolve to raw TypeScript source in-repo, so Vite compiles the Studio
// packages from source as part of this app's build — that is intended for the example.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom"
  }
});
