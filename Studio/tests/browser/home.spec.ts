import { expect, test } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import axe from "axe-core";

const screenshotDir = resolve(process.cwd(), process.env.HOME_SCREENSHOT_DIR ?? "../docs/pr-assets/2758");

async function expectNoSeriousAxeViolations(page: import("@playwright/test").Page) {
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const root = document.querySelector(".home-page") ?? document.querySelector(".dashboard-page");
    if (!root) return [{ id: "missing-home-page", impact: "critical", help: "Home page root was not in the document.", nodes: [] }];
    const result = await (window as Window & {
      axe: { run: (context: Element, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: unknown[] }> }> };
    }).axe.run(root, { resultTypes: ["violations"] });
    return result.violations.filter(violation => violation.impact === "critical" || violation.impact === "serious");
  });
  await test.info().attach("axe-critical-serious.json", {
    body: JSON.stringify(violations, null, 2),
    contentType: "application/json"
  });
  expect(violations).toEqual([]);
}

test.describe("customizable Home", () => {
  test.use({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2
  });

  test("renders the default Home landing and passes keyboard plus axe checks", async ({ page }) => {
    page.on("pageerror", error => console.error(error));
    await page.goto("/?mode=home");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Get started" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Recent workflows" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Recent runs" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Needs attention" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Tasks" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Create a workflow" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Open the designer" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Try Elsa locally" })).toBeVisible();
    await expect(page.getByText("Completed")).toBeVisible();
    await expect(page.getByText("All clear")).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Home" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Dashboard" })).toHaveCount(0);

    await page.getByRole("button", { name: "Edit home" }).focus();
    await expect(page.getByRole("button", { name: "Edit home" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("button", { name: "Add widget" }).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Reset to default" })).toBeVisible();
    const addWidget = page.getByRole("button", { name: "Add widget" }).first();
    await addWidget.focus();
    await expect(addWidget).toBeFocused();
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Add widget" });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel("Search widgets")).toBeFocused();
    await expect(dialog.getByRole("button", { name: "Tasks" })).toHaveCount(0);
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(addWidget).toBeFocused();

    await page.getByRole("button", { name: "Done" }).click();

    await expectNoSeriousAxeViolations(page);
  });

  test("renders run trends, exact buckets, definition names, and portfolio counts from the real widgets", async ({ page }) => {
    await page.goto("/?mode=home-dashboard&state=ready");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Workflow run health" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Workflow portfolio" })).toBeVisible();
    expect(await page.locator(".dashboard-grid > article.dashboard-widget").evaluateAll(articles =>
      articles.map(article => article.getAttribute("data-widget-id"))
    )).toEqual([
      "home.get-started",
      "workflows.run-health",
      "workflows.portfolio",
      "home.quick-actions",
      "home.needs-attention",
      "home.recent-runs",
      "home.recent-workflows"
    ]);
    await expect(page.locator('[data-widget-id="workflows.run-health"] [role="img"]')).toHaveAttribute("aria-label", /Run starts by hour: 12 starts/);
    await expect(page.locator('[data-widget-id="workflows.portfolio"] [data-metric="Active definitions"] .workflow-dashboard-metric__value')).toHaveText("12");
    await expect(page.getByRole("link", { name: "View all failed runs for Orders" })).toHaveAttribute("href", "/workflows/instances?definitionId=orders&status=Faulted&runKind=PublishedRun");

    await page.getByText("View exact bucket counts").click();
    await expect(page.locator('[data-widget-id="workflows.run-health"] tbody tr')).toHaveCount(24);
    const request = await page.evaluate(() => (window.__homeDashboardRequests ?? []).find(value => value.startsWith("/_elsa/workflows/dashboard/runs?")) ?? "");
    const query = new URL(request, "https://backend.example").searchParams;
    expect(query.get("bucket")).toBe("hour");
    expect(query.get("includeTestRuns")).toBe("false");
    expect(query.get("from")).not.toBeNull();
    expect(query.get("to")).not.toBeNull();
    await expectNoSeriousAxeViolations(page);
    await page.setViewportSize({ width: 768, height: 1000 });
    const healthWidth = await page.locator(".workflow-run-health").evaluate(element => ({ width: element.clientWidth, scrollWidth: element.scrollWidth }));
    expect(healthWidth.scrollWidth).toBeLessThanOrEqual(healthWidth.width);
  });

  test("distinguishes an empty start window from unavailable aggregate data", async ({ page }) => {
    await page.goto("/?mode=home-dashboard&state=empty");
    const health = page.locator('[data-widget-id="workflows.run-health"]');
    await expect(health.getByText("No workflow starts in this window.")).toBeVisible();
    await expect(health.locator('[data-metric="Started"] .workflow-dashboard-metric__value')).toHaveText("0");
    await expect(health.locator('[data-metric="Running now"] .workflow-dashboard-metric__value')).toHaveText("2");
    await expect(health.getByRole("img")).toHaveCount(0);
    await expect(page.locator('[data-widget-id="workflows.portfolio"] [data-metric="Active definitions"] .workflow-dashboard-metric__value')).toHaveText("0");

    await page.goto("/?mode=home-dashboard&state=unavailable");
    await expect(page.getByText("Run statistics are unavailable.")).toBeVisible();
    await expect(page.getByText("Portfolio statistics are unavailable.")).toBeVisible();
    await expect(page.getByText("Try refreshing the widget.").first()).toBeVisible();
    await expect(page.locator('[data-widget-id="workflows.run-health"] [data-metric="Started"]')).toHaveCount(0);
    await expect(page.locator('[data-widget-id="workflows.portfolio"] [data-metric="Active definitions"]')).toHaveCount(0);
  });

  test("does not request aggregate widgets without dashboard read permission", async ({ page }) => {
    await page.goto("/?mode=home-dashboard&state=denied");
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Workflow run health" })).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Workflow portfolio" })).toHaveCount(0);
    await expect.poll(() => page.evaluate(() => (window.__homeDashboardRequests ?? []).some(value => value === "/design/workflows/definitions?take=8"))).toBe(true);
    const aggregateRequests = await page.evaluate(() => (window.__homeDashboardRequests ?? []).filter(value => value.startsWith("/_elsa/workflows/dashboard/")));
    expect(aggregateRequests).toEqual([]);
  });

  test("keeps edited run-health options when the widget is removed and added again", async ({ page }) => {
    await page.goto("/?mode=home-dashboard&state=ready");
    const health = page.locator('[data-widget-id="workflows.run-health"]');
    await expect(health).toBeVisible();
    await page.getByRole("button", { name: "Edit home" }).click();
    await health.getByRole("button", { name: "Settings for Workflow run health" }).click();
    const dialog = page.getByRole("dialog", { name: "Workflow run health settings" });
    await dialog.getByLabel("Date range").selectOption({ label: "7 days" });
    await dialog.getByLabel("Include test runs").check();
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect.poll(() => page.evaluate(() => (window.__homeDashboardRequests ?? []).some(value => {
      if (!value.startsWith("/_elsa/workflows/dashboard/runs?")) return false;
      const query = new URL(value, "https://backend.example").searchParams;
      return query.get("bucket") === "day" && query.get("includeTestRuns") === "true";
    }))).toBe(true);

    await page.getByRole("button", { name: "Hide Workflow run health" }).click();
    await expect(page.locator('[data-widget-id="workflows.run-health"]')).toHaveCount(0);
    await page.getByRole("button", { name: "Add widget" }).first().click();
    const picker = page.getByRole("dialog", { name: "Add widget" });
    await picker.getByRole("button", { name: "Workflow run health" }).click();
    await expect(page.locator('[data-widget-id="workflows.run-health"]')).toBeVisible();
    await expect.poll(() => page.evaluate(() => (window.__homeDashboardLayouts ?? []).length)).toBeGreaterThanOrEqual(3);
    const savedOptions = await page.evaluate(() => {
      const layouts = window.__homeDashboardLayouts ?? [];
      const layout = layouts.at(-1) as { widgets?: Array<{ id: string; options?: unknown }> } | undefined;
      return layout?.widgets?.find(entry => entry.id === "workflows.run-health")?.options;
    });
    expect(savedOptions).toMatchObject({ range: "7d", includeTestRuns: true, keepVisible: true });
    await expect(page.locator('[data-widget-id="workflows.run-health"] .workflow-run-health__scope')).toContainText("day");
    await expect(page.locator('[data-widget-id="workflows.run-health"] .workflow-run-health__scope')).toContainText("Test runs included");
    await page.locator('[data-widget-id="workflows.run-health"] summary').click();
    await expect(page.locator('[data-widget-id="workflows.run-health"] tbody tr')).toHaveCount(7);
  });

  test("captures Classic light and dark before/after screenshots when asked", async ({ page }) => {
    test.skip(!process.env.HOME_SCREENSHOTS, "Screenshot capture is opt-in.");
    await mkdir(screenshotDir, { recursive: true });

    await page.goto("/?mode=home-before");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Get started" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toHaveCount(0);
    await page.screenshot({ path: resolve(screenshotDir, "before-classic-light.png"), fullPage: false });

    await page.goto("/?mode=home-before&theme=dark");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Get started" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "before-classic-dark.png"), fullPage: false });

    await page.goto("/?mode=home");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-classic-light.png"), fullPage: false });

    await page.goto("/?mode=home&theme=dark");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-classic-dark.png"), fullPage: false });
  });
});
