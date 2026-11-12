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

test("Feature Management keeps Clear under the native pointer after selection", async ({ page }) => {
  for (const viewportWidth of [320, 360]) {
    await page.setViewportSize({ width: viewportWidth, height: 1000 });
    await page.goto("/feature-management.html");

    for (const host of ["Server", "Studio"]) {
      await page.getByRole("tablist", { name: "Hosts" }).getByRole("tab", { name: host }).click();
      const visibleCount = host === "Studio" ? 2 : 114;
      const row = page.locator(".feature-management-bulk-actions");
      await expect(row).toContainText(`0/${visibleCount} visible`);

      const clear = row.getByRole("button", { name: "Clear selected features" });
      await expect(clear).toBeDisabled();
      await row.getByRole("checkbox", { name: "Select visible features" }).focus();
      await page.keyboard.press("Space");
      await expect(row).toContainText(`${visibleCount}/${visibleCount} visible`);
      await expect(clear).toBeEnabled();

      await row.locator('label > input[type="checkbox"]').focus();
      const button = await clear.boundingBox();
      if (!button) throw new Error("The Clear button must have a visible box.");
      const x = button.x + button.width / 2;
      const y = button.y + button.height / 2;
      await page.mouse.move(x, y);
      await page.mouse.down();
      await page.mouse.up();
      await expect(row).toContainText("0 selected");
    }
  }
});


for (const mode of ["light", "dark"] as const) {
  test(`Feature categories stay discoverable and contained with long labels and many choices (${mode})`, async ({ page }, testInfo) => {
    await page.addInitScript(mode => {
      localStorage.setItem("elsa-studio-theme", "classic");
      localStorage.setItem("elsa-studio-workspace-style", "minimal");
      localStorage.setItem("elsa-studio-theme-mode", mode);
    }, mode);
    for (const viewport of cases) {
      await test.step(`${mode}: ${viewport.name}`, async () => {
        await page.setViewportSize({ width: viewport.viewportWidth, height: 1000 });
        const params = new URLSearchParams({ categoryRail: "1" });
        if (viewport.moduleWidth) params.set("moduleWidth", String(viewport.moduleWidth));
        await page.goto(`/feature-management.html?${params}`);
        await expect(page.locator("html")).toHaveAttribute("data-theme-mode", mode);
        const rail = page.getByRole("region", { name: "Categories", exact: true });
        const choices = rail.getByRole("group", { name: "Feature categories", exact: true });
        await expect(choices.getByRole("button")).toHaveCount(26);
        const longCategory = choices.getByRole("button", { name: /^ExtremelyLongUnbrokenCategoryName/ });
        await longCategory.focus();
        await longCategory.press("Enter");
        await expect(longCategory).toHaveAttribute("aria-pressed", "true");
        await expect(page.locator(".feature-management-card")).toHaveCount(1);
        const search = rail.getByRole("searchbox", { name: "Search categories", exact: true });
        await search.fill("no-category");
        await rail.getByRole("button", { name: "Clear category search", exact: true }).press("Enter");
        await expect(search).toBeFocused();
        await expect(longCategory).toHaveAttribute("aria-pressed", "true");
        const lastChoice = choices.getByRole("button").last();
        await lastChoice.focus();
        await expect(lastChoice).toBeInViewport();
        await choices.getByRole("button", { name: /^All categories/ }).press("Space");
        await expect(page.locator(".feature-management-card")).toHaveCount(114);
        const geometry = await rail.evaluate(element => {
          const rect = element.getBoundingClientRect();
          const list = element.parentElement!.querySelector(".feature-management-list-column")!.getBoundingClientRect();
          const categoryButtons = Array.from(element.querySelectorAll<HTMLButtonElement>(".feature-management-category-options > button"));
          const labelsFit = () => categoryButtons.every(button => {
            const box = button.getBoundingClientRect();
            return Array.from(button.querySelectorAll("span")).every(label => {
              const text = label.getBoundingClientRect();
              return text.top >= box.top - 1 && text.bottom <= box.bottom + 1;
            });
          });
          const labelsContained = labelsFit();
          // Revert the old flex sizing in the wide scrollable rail: the vertical guard
          // must detect compressed multiline labels, then pass with production sizing.
          let revertedLabelsContained: boolean | null = null;
          if (element.parentElement!.getBoundingClientRect().width >= 960) {
            const previous = categoryButtons.map(button => button.style.flexShrink);
            try {
              categoryButtons.forEach(button => { button.style.flexShrink = "1"; });
              revertedLabelsContained = labelsFit();
            } finally {
              categoryButtons.forEach((button, index) => { button.style.flexShrink = previous[index]; });
            }
          }
          return {
            width: element.parentElement!.getBoundingClientRect().width,
            beside: rect.right <= list.left,
            above: rect.bottom <= list.top,
            labelsContained,
            revertedLabelsContained,
            controlsFit: Array.from(element.querySelectorAll("button,input")).every(control => {
              const box = control.getBoundingClientRect();
              return box.left >= rect.left && box.right <= rect.right + 1 && control.scrollWidth <= control.clientWidth;
            }),
            overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth
          };
        });
        expect(geometry.controlsFit).toBe(true);
        expect(geometry.labelsContained).toBe(true);
        if (geometry.width >= 960) expect(geometry.revertedLabelsContained).toBe(false);
        expect(geometry.overflow).toBeLessThanOrEqual(1);
        expect(geometry.width >= 960 ? geometry.beside : geometry.above).toBe(true);
        await longCategory.scrollIntoViewIfNeeded();
        await search.focus();
        await testInfo.attach(`categories-${mode}-${viewport.viewportWidth}-${viewport.moduleWidth ?? "full"}`, {
          body: await page.screenshot(), contentType: "image/png"
        });
      });
    }
  });
}
