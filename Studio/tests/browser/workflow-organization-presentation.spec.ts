import { expect, test } from "@playwright/test";

for (const minimal of [false, true]) for (const dark of [false, true]) for (const width of [1280, 320]) {
  test(`organization has a structured entry and dialog: ${minimal ? "Minimal" : "Classic"} ${dark ? "dark" : "light"} ${width}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    if (minimal) {
      await page.goto("/minimal-studio.html?view=appearance");
      await page.getByRole("radiogroup", { name: "Studio preset", exact: true }).getByRole("radio", { name: dark ? "Graphite" : "Atelier", exact: true }).click();
    }
    const query = `organization-surface=properties&tags=empty&theme=${dark ? "dark" : "light"}${minimal ? "" : "&palette=classic"}`;
    await page.goto(minimal ? `/minimal-studio.html?view=library&${query}` : `/?mode=workflow-library&${query}`);
    if (minimal) await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
    else await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", dark ? "dark" : "light");
    const entry = page.getByRole("region", { name: "Workflow organization", exact: true });
    // A compact action and explanatory heading replace the stretched bare button.
    await expect(entry.getByRole("heading", { name: "Organization", exact: true })).toBeVisible();
    const organize = entry.getByRole("button", { name: "Organize", exact: true });
    const entryBox = await entry.boundingBox();
    const buttonBox = await organize.boundingBox();
    expect(buttonBox!.width).toBeLessThan(180);
    expect(entryBox!.height).toBeGreaterThan(60);
    await organize.click();
    const dialog = page.getByRole("dialog", { name: "Organize Welcome new customer", exact: true });
    await expect(dialog.getByRole("region", { name: "Home folder", exact: true })).toContainText("Unfiled");
    await expect(dialog.getByText("No tags available", { exact: true })).toBeVisible();
    await expect(dialog).not.toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
    await page.screenshot({ path: testInfo.outputPath("organization-empty.png") });
    const search = dialog.getByRole("searchbox", { name: "Find workflow tags" });
    const find = dialog.getByRole("button", { name: "Find", exact: true });
    const searchBox = (await search.boundingBox())!;
    const findBox = (await find.boundingBox())!;
    expect(Math.abs(searchBox.y + searchBox.height - findBox.y - findBox.height)).toBeLessThanOrEqual(1);
    if (width === 1280) expect(searchBox.width).toBeGreaterThan(350);
    const reload = dialog.getByRole("button", { name: "Reload organization", exact: true });
    const close = dialog.getByRole("button", { name: "Close", exact: true });
    expect((await reload.boundingBox())!.width).toBeLessThan(220);
    await expect(dialog.getByRole("button", { name: "Save tags" })).toBeDisabled();
    const box = (await dialog.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(width);
    expect(box.y + box.height).toBeLessThanOrEqual(900);
    await close.focus();
    await page.keyboard.press("Tab");
    const move = dialog.getByRole("button", { name: "Move to folder", exact: true });
    await expect(move).toBeFocused();
    // An independently themed ring must remain the shared button's focus colour.
    await dialog.evaluate(element => (element as HTMLElement).style.setProperty("--studio-focus", "rgb(123, 234, 45)"));
    await expect(move).toHaveCSS("outline-color", "rgb(123, 234, 45)");
    await dialog.evaluate(element => (element as HTMLElement).style.removeProperty("--studio-focus"));
    await search.fill("missing");
    await find.click();
    await expect(dialog.getByText("No matching tags", { exact: true })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("organization.png") });
    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
    await expect(organize).toBeFocused();
  });
}

for (const width of [1280, 320]) test(`populated organization keeps its footer accessible with long content at ${width}`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width, height: 740 });
  await page.goto("/?mode=workflow-library&tags=many&long-folder=1&theme=dark&palette=classic");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Organize Collect overdue payment", exact: true });
  await expect(dialog.getByRole("checkbox", { name: "Additional classification 40", exact: true })).toHaveCount(1);
  await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeInViewport();
  await expect(dialog.getByRole("region", { name: "Home folder", exact: true })).toContainText("Customer operations");
  await dialog.getByRole("checkbox", { name: "Critical", exact: true }).check();
  const scroll = dialog.locator(".wf-organization-scroll");
  await scroll.evaluate(element => element.scrollTop = 0);
  await expect(dialog.getByRole("button", { name: "Move to folder", exact: true })).toBeDisabled();
  const save = dialog.getByRole("button", { name: "Save tags", exact: true });
  await expect(save).toBeEnabled();
  const box = (await dialog.boundingBox())!;
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(width);
  expect(box.y + box.height).toBeLessThanOrEqual(740);
  await page.screenshot({ path: testInfo.outputPath("organization-populated.png") });
  await save.click();
  await expect(dialog).toBeHidden();
});
