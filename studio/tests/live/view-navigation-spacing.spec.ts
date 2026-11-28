import { expect, test, type Page } from "@playwright/test";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL, "Requires a running Studio/Workbench pair with an existing workflow definition.");
test.use({ ignoreHTTPSErrors: true });

async function navigationGeometry(page: Page) {
  return page.getByRole("navigation", { name: "Workflows views", exact: true }).evaluate(navigation => {
    const content = navigation.closest(".content")!.getBoundingClientRect();
    const row = navigation.getBoundingClientRect();
    const active = navigation.querySelector('[aria-current="page"]')!.getBoundingClientRect();
    return { left: row.left, top: row.top, right: row.right, bottom: row.bottom,
      inset: row.left - content.left, activeLeft: active.left, activeTop: active.top };
  });
}

for (const mode of ["light", "dark"] as const) {
  test(`local view tabs stay in place between the list and editor in ${mode}`, async ({ page }, info) => {
    await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), mode);
    await signIn(page);
    await page.locator(".sidebar").getByRole("link", { name: "Workflows", exact: true }).click();
    const views = page.getByRole("navigation", { name: "Workflows views", exact: true });
    const definitions = page.getByRole("table", { name: "Workflow definitions", exact: true });
    await expect(definitions).toHaveAttribute("aria-busy", "false");
    const workflow = definitions.getByRole("link").first();
    await expect(workflow).toBeVisible();

    for (const width of [1440, 768, 390]) {
      await page.setViewportSize({ width, height: 900 });
      await expect(views).toBeVisible();
      const before = await navigationGeometry(page);
      expect(before.inset).toBeCloseTo(16, 0);
      expect(before.activeTop - before.top).toBeCloseTo(8, 0);
      await page.screenshot({ path: info.outputPath(`list-${width}.png`), animations: "disabled" });

      await workflow.click();
      const editor = page.locator(".wf-editor");
      await expect(editor).toBeVisible();
      const after = await navigationGeometry(page);
      expect(after).toEqual(before);
      const geometry = await editor.evaluate(element => {
        const content = element.closest(".content")!.getBoundingClientRect();
        const editor = element.getBoundingClientRect();
        return { contentLeft: content.left, contentRight: content.right, editorLeft: editor.left, editorRight: editor.right, top: editor.top };
      });
      expect(geometry.editorLeft).toBeCloseTo(geometry.contentLeft, 0);
      expect(geometry.editorRight).toBeCloseTo(geometry.contentRight, 0);
      expect(geometry.top - after.bottom).toBeCloseTo(12, 0);
      await page.screenshot({ path: info.outputPath(`editor-${width}.png`), animations: "disabled" });

      await views.getByRole("link", { name: "Definitions", exact: true }).click();
      await expect(definitions).toBeVisible();
      expect(await navigationGeometry(page)).toEqual(before);
    }
  });
}
