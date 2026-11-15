import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL, "Requires a local Studio/Workbench pair; changes only this browser's workspace tabs.");

async function capture(page: Page, info: TestInfo, name: string) {
  const path = info.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true, animations: "disabled" });
  await info.attach(name, { path, contentType: "image/png" });
}

async function stripGeometry(page: Page) {
  return page.locator(".studio-workspace-tabs").evaluate(strip => {
    const tabs = strip.querySelectorAll('.studio-workspace-tab');
    const list = strip.querySelector('[role="tablist"]')!;
    const add = strip.querySelector('[aria-label="New tab"]')!;
    const switcher = strip.querySelector('[aria-label^="All tabs"]')!;
    const last = tabs[tabs.length - 1].getBoundingClientRect();
    const plus = add.getBoundingClientRect();
    const menu = switcher.getBoundingClientRect();
    const row = list.getBoundingClientRect();
    const edge = strip.getBoundingClientRect();
    return {
      gap: plus.left - last.right, plusLeft: plus.left, plusRight: plus.right,
      plusCenterY: plus.y + plus.height / 2, menuCenterY: menu.y + menu.height / 2, rowCenterY: row.y + row.height / 2,
      plusVisible: document.elementFromPoint(plus.x + plus.width / 2, plus.y + plus.height / 2)?.closest('[aria-label="New tab"]') === add,
      menuLeft: menu.left, rightGap: edge.right - menu.right,
      scrollWidth: list.scrollWidth, clientWidth: list.clientWidth,
      pageWidth: document.documentElement.clientWidth, pageScroll: document.documentElement.scrollWidth
    };
  });
}

for (const mode of ["light", "dark"] as const) {
  test(`new tab follows the last tab and stays visible on overflow in ${mode}`, async ({ page }, info) => {
    await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), mode);
    await signIn(page);
    await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Workflows", exact: true }).click();
    const list = page.getByRole("tablist", { name: "Open screens", exact: true });
    const tabs = list.getByRole("tab");
    const add = page.getByRole("button", { name: "New tab", exact: true });
    const switcher = page.getByRole("button", { name: /^All tabs \(/ });
    await expect(tabs).toHaveCount(1);
    const sparse = await stripGeometry(page);
    expect(sparse.gap).toBeGreaterThanOrEqual(0);
    expect(sparse.gap).toBeLessThanOrEqual(12);
    expect(sparse.rightGap).toBeLessThanOrEqual(12);
    await capture(page, info, "sparse-tabs");

    await add.focus();
    await page.keyboard.press("Enter");
    await expect(tabs).toHaveCount(2);
    await expect.poll(() => new URL(page.url()).pathname).toBe("/");
    await page.keyboard.press("Alt+Shift+T");
    await expect(tabs).toHaveCount(3);
    await tabs.first().click();
    await expect(tabs.first()).toHaveAttribute("aria-selected", "true");
    const growing = await stripGeometry(page);
    expect(growing.gap).toBeLessThanOrEqual(12);
    expect(growing.plusLeft).toBeGreaterThan(sparse.plusLeft);
    expect(growing.menuLeft).toBeCloseTo(sparse.menuLeft, 0);
    await capture(page, info, "earlier-tab-selected");

    for (let count = 4; count <= 20; count++) {
      await add.click();
      await expect(tabs).toHaveCount(count);
    }
    await expect.poll(async () => {
      const geometry = await stripGeometry(page);
      return geometry.scrollWidth > geometry.clientWidth;
    }).toBe(true);
    const overflow = await stripGeometry(page);
    expect(overflow.plusVisible).toBe(true);
    expect(overflow.plusCenterY).toBeCloseTo(overflow.rowCenterY, 0);
    expect(overflow.menuCenterY).toBeCloseTo(overflow.rowCenterY, 0);
    expect(overflow.plusRight).toBeLessThanOrEqual(overflow.menuLeft);
    expect(overflow.menuLeft).toBeCloseTo(sparse.menuLeft, 0);
    await capture(page, info, "overflow-end");
    await list.evaluate(element => { element.scrollLeft = 0; });
    await expect.poll(async () => (await stripGeometry(page)).plusVisible).toBe(true);
    await capture(page, info, "overflow-start");
    await switcher.click();
    await page.getByRole("menuitemradio", { name: "Workflows", exact: true }).click();
    await expect(tabs.first()).toHaveAttribute("aria-selected", "true");

    await page.setViewportSize({ width: 360, height: 900 });
    const narrow = await stripGeometry(page);
    expect(narrow.plusVisible).toBe(true);
    expect(narrow.plusCenterY).toBeCloseTo(narrow.rowCenterY, 0);
    expect(narrow.menuCenterY).toBeCloseTo(narrow.rowCenterY, 0);
    expect(narrow.plusRight).toBeLessThanOrEqual(narrow.menuLeft);
    expect(narrow.rightGap).toBeLessThanOrEqual(12);
    expect(narrow.pageScroll).toBeLessThanOrEqual(narrow.pageWidth + 1);
    await capture(page, info, "narrow-overflow");
    await add.click();
    await expect(tabs).toHaveCount(21);
    await expect.poll(() => new URL(page.url()).pathname).toBe("/");
  });
}
