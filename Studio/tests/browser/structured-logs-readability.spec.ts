import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";

// Deterministic component evidence; real composed hosts provide product acceptance.
const category = "Company.Fulfillment.WorkflowProcessing.Services.Diagnostics.OperationCompletionLogger";
const source = "warehouse-processing-edge-node-with-a-long-source-identifier";
const message = "The order was accepted and its allocation was recorded before confirmation. ".repeat(3) + "correlation_" + "a".repeat(80);
const entries = [
  { sequence: 101, timestamp: "2026-10-31T10:42:10Z", level: "Information", category: "Orders", sourceId: "server", message: "Order accepted.", properties: {} },
  { sequence: 102, timestamp: "2026-10-31T10:42:11Z", level: "Information", category, sourceId: source, message, properties: {}, exception: { type: "Example.Timeout", message: "Transient downstream condition.", stackTrace: "Example.Timeout at confirmation" } }
];

for (const [theme, workspace] of [["linen", "linen"], ["classic", "minimal"]]) {
  for (const mode of ["light", "dark"]) {
    for (const width of [390, 1024, 1280, 1440]) {
      test(`${theme} ${mode} keeps log messages and recovery usable at ${width}px`, async ({ page, context }) => {
        await page.setViewportSize({ width, height: 1000 });
        await context.grantPermissions(["clipboard-read", "clipboard-write"]);
        await page.addInitScript(({ theme, workspace, mode }) => {
          localStorage.setItem("elsa-studio-theme", theme);
          localStorage.setItem("elsa-studio-workspace-style", workspace);
          localStorage.setItem("elsa-studio-theme-mode", mode);
          localStorage.setItem("elsa-studio-structured-logs-autoscroll", "false");
        }, { theme, workspace, mode });
        await page.route("**/_elsa/studio/diagnostics/structured-logs/**", async route => {
          const url = new URL(route.request().url());
          if (url.pathname.endsWith("/sources")) return route.fulfill({ json: [{ id: "server", displayName: "Server" }, { id: "studio", displayName: "Studio" }, { id: source, displayName: "Warehouse stream" }] });
          if (url.pathname.endsWith("/recent")) {
            const filter = url.searchParams.get("category") ?? "";
            const sourceId = url.searchParams.get("source") ?? "";
            const level = url.searchParams.get("minLevel") ?? "Information";
            return route.fulfill({ json: entries.filter(entry => (!filter || entry.category.includes(filter)) && (!sourceId || entry.sourceId === sourceId) && ["", "Trace", "Debug", "Information"].includes(level)) });
          }
          return route.continue();
        });
        await page.goto("/minimal-studio.html?screen=logs");
        await expect(page.locator("html")).toHaveAttribute("data-workspace-style", workspace);
        const table = page.getByRole("table", { name: "Structured logs", exact: true });
        await expect(table.getByRole("columnheader")).toHaveCount(6);
        await expect(table.getByRole("row", { name: /Select Information structured log/ })).toHaveCount(2);
        await page.getByRole("button", { name: "Pause", exact: true }).click();
        const details = page.getByRole("complementary", { name: "Structured log details", exact: true });
        for (const entry of entries) {
          const row = table.getByRole("row", { name: `Select Information structured log from ${entry.category}`, exact: true });
          await row.click();
          await expect(details).toBeVisible();
          for (const wrap of [false, true]) for (const compact of [false, true]) {
            for (const [name, pressed] of [["Wrap", wrap], ["Compact", compact]] as const) {
              const control = page.getByRole("button", { name, exact: true });
              if (await control.getAttribute("aria-pressed") !== String(pressed)) await control.click();
              await expect(control).toHaveAttribute("aria-pressed", String(pressed));
            }
            await table.evaluate(element => { element.scrollLeft = 0; });
            const geometry = await row.locator(".structured-log-message").evaluate(element => {
              const viewport = element.closest(".structured-logs-rows")!;
              const area = viewport.getBoundingClientRect();
              const text = element.getBoundingClientRect();
              const header = viewport.querySelector(".structured-logs-grid-header")!.getBoundingClientRect();
              return { message: Math.max(0, Math.min(text.right, area.right) - Math.max(text.left, area.left)), rows: viewport.clientHeight - header.height };
            });
            expect(geometry.message, "useful message width beside selected details").toBeGreaterThanOrEqual(250);
            expect(geometry.rows, "details leave usable log rows").toBeGreaterThanOrEqual(100);
            await row.getByRole("button", { name: /Copy structured log message/ }).click();
            await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(entry.message);
            await expect(details.getByRole("heading", { name: entry.category, exact: true })).toBeVisible();
          }
        }
        await details.getByText(source, { exact: true }).scrollIntoViewIfNeeded();
        await expect(details.getByText(source, { exact: true })).toBeVisible();
        await details.getByText(category, { exact: true }).last().scrollIntoViewIfNeeded();
        await expect(details.getByText(category, { exact: true }).last()).toBeVisible();
        await page.getByRole("button", { name: "Copy selected", exact: true }).click();
        const copied = await page.evaluate(() => navigator.clipboard.readText());
        expect(copied).toContain(category);
        expect(copied).toContain(message);
        const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "Download visible", exact: true }).click()]);
        const exported = (await readFile((await download.path())!)).toString("utf8").trim().split(/\r?\n/).map(line => JSON.parse(line));
        expect(exported).toEqual(expect.arrayContaining([expect.objectContaining({ sourceId: source, category, message })]));
        await page.getByRole("button", { name: "Close structured log details", exact: true }).click();
        const sourceFilter = page.getByRole("combobox", { name: "Structured log source", exact: true });
        await sourceFilter.selectOption("server");
        await expect(table.getByRole("row", { name: /Select Information structured log/ })).toHaveCount(1);
        await expect(table.getByRole("row", { name: "Select Information structured log from Orders", exact: true })).toBeVisible();
        await sourceFilter.selectOption(source);
        await expect(table.getByRole("row", { name: /Select Information structured log/ })).toHaveCount(1);
        await expect(table.getByRole("row", { name: `Select Information structured log from ${category}`, exact: true })).toBeVisible();
        await sourceFilter.selectOption("studio");
        await expect(table.getByText("No logs match these filters.", { exact: true })).toBeVisible();
        await table.getByRole("button", { name: "Reset filters", exact: true }).click();
        await expect(table.getByRole("row", { name: /Select Information structured log/ })).toHaveCount(2);
        await table.evaluate(element => { element.scrollLeft = element.scrollWidth; });
        await page.getByRole("combobox", { name: "Minimum log level", exact: true }).selectOption("Warning");
        await page.getByRole("combobox", { name: "Structured log source", exact: true }).selectOption("studio");
        const categoryFilter = page.getByRole("textbox", { name: "Filter by category", exact: true });
        await categoryFilter.fill("no-match-readability");
        const empty = table.getByText("No logs match these filters.", { exact: true });
        await expect(empty).toBeVisible();
        expect(await empty.evaluate(element => { const viewport = element.closest(".structured-logs-rows")!.getBoundingClientRect(); const box = element.getBoundingClientRect(); return box.left >= viewport.left && box.right <= viewport.right; })).toBe(true);
        await table.getByRole("button", { name: "Reset filters", exact: true }).click();
        await expect(categoryFilter).toBeFocused();
        await expect(categoryFilter).toHaveValue("");
        await expect(page.getByRole("combobox", { name: "Minimum log level", exact: true })).toHaveValue("Information");
        await expect(page.getByRole("combobox", { name: "Structured log source", exact: true })).toHaveValue("");
        await expect(page.getByRole("button", { name: "Resume", exact: true })).toBeVisible();
        await expect(table.getByRole("row", { name: /Select Information structured log/ })).toHaveCount(2);
        await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      });
    }
  }
}

