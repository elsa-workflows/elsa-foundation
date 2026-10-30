import { expect, test } from "@playwright/test";

for (const fixture of [
  { path: "connection-secrets.html", opener: "Create connection", dialog: "Create connection", scope: ".connections-header" },
  { path: "secret-scopes.html", opener: "Create secret", dialog: "Create secret", scope: ".secrets-header" }
]) {
  test(`${fixture.dialog} retains desktop insets with the minimum host token contract`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/${fixture.path}`);
    // The documented minimum layer-1 host contract only requires spacing xs/sm/md/lg.
    // `initial` makes this optional custom property invalid even if the fixture host defines it.
    await page.addStyleTag({ content: ":root { --spacing-xl: initial !important; }" });
    await page.locator(fixture.scope).getByRole("button", { name: fixture.opener, exact: true }).click();
    const dialog = page.getByRole("dialog", { name: fixture.dialog, exact: true });

    for (const selector of ["header", "[class$='-create-content']", "[class*='-create-actions']"]) {
      const region = dialog.locator(selector);
      await expect(region).toBeVisible();
      const insets = await region.evaluate(element => {
        const style = getComputedStyle(element);
        return [parseFloat(style.paddingLeft), parseFloat(style.paddingRight)];
      });
      expect(insets.every(inset => inset >= 16)).toBe(true);
    }
    await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeInViewport();
    await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
  });
}
