import { expect, test, type Page } from "@playwright/test";

const longTitle = "Picker-created policy narrow-dark-1731234567890";

type Box = { left: number; right: number; top: number; bottom: number };

async function open(page: Page, width: number, appearance: "light" | "dark", extraQuery = "") {
  await page.setViewportSize({ width, height: 240 });
  const query = extraQuery ? `&${extraQuery}` : "";
  await page.goto(`/workspace-tab-strip.html?appearance=${appearance}${query}`);
  await expect(page.getByRole("tablist", { name: "Open screens", exact: true })).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForFunction(() => {
    const tablist = document.querySelector(".studio-workspace-tablist");
    const tab = document.querySelector<HTMLElement>('.studio-workspace-tab[data-active="true"]');
    if (!tablist || !tab) return false;
    const list = tablist.getBoundingClientRect();
    const box = tab.getBoundingClientRect();
    return box.left >= list.left - 1 && box.right <= list.right + 1;
  });
}

async function layout(page: Page) {
  return page.evaluate(() => {
    const box = (element: Element) => {
      const rect = element.getBoundingClientRect();
      return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width };
    };
    const strip = document.querySelector(".studio-workspace-tabs")!;
    const tablist = document.querySelector<HTMLElement>(".studio-workspace-tablist")!;
    const tab = document.querySelector<HTMLElement>('.studio-workspace-tab[data-active="true"]')!;
    const title = tab.querySelector<HTMLElement>(".studio-workspace-tab-title")!;
    const close = tab.querySelector<HTMLElement>(".studio-workspace-tab-close")!;
    const add = document.querySelector<HTMLElement>('[aria-label="New tab"]')!;
    const button = tab.querySelector<HTMLElement>('[role="tab"]')!;
    const background = [...document.querySelectorAll<HTMLElement>(".studio-workspace-tab")]
      .filter(candidate => candidate.getAttribute("data-active") !== "true")
      .map(candidate => {
        const icon = candidate.querySelector<HTMLElement>(".studio-workspace-tab-icon")!;
        return { tab: box(candidate), icon: box(icon), pinned: candidate.getAttribute("data-pinned") === "true" };
      });
    return {
      strip: box(strip),
      tablist: { ...box(tablist), scrollWidth: tablist.scrollWidth, clientWidth: tablist.clientWidth },
      tab: box(tab),
      title: {
        ...box(title),
        overflow: getComputedStyle(title).textOverflow,
        scrollWidth: title.scrollWidth,
        clientWidth: title.clientWidth
      },
      close: box(close),
      add: box(add),
      background,
      accessibleName: button.getAttribute("aria-label"),
      tooltip: button.getAttribute("title")
    };
  });
}

function fullyWithin(inner: Box, outer: Box) {
  expect(inner.left).toBeGreaterThanOrEqual(outer.left - 1);
  expect(inner.right).toBeLessThanOrEqual(outer.right + 1);
  expect(inner.top).toBeGreaterThanOrEqual(outer.top - 1);
  expect(inner.bottom).toBeLessThanOrEqual(outer.bottom + 1);
}

function noOverlap(a: Box, b: Box) {
  expect(a.right <= b.left + 1 || b.right <= a.left + 1 || a.bottom <= b.top + 1 || b.bottom <= a.top + 1).toBe(true);
}

function activeTabReadable(measured: Awaited<ReturnType<typeof layout>>) {
  fullyWithin(measured.tab, measured.strip);
  fullyWithin(measured.close, measured.strip);
  fullyWithin(measured.tab, measured.tablist);
  fullyWithin(measured.close, measured.tablist);
  noOverlap(measured.tab, measured.add);
  noOverlap(measured.close, measured.add);
  expect(measured.tab.right).toBeLessThanOrEqual(measured.add.left + 1);
  expect(measured.close.right).toBeLessThanOrEqual(measured.add.left + 1);
  expect(measured.title.overflow).toBe("ellipsis");
  expect(measured.title.scrollWidth).toBeGreaterThan(measured.title.clientWidth);
  expect(measured.accessibleName).toBe(longTitle);
  expect(measured.tooltip).toBe(longTitle);
}

for (const width of [320, 360, 1440]) {
  for (const appearance of ["light", "dark"] as const) {
    test(`the active workspace tab stays readable at ${width}px ${appearance}`, async ({ page }, testInfo) => {
      await open(page, width, appearance);
      const measured = await layout(page);
      activeTabReadable(measured);
      if (width <= 360) await page.screenshot({ path: testInfo.outputPath(`workspace-tab-strip-${width}-${appearance}.png`) });
    });
  }
}

for (const width of [320, 360]) {
  for (const appearance of ["light", "dark"] as const) {
    test(`many workspace tabs stay wide enough to scroll at ${width}px ${appearance}`, async ({ page }) => {
      await open(page, width, appearance, "tabs=many");
      await expect(page.locator(".studio-workspace-tab")).toHaveCount(8);
      await expect(page.locator('.studio-workspace-tab[data-pinned="true"]')).toHaveCount(1);
      const measured = await layout(page);
      expect(measured.background).toHaveLength(7);
      expect(measured.background.some(tab => tab.pinned)).toBe(true);
      for (const item of measured.background) {
        expect(item.tab.width).toBeGreaterThanOrEqual(48);
        fullyWithin(item.icon, item.tab);
      }
      expect(measured.tablist.scrollWidth).toBeGreaterThan(measured.tablist.clientWidth);
      activeTabReadable(measured);

      await page.getByRole("button", { name: "All tabs (8)" }).click();
      await page.getByRole("menuitemradio", { name: "Home" }).click();
      await page.waitForFunction(() => {
        const tablist = document.querySelector(".studio-workspace-tablist");
        const tab = document.querySelector<HTMLElement>('.studio-workspace-tab[data-active="true"]');
        if (!tablist || !tab) return false;
        const list = tablist.getBoundingClientRect();
        const box = tab.getBoundingClientRect();
        return tab.getAttribute("data-pinned") === "true"
          && box.left >= list.left - 1
          && box.right <= list.right + 1;
      });
    });
  }
}
