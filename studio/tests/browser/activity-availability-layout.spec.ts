import { expect, test, type Page } from "@playwright/test";

async function open(page: Page, mode: string, width: number, height: number, parameters = "") {
  await page.setViewportSize({ width, height });
  await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), mode);
  await page.goto(`/workspace-appearance.html?screen=availability&bottom-panel${parameters}`);
  await expect(page.getByRole("heading", { name: "Activity availability", exact: true })).toBeVisible();
}

for (const mode of ["light", "dark"]) {
  for (const short of [false, true]) {
    test(`fills available height with a ${short ? "short" : "long"} availability list in ${mode}`, async ({ page }) => {
      await open(page, mode, 1440, 1080, short ? "&short" : "");
      const list = page.locator(".availability-activity-list");
      const initial = (await list.boundingBox())!;
      expect(initial.height).toBeGreaterThan(520);
      const content = (await page.locator(".content").boundingBox())!;
      expect(content.y + content.height - initial.y - initial.height).toBeLessThan(70);
      await page.setViewportSize({ width: 1440, height: 1320 });
      await expect.poll(async () => (await list.boundingBox())!.height).toBeGreaterThan(initial.height + 200);

      await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
      const expanded = (await list.boundingBox())!;
      expect(expanded.height).toBeLessThan(initial.height + 100);
      expect(expanded.y + expanded.height).toBeLessThan((await page.locator(".bottom-panel").boundingBox())!.y);
      await expect(page.getByRole("switch", { name: "Fixture activity 001 available in new workflows", exact: true })).toBeDisabled();

      const filter = page.getByPlaceholder("Filter activities…");
      await filter.fill("Fixture activity 002");
      await page.getByRole("button", { name: /Fixture activity 002/ }).click();
      const details = page.getByRole("complementary", { name: "Fixture activity 002 details" });
      expect((await details.boundingBox())!.height).toBeCloseTo((await list.boundingBox())!.height, 0);
      const detailsBody = details.locator(".availability-details-body");
      await expect.poll(() => detailsBody.evaluate(element => element.scrollHeight - element.clientHeight)).toBeGreaterThan(0);
      await details.getByRole("button", { name: "Close details", exact: true }).click();
      await filter.fill("No matching activity");
      await expect(list.getByText("No activities match the filter.", { exact: true })).toBeVisible();
      expect((await list.boundingBox())!.height).toBeCloseTo(expanded.height, 0);
      await filter.fill("");

      if (!short) {
        const box = (await list.boundingBox())!;
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.wheel(0, 10000);
        await expect(page.getByRole("switch", { name: "Fixture activity 096 available in new workflows", exact: true })).toBeInViewport();
      }
      expect(await page.evaluate(() => window.scrollY)).toBe(0);
      expect(await page.locator(".content").evaluate(element => element.scrollTop)).toBe(0);
    });
  }
}

for (const width of [1280, 390]) {
  test(`keeps availability controls and details reachable in a short ${width}px workspace`, async ({ page }) => {
    await open(page, "dark", width, 640, "&extras");
    await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
    const list = page.locator(".availability-activity-list");
    await list.getByRole("button", { name: /Fixture activity 002/ }).click();
    const details = page.getByRole("complementary", { name: "Fixture activity 002 details" });
    await details.getByRole("button", { name: "Close details", exact: true }).click();
    const unresolved = page.getByText("Unknown set", { exact: true });
    await unresolved.scrollIntoViewIfNeeded();
    await expect(unresolved).toBeInViewport();
    expect((await unresolved.boundingBox())!.y).toBeLessThan((await page.locator(".bottom-panel").boundingBox())!.y);
    const mode = page.getByRole("button", { name: /^All except/ });
    await mode.focus();
    await expect(mode).toBeInViewport();
    expect(await page.evaluate(() => window.scrollY)).toBe(0);
  });
}
