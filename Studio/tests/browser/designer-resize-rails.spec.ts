import { expect, test } from "@playwright/test";

for (const host of ["workflow", "activity"]) {
  for (const appearance of ["light", "dark"]) {
    test(`${host} resize rails clear rounded corners and retain their full drag target (${appearance})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(`/?mode=activity-duplicate-editor&composition=sequence&host=${host}&palette-theme=meridian&appearance=${appearance}`);
      // Reproduce an embedding host's rounded panels explicitly in this standalone fixture.
      await page.addStyleTag({ content: ".activity-duplicate-editor .wf-editor-body { --wf-radius-xl: 24px; } .activity-duplicate-editor :is(.wf-palette, .wf-inspector) { border-radius: var(--wf-radius-xl); }" });
      const canvasColor = await page.locator(".wf-canvas-shell").evaluate(element => getComputedStyle(element).backgroundColor);
      await expect(page.locator(".wf-editor-body")).toHaveCSS("background-color", canvasColor);
      for (const [name, panel, key, direction] of [
        ["activities", ".wf-palette", "ArrowRight", 1],
        ["inspector", ".wf-inspector", "ArrowLeft", -1]
      ] as const) {
        const handle = page.getByRole("separator", { name: `Resize ${name} panel`, exact: true });
        await expect(handle).toBeVisible();
        const bounds = (await handle.boundingBox())!;
        const panelBounds = (await page.locator(panel).boundingBox())!;
        expect(Math.abs(bounds.height - panelBounds.height)).toBeLessThanOrEqual(1);
        const chrome = await handle.evaluate(element => {
          const target = getComputedStyle(element), rail = getComputedStyle(element, "::after");
          return { top: parseFloat(rail.top), bottom: parseFloat(rail.bottom), targetBackground: target.backgroundColor,
            leftBorder: parseFloat(target.borderLeftWidth), rightBorder: parseFloat(target.borderRightWidth) };
        });
        expect(chrome.top).toBeGreaterThanOrEqual(12);
        expect(chrome.bottom).toBeGreaterThanOrEqual(12);
        const cornerRadius = await page.locator(panel).evaluate(element => parseFloat(getComputedStyle(element).borderTopRightRadius));
        expect(cornerRadius).toBe(24);
        expect(chrome.top).toBeGreaterThanOrEqual(cornerRadius);
        expect(chrome.bottom).toBeGreaterThanOrEqual(cornerRadius);
        expect(chrome.targetBackground).toBe(await page.locator(".wf-canvas-shell").evaluate(element => getComputedStyle(element).backgroundColor));
        expect(chrome.leftBorder + chrome.rightBorder).toBe(0);
        const initial = Number(await handle.getAttribute("aria-valuenow"));
        await handle.focus();
        await page.keyboard.press(key);
        await expect(handle).toHaveAttribute("aria-valuenow", String(initial + 16));
        expect(await handle.evaluate(element => getComputedStyle(element, "::after").boxShadow)).not.toBe("none");
        // Start above the visible rail: the corner clearance must not reduce the usable drag area.
        const resized = (await handle.boundingBox())!;
        await page.mouse.move(resized.x + resized.width / 2, resized.y + 2);
        await page.mouse.down();
        await page.mouse.move(resized.x + resized.width / 2 + direction * 32, resized.y + 2, { steps: 4 });
        await page.mouse.up();
        await expect(handle).toHaveAttribute("aria-valuenow", String(initial + 48));
      }
      await page.mouse.move(700, 400);
      await page.locator(".wf-canvas-shell").click({ position: { x: 20, y: 200 } });
      await page.screenshot({ path: testInfo.outputPath("inset-resize-rails.png") });
    });
  }
}
