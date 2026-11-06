import { expect, type Page, type TestInfo } from "@playwright/test";

/** Uses only real buffered logs from the disposable composed hosts. */
export async function structuredLogsReadabilityJourney(page: Page, info: TestInfo, capture: (page: Page, info: TestInfo, name: string) => Promise<void>) {
  const originalViewport = page.viewportSize()!;
  await page.goto("/diagnostics/structured-logs");
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: new URL(page.url()).origin });
  const section = page.locator(".structured-logs-page:visible");
  const table = section.getByRole("table", { name: "Structured logs", exact: true });
  const row = table.getByRole("row", { name: /Select Information structured log/ }).first();
  await expect(row).toBeVisible();
  await section.getByRole("button", { name: "Pause", exact: true }).click();
  const autoscroll = section.getByRole("button", { name: "Autoscroll", exact: true });
  if (await autoscroll.getAttribute("aria-pressed") === "true") await autoscroll.click();
  for (const name of ["Wrap", "Compact"]) {
    const control = section.getByRole("button", { name, exact: true });
    if (await control.getAttribute("aria-pressed") === "true") await control.click();
  }
  await row.click();
  const details = section.getByRole("complementary", { name: "Structured log details", exact: true });
  await expect(details).toBeVisible();
  const message = await row.locator(".structured-log-message").innerText();
  for (const width of [1440, 1280, 1024, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    await table.evaluate(element => { element.scrollLeft = 0; });
    await expect.poll(() => row.locator(".structured-log-message").evaluate(element => {
      const viewport = element.closest(".structured-logs-rows")!.getBoundingClientRect();
      const box = element.getBoundingClientRect();
      return Math.min(box.right, viewport.right) - Math.max(box.left, viewport.left);
    }), { message: "Message remains readable beside the selected details" }).toBeGreaterThanOrEqual(250);
    await expect.poll(() => table.evaluate(element => element.clientHeight - element.querySelector(".structured-logs-grid-header")!.getBoundingClientRect().height)).toBeGreaterThanOrEqual(100);
    await row.getByRole("button", { name: /Copy structured log message/ }).click();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(message);
    await capture(page, info, `readability-${width}`);
  }
  await section.getByRole("button", { name: "Wrap", exact: true }).click();
  await expect(section.getByRole("button", { name: "Wrap", exact: true })).toHaveAttribute("aria-pressed", "true");
  await capture(page, info, "readability-wrap");
  await section.getByRole("button", { name: "Compact", exact: true }).click();
  await expect(section.getByRole("button", { name: "Compact", exact: true })).toHaveAttribute("aria-pressed", "true");
  await capture(page, info, "readability-compact");
  await details.getByText("Category", { exact: true }).scrollIntoViewIfNeeded();
  await expect(details.getByText("Source ID", { exact: true })).toBeVisible();
  await section.getByRole("button", { name: "Copy selected", exact: true }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain(message);
  const [download] = await Promise.all([page.waitForEvent("download"), section.getByRole("button", { name: "Download visible", exact: true }).click()]);
  expect(await download.failure()).toBeNull();
  await section.getByRole("button", { name: "Close structured log details", exact: true }).click();
  await table.evaluate(element => { element.scrollLeft = element.scrollWidth; });
  const category = section.getByRole("textbox", { name: "Filter by category", exact: true });
  await category.fill("no-match-readability-acceptance");
  const empty = table.getByText("No logs match these filters.", { exact: true });
  await expect(empty).toBeVisible();
  expect(await empty.evaluate(element => {
    const viewport = element.closest(".structured-logs-rows")!.getBoundingClientRect();
    const box = element.getBoundingClientRect();
    return box.left >= viewport.left && box.right <= viewport.right;
  })).toBe(true);
  await capture(page, info, "readability-empty");
  await table.getByRole("button", { name: "Reset filters", exact: true }).click();
  await expect(category).toBeFocused();
  await expect(category).toHaveValue("");
  await expect(section.getByRole("button", { name: "Resume", exact: true })).toBeVisible();
  await expect(row).toBeVisible();
  await capture(page, info, "readability-reset");
  await page.setViewportSize(originalViewport);
  await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
  const dock = page.getByRole("region", { name: "Bottom panel", exact: true });
  await dock.getByRole("tab", { name: "Structured Logs", exact: true }).click();
  const dockRow = dock.getByRole("row", { name: /Select Information structured log/ }).first();
  await expect(dockRow).toBeVisible();
  await dockRow.click();
  await expect(dock.getByRole("complementary", { name: "Structured log details", exact: true })).toBeVisible();
  await dockRow.getByRole("button", { name: /Copy structured log message/ }).click();
  await capture(page, info, "readability-dock");
}
