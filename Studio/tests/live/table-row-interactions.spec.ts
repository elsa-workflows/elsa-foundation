import { expect, test } from "@playwright/test";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL, "Requires a running Studio/Workbench pair with existing workflow and activity definitions, executables and runs.");
test.use({ ignoreHTTPSErrors: true, trace: "on" });

const surfaces = [
  { id: "definitions", path: "/workflows/definitions", required: true },
  { id: "executables", path: "/workflows/executables", required: true },
  { id: "runs", path: "/workflows/instances", required: true },
  { id: "activities", path: "/workflows/activity-definitions", required: true },
  { id: "backends", path: "/settings/backends", required: true },
  { id: "connections", path: "/security/connections" },
  { id: "secrets", path: "/security/secrets" },
  { id: "decisions", path: "/decisions" },
  { id: "tasks", path: "/user-tasks" },
  { id: "forms", path: "/user-tasks/forms" },
  { id: "dashboards", path: "/dashboards" },
  { id: "questions", path: "/questions" },
  { id: "tags", path: "/workflows/tags" },
  { id: "bpmn-runs", path: "/workflows/bpmn/runs" },
  { id: "logs", path: "/diagnostics/structured-logs" },
  { id: "traces", path: "/diagnostics/open-telemetry" }
];

for (const theme of ["light", "dark"]) {
  test(`resource rows on the composed Studio host in ${theme}`, async ({ page }, info) => {
    test.setTimeout(300_000);
    await page.addInitScript(theme => localStorage.setItem("elsa-studio-theme-mode", theme), theme);
    await signIn(page);
    const coverage: { surface: string; path: string; status: string; reason?: string }[] = [];
    try {
      for (const surface of surfaces) {
        await page.setViewportSize({ width: 1440, height: 900 });
        await page.goto(surface.path);
        const row = page.locator(".content [data-studio-row-interactive]:visible").first();
        if (surface.required) {
          try {
            await expect(row).toBeVisible();
          } catch (error) {
            await page.screenshot({ path: info.outputPath(`${surface.id}-failed.png`) });
            await info.attach(`${surface.id}-failure.yml`, { body: await page.locator("body").ariaSnapshot(), contentType: "text/yaml" });
            coverage.push({ surface: surface.id, path: page.url(), status: "failed", reason: (await page.locator("body").innerText()).slice(0, 2500) });
            throw error;
          }
        }
        else {
          // Diagnostics intentionally keep their streaming connection open.
          await row.waitFor({ state: "visible", timeout: 4_000 }).catch(() => undefined);
          if (!await row.count()) {
            coverage.push({ surface: surface.id, path: page.url(), status: "not-qualified", reason: (await page.locator(".content").innerText()).slice(0, 500) });
            await page.screenshot({ path: info.outputPath(`${surface.id}-unavailable.png`) });
            continue;
          }
        }
        for (const width of [1440, 390]) {
          await page.setViewportSize({ width, height: 900 });
          await row.hover();
          await expect(row).toHaveCSS("cursor", "pointer");
          const sharedHover = await row.evaluate(element => {
            const probe = element.ownerDocument.createElement("span");
            probe.style.backgroundColor = "var(--studio-row-hover)";
            element.appendChild(probe);
            const color = getComputedStyle(probe).backgroundColor;
            probe.remove();
            return color;
          });
          if (await row.getAttribute("aria-selected") !== "true" && !await row.evaluate(element => element.classList.contains("selected"))) await expect(row).toHaveCSS("background-color", sharedHover);
          await page.screenshot({ path: info.outputPath(`${surface.id}-hover-${width}.png`) });
        }
        await page.setViewportSize({ width: 1440, height: 900 });
        await row.focus();
        await page.keyboard.press("Tab");
        await page.keyboard.press("Shift+Tab");
        await expect(row).toBeFocused();
        await expect(row).toHaveCSS("outline-style", "solid");
        await page.screenshot({ path: info.outputPath(`${surface.id}-focus.png`) });
        await info.attach(`${surface.id}-row-accessibility.yml`, { body: await row.ariaSnapshot(), contentType: "text/yaml" });
        if (surface.required) {
          const before = page.url();
          await row.press("Enter");
          await expect.poll(async () => page.url() !== before || !await row.count()).toBe(true);
        }
        coverage.push({ surface: surface.id, path: surface.path, status: surface.required ? "hover-focus-open-passed" : "hover-focus-passed" });
      }
    } finally {
      await info.attach("surface-coverage.json", { body: JSON.stringify(coverage, null, 2), contentType: "application/json" });
    }
  });
}
