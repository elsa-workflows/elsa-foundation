import { expect, test, type Page } from "@playwright/test";

const longTitle = "Picker-created policy narrow-dark-1731234567890";

async function open(page: Page, width: number, appearance: "light" | "dark") {
  await page.setViewportSize({ width, height: 240 });
  await page.goto(`/workspace-tab-strip.html?appearance=${appearance}`);
  await expect(page.getByRole("tablist", { name: "Open screens", exact: true })).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  // Pin the layout without relying on scrollIntoView: a title change on the active tab
  // does not re-run that effect, so the close control must remain in view at scrollLeft 0.
  await page.locator(".studio-workspace-tablist").evaluate(element => { element.scrollLeft = 0; });
}

async function layout(page: Page) {
  return page.evaluate(() => {
    const box = (element: Element) => {
      const rect = element.getBoundingClientRect();
      return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
    };
    const strip = document.querySelector(".studio-workspace-tabs")!;
    const tab = document.querySelector<HTMLElement>('.studio-workspace-tab[data-active="true"]')!;
    const title = tab.querySelector<HTMLElement>(".studio-workspace-tab-title")!;
    const close = tab.querySelector<HTMLElement>(".studio-workspace-tab-close")!;
    const add = document.querySelector<HTMLElement>('[aria-label="New tab"]')!;
    const button = tab.querySelector<HTMLElement>('[role="tab"]')!;
    return {
      strip: box(strip),
      tab: box(tab),
      title: {
        ...box(title),
        overflow: getComputedStyle(title).textOverflow,
        scrollWidth: title.scrollWidth,
        clientWidth: title.clientWidth
      },
      close: box(close),
      add: box(add),
      accessibleName: button.getAttribute("aria-label"),
      tooltip: button.getAttribute("title")
    };
  });
}

function fullyWithin(
  inner: { left: number; right: number; top: number; bottom: number },
  outer: { left: number; right: number; top: number; bottom: number }
) {
  expect(inner.left).toBeGreaterThanOrEqual(outer.left - 1);
  expect(inner.right).toBeLessThanOrEqual(outer.right + 1);
  expect(inner.top).toBeGreaterThanOrEqual(outer.top - 1);
  expect(inner.bottom).toBeLessThanOrEqual(outer.bottom + 1);
}

function noOverlap(
  a: { left: number; right: number; top: number; bottom: number },
  b: { left: number; right: number; top: number; bottom: number }
) {
  expect(a.right <= b.left + 1 || b.right <= a.left + 1 || a.bottom <= b.top + 1 || b.bottom <= a.top + 1).toBe(true);
}

for (const width of [320, 360, 1440]) {
  for (const appearance of ["light", "dark"] as const) {
    test(`the active workspace tab stays readable at ${width}px ${appearance}`, async ({ page }, testInfo) => {
      await open(page, width, appearance);
      const measured = await layout(page);
      fullyWithin(measured.tab, measured.strip);
      fullyWithin(measured.close, measured.strip);
      noOverlap(measured.tab, measured.add);
      noOverlap(measured.close, measured.add);
      expect(measured.title.overflow).toBe("ellipsis");
      expect(measured.title.scrollWidth).toBeGreaterThan(measured.title.clientWidth);
      expect(measured.accessibleName).toBe(longTitle);
      expect(measured.tooltip).toBe(longTitle);
      if (width <= 360) await page.screenshot({ path: testInfo.outputPath(`workspace-tab-strip-${width}-${appearance}.png`) });
    });
  }
}
