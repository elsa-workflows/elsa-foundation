import { expect, test } from "@playwright/test";

for (const host of ["workflow", "activity"]) for (const width of [360, 390]) for (const mode of ["light", "dark"]) {
  test(`${host} block commands stay inside the phone canvas at ${width} ${mode}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=sequence&palette-theme=classic&appearance=${mode}`);
    await page.locator('.wf-sequence-step[data-id="write-one"]').press("Enter");
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    await page.setViewportSize({ width, height: 900 });
    const expandInspector = page.getByRole("button", { name: "Expand inspector panel", exact: true });
    await expect(expandInspector).toBeVisible();
    await expandInspector.click();
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    const canvas = page.locator(".wf-canvas-shell");
    const commands = canvas.getByRole("group", { name: "Named block commands", exact: true });
    await expect(commands).toBeAttached();
    // IntersectionObserver accounts for ancestor clipping: a command's painted area must
    // never escape its canvas into the active inspector, even when its own box is wider.
    await expect.poll(() => commands.evaluate(async element => {
      const boundary = element.closest(".wf-canvas-shell")!.getBoundingClientRect();
      const buttons = [...element.querySelectorAll("button")];
      const entries = await new Promise<IntersectionObserverEntry[]>(resolve => {
        const observer = new IntersectionObserver(observed => {
          observer.disconnect();
          resolve(observed);
        });
        buttons.forEach(button => observer.observe(button));
      });
      return entries.length === buttons.length && entries.every(entry => !entry.isIntersecting
        || entry.intersectionRect.left >= boundary.left - 1 && entry.intersectionRect.right <= boundary.right + 1);
    })).toBe(true);
    await page.screenshot({ path: info.outputPath("inspector-contained.png"), fullPage: true });

    await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
    const palette = page.getByRole("button", { name: "Collapse activities panel", exact: true });
    if (await palette.isVisible()) await palette.click();
    const create = commands.getByRole("button", { name: "Create Sequence block", exact: true });
    await expect(create).toBeVisible();
    // This isolated activity fixture has no resource candidate-validation endpoint.
    // Its commands must stay unavailable; editable activity drafts are covered on the composed host.
    if (host === "activity") {
      await expect(create).toBeDisabled();
      await page.screenshot({ path: info.outputPath("canvas-returned.png"), fullPage: true });
      return;
    }
    await expect(create).toBeEnabled();
    await create.focus();
    await create.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Create Sequence block", exact: true });
    await expect(dialog).toBeVisible();
    await dialog.getByRole("textbox", { name: "Name", exact: true }).press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(create).toBeFocused();
    await page.screenshot({ path: info.outputPath("canvas-returned.png"), fullPage: true });
  });
}
