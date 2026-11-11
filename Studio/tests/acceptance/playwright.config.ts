import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.UX_ACCEPTANCE_OUT) {
  throw new Error("Run through tools/ux-acceptance/run.py with its owned disposable hosts.");
}
const output = process.env.UX_ACCEPTANCE_OUT;
// Each supplementary journey runs once in its own project only; run.py's verdict owns the exact inventory.
// Playwright greps "<project> <file> <title>"; anchor on the complete trailing title.
const supplement = (title: string) => ({
  testMatch: "supplements.spec.ts",
  grep: new RegExp(` ${title.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}$`)
});
export default defineConfig({
  testDir: ".",
  testMatch: "journeys.spec.ts",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  outputDir: `${output}/results`,
  reporter: [["line"], ["json", { outputFile: `${output}/playwright.json` }], ["html", { outputFolder: `${output}/html`, open: "never" }]],
  use: { baseURL: process.env.ELSA_STUDIO_URL, trace: "on", screenshot: "only-on-failure" },
  projects: [
    { name: "desktop-light", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "light" } },
    { name: "desktop-dark", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "dark" } },
    { name: "narrow-light", use: { viewport: { width: 360, height: 900 }, colorScheme: "light" } },
    { name: "narrow-dark", use: { viewport: { width: 360, height: 900 }, colorScheme: "dark" } },
    { name: "narrow-390-light", use: { viewport: { width: 390, height: 900 }, colorScheme: "light" } },
    { name: "narrow-390-dark", use: { viewport: { width: 390, height: 900 }, colorScheme: "dark" } },
    // Real touch emulation: coarse primary pointer, touch events and tap gestures against the combined hosts.
    ...[360, 390, 1440].flatMap(width => (["light", "dark"] as const).map(colorScheme => ({
      name: `touch-${width}-${colorScheme}`, ...supplement("coarse pointer grouped navigation"),
      use: { viewport: { width, height: 900 }, colorScheme, hasTouch: true, isMobile: width <= 1024 }
    }))),
    { name: "restricted-390-light", ...supplement("restricted principal settings access"), use: { viewport: { width: 390, height: 900 }, colorScheme: "light" } },
    { name: "availability-390-light", ...supplement("activity availability load failures and recovery"), use: { viewport: { width: 390, height: 900 }, colorScheme: "light" } },
    { name: "design-api-subset-1440-light", ...supplement("design-only API composition"),
      use: { baseURL: process.env.UX_DESIGN_STUDIO_URL, viewport: { width: 1440, height: 900 }, colorScheme: "light" } },
    { name: "runtime-api-subset-1440-light", ...supplement("runtime-only API composition and combined return"),
      use: { baseURL: process.env.UX_RUNTIME_STUDIO_URL, viewport: { width: 1440, height: 900 }, colorScheme: "light" } }
  ]
});
