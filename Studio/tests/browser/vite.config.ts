import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

export default defineConfig({
  root: __dirname,
  plugins: [react()],
  resolve: {
    alias: {
      "@elsa-workflows/studio-sdk": resolve(__dirname, "../../src/Elsa.Studio.Web/Client/src/sdk/index.ts")
    }
  },
  server: {
    host: "127.0.0.1",
    port: 4179,
    strictPort: true
  }
});
