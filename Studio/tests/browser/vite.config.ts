import { defineConfig, searchForWorkspaceRoot } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

export default defineConfig({
  root: __dirname,
  plugins: [react()],
  resolve: {
    alias: {
      "@elsa-workflows/studio-sdk": resolve(__dirname, "../../src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts"),
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