test("dock reset restores filters and level focus after copying and scrolling", async ({ page, context }) => {
  await page.setViewportSize({ width: 1024, height: 1000 });
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.addInitScript(() => localStorage.setItem("elsa-studio-structured-logs-autoscroll", "false"));
  await page.route("**/_elsa/studio/diagnostics/structured-logs/**", async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith("/sources")) return route.fulfill({ json: [{ id: "server", displayName: "Server" }, { id: "studio", displayName: "Studio" }, { id: source, displayName: "Warehouse stream" }] });
    if (url.pathname.endsWith("/recent")) {
      const sourceId = url.searchParams.get("source") ?? "";
      const level = url.searchParams.get("minLevel") ?? "Information";
      return route.fulfill({ json: entries.filter(entry => (!sourceId || entry.sourceId === sourceId) && ["", "Trace", "Debug", "Information"].includes(level)) });
    }
    return route.continue();
  });
  await page.goto("/minimal-studio.html?screen=logs-panel");
  const table = page.getByRole("table", { name: "Structured logs", exact: true });
  let paneWidth = 0;
  // Exercise a genuinely overflowing dock using its measured container, not a phone card.
  for (const width of [1024, 960, 900, 860, 820, 780, 740, 700]) {
    await page.setViewportSize({ width, height: 1000 });
    paneWidth = await page.locator(".structured-logs-panel").evaluate(element => element.clientWidth);
    if (paneWidth > 620 && paneWidth < 710) break;
  }
  expect(paneWidth).toBeGreaterThan(620);
  expect(paneWidth).toBeLessThan(710);
  const rows = table.getByRole("row", { name: /Select Information structured log/ });
  await expect(rows).toHaveCount(2);
  await expect(page.getByRole("textbox", { name: "Filter by category", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Pause", exact: true }).click();
  await rows.first().getByRole("button", { name: /Copy structured log message/ }).click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(entries[0].message);
  await expect.poll(() => table.evaluate(element => element.scrollWidth - element.clientWidth)).toBeGreaterThan(0);
  await table.evaluate(element => { element.scrollLeft = element.scrollWidth; });
  await expect.poll(() => table.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  const level = page.getByRole("combobox", { name: "Minimum log level", exact: true });
  const sourceFilter = page.getByRole("combobox", { name: "Structured log source", exact: true });
  await level.selectOption("Warning");
  await sourceFilter.selectOption("studio");
  const empty = table.getByText("No logs match these filters.", { exact: true });
  await expect(empty).toBeVisible();
  expect(await empty.evaluate(element => {
    const viewport = element.closest(".structured-logs-rows")!.getBoundingClientRect();
    const box = element.getBoundingClientRect();
    return box.left >= viewport.left && box.right <= viewport.right;
  })).toBe(true);
  const reset = table.getByRole("button", { name: "Reset filters", exact: true });
  await expect(reset).toBeVisible();
  await reset.focus();
  await reset.press("Enter");
  await expect(level).toBeFocused();
  await expect(level).toHaveValue("Information");
  await expect(sourceFilter).toHaveValue("");
  await expect(rows).toHaveCount(2);
  await expect(page.getByRole("button", { name: "Resume", exact: true })).toBeVisible();
});
