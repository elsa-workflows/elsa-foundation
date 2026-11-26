import { defineConfig } from "@playwright/test";
export default defineConfig({ testDir: ".", testMatch: "dashboardDefinition.spec.ts", workers: 1, retries: 0, forbidOnly: true });
