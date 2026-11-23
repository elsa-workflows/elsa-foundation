import { expect, test } from "@playwright/test";

for (const width of [1440, 390]) for (const mode of ["light", "dark"]) {
  test(`confirmation keeps long identities and actions inside the dialog at ${width}px in ${mode}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 960 });
    await page.addInitScript(mode => {
      localStorage.setItem("elsa-studio-theme-mode", mode);
    }, mode);
    await page.goto("/workspace-appearance.html?screen=dialogs");
    await page.getByRole("button", { name: "Remove external link", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Remove external link", exact: true });
    await expect(dialog).toBeVisible();
    await dialog.evaluate(async element => {
      await document.fonts.ready;
      await Promise.all(element.getAnimations({ subtree: true }).map(animation => animation.finished));
    });
    const dimensions = await dialog.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return {
        width: element.clientWidth,
        scroll: element.scrollWidth,
        left: bounds.left,
        right: bounds.right,
        children: [...element.querySelectorAll(".studio-dialog-message, button")].map(child => {
          const box = child.getBoundingClientRect();
          return { text: child.textContent, left: box.left, right: box.right };
        })
      };
    });
    expect(dimensions.scroll).toBeLessThanOrEqual(dimensions.width + 1);
    expect(dimensions.left).toBeGreaterThanOrEqual(0);
    expect(dimensions.right).toBeLessThanOrEqual(width);
    for (const child of dimensions.children) {
      expect(child.left, child.text ?? "").toBeGreaterThanOrEqual(dimensions.left);
      expect(child.right, child.text ?? "").toBeLessThanOrEqual(dimensions.right);
    }
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(dialog).toHaveCount(0);
  });
}
