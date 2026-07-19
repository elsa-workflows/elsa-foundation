import { resolve } from "node:path";
import { defineConfig } from "vite";

export default defineConfig({
  test: {
    environment: "jsdom",
    setupFiles: [resolve(__dirname, "../../../vitest.setup.ts")]
  }
});
