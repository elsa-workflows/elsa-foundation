import { expect, test } from "@playwright/test";

const cases = [
  { name: "320px viewport", viewportWidth: 320 },
  { name: "360px viewport", viewportWidth: 360 },
  { name: "286px module container in a wide viewport", viewportWidth: 1280, moduleWidth: 286 },
  { name: "1280px desktop reference", viewportWidth: 1280 }
];

async function readToolbarGeometry(page: import("@playwright/test").Page) {
  return page.locator(".feature-management-bulk-actions").evaluate(row => {
    const toRect = (element: Element) => {
      const rect = element.getBoundingClientRect();
      return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height };
    };
    const labelCount = row.querySelector("label > span");
    const selectedCount = row.querySelector(":scope > div > span");
    const buttons = Array.from(row.querySelectorAll("button"));
    if (!labelCount || !selectedCount) throw new Error("Both count labels must render.");
    const textFit = (element: Element) => element.clientWidth > 0 && element.scrollWidth <= element.clientWidth;
    const overlaps = (first: DOMRect, second: DOMRect) =>
      first.left < second.right && first.right > second.left && first.top < second.bottom && first.bottom > second.top;
    const countRects = [labelCount, selectedCount].map(element => element.getBoundingClientRect());
    const buttonRects = buttons.map(button => button.getBoundingClientRect());
    const rowRect = row.getBoundingClientRect();
    return {
      row: toRect(row),
      rowClientWidth: row.clientWidth,
      rowScrollWidth: row.scrollWidth,
      rowDisplay: getComputedStyle(row).display,
      visibleCount: { text: labelCount.textContent, rect: toRect(labelCount), fits: textFit(labelCount) },
      selectedCount: { text: selectedCount.textContent, rect: toRect(selectedCount), fits: textFit(selectedCount) },
      buttons: buttons.map((button, index) => ({
        text: button.textContent?.trim(),
        disabled: (button as HTMLButtonElement).disabled,
        rect: toRect(button),
        textFits: textFit(button),
        insideRow: buttonRects[index].left >= rowRect.left
          && buttonRects[index].right <= rowRect.right
          && buttonRects[index].top >= rowRect.top
          && buttonRects[index].bottom <= rowRect.bottom
      })),
      countOverlap: overlaps(countRects[0], countRects[1]),
      countButtonOverlap: countRects.some(countRect => buttonRects.some(buttonRect => overlaps(countRect, buttonRect))),
      buttonOverlap: buttonRects.some((rect, index) => buttonRects.slice(index + 1).some(other => overlaps(rect, other))),
      documentClientWidth: document.documentElement.clientWidth,
      documentScrollWidth: document.documentElement.scrollWidth
    };
  });
}

function expectToolbarToFit(geometry: Awaited<ReturnType<typeof readToolbarGeometry>>, expected: { visible: string; selected: string }) {
  expect(geometry.visibleCount.text).toBe(expected.visible);
  expect(geometry.selectedCount.text).toBe(expected.selected);
  expect(geometry.visibleCount.fits).toBe(true);
  expect(geometry.selectedCount.fits).toBe(true);
  expect(geometry.countOverlap).toBe(false);
  expect(geometry.countButtonOverlap).toBe(false);
  expect(geometry.buttonOverlap).toBe(false);
  expect(geometry.buttons.every(button => button.insideRow && button.textFits)).toBe(true);
  expect(geometry.rowScrollWidth).toBeLessThanOrEqual(geometry.rowClientWidth);
  expect(geometry.documentScrollWidth).toBeLessThanOrEqual(geometry.documentClientWidth);
}

test("Feature Management selection counts and actions reflow without clipping", async ({ page }, testInfo) => {
  for (const viewport of cases) {
    await test.step(viewport.name, async () => {
      await page.setViewportSize({ width: viewport.viewportWidth, height: 1000 });
      const path = viewport.moduleWidth ? `?moduleWidth=${viewport.moduleWidth}` : "";
      await page.goto(`/feature-management.html${path}`);
      const row = page.locator(".feature-management-bulk-actions");
      await expect(row).toContainText("0/114 visible");
      const empty = await readToolbarGeometry(page);
      expectToolbarToFit(empty, { visible: "0/114 visible", selected: "0 selected" });
      expect(empty.buttons.every(button => button.disabled)).toBe(true);

      const selectVisible = page.getByRole("checkbox", { name: "Select visible features" });
      await selectVisible.focus();
      await page.keyboard.press("Space");
      await expect(row).toContainText("114/114 visible");
      const populated = await readToolbarGeometry(page);
      expectToolbarToFit(populated, { visible: "114/114 visible", selected: "114 selected" });
      expect(populated.buttons.map(button => button.disabled)).toEqual([false, true, false]);

      await page.keyboard.press("Tab");
      await expect(page.getByRole("button", { name: "Enable selected features" })).toBeFocused();
      await page.keyboard.press("Tab");
      await expect(page.getByRole("button", { name: "Clear selected features" })).toBeFocused();
      await testInfo.attach(`${viewport.name.replaceAll(" ", "-")}-empty-geometry`, {
        body: JSON.stringify(empty, null, 2), contentType: "application/json"
      });
      await testInfo.attach(`${viewport.name.replaceAll(" ", "-")}-selected-geometry`, {
        body: JSON.stringify(populated, null, 2), contentType: "application/json"
      });

      if (viewport.viewportWidth === 1280 && !viewport.moduleWidth) {
        expect(populated.rowDisplay).toBe("flex");
        const buttonTops = populated.buttons.map(button => button.rect.top);
        expect(Math.max(...buttonTops) - Math.min(...buttonTops)).toBeLessThanOrEqual(1);
      } else {
        expect(populated.rowDisplay).toBe("grid");
      }
    });
  }
});
