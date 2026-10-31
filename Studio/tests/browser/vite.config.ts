import { defineConfig, searchForWorkspaceRoot } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

export default defineConfig({
  root: __dirname,
  // Keep the fixture optimizer separate from package builds and other Vite entry points.
  // Sharing their cache invalidates dependency URLs in already-open browser sessions.
  cacheDir: resolve(__dirname, "../../node_modules/.vite/browser"),
  plugins: [react()],
  // A first-use optimizer reload discards in-flight mocked authoring sessions.
  optimizeDeps: { include: ["lucide-react/dynamicIconImports"] },
  resolve: {
    alias: {
      "@elsa-workflows/studio-sdk": resolve(__dirname, "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts"),
      "@elsa-workflows/studio-ui": resolve(__dirname, "../../src/essentials/Elsa.Studio.Hosting/StudioUi/src/index.ts"),
      "@xyflow/react": resolve(__dirname, "../../src/essentials/Elsa.Studio.Workflows/Client/node_modules/@xyflow/react")
    }
  },
  server: {
    fs: {
      allow: [searchForWorkspaceRoot(__dirname), resolve(__dirname, "../../../Extensions")]
    },
    host: "127.0.0.1",
    port: 4179,
    strictPort: true
  }
});
