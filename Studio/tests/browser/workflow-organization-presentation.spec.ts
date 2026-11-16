import axe from "axe-core";
import { expect, test, type Page } from "@playwright/test";

for (const minimal of [false, true]) for (const dark of [false, true]) for (const width of [1280, 320]) {
  test(`separate organization actions and chip input: ${minimal ? "Minimal" : "Classic"} ${dark ? "dark" : "light"} ${width}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    if (minimal) {
      await page.goto("/minimal-studio.html?view=appearance");
      await page.getByRole("radiogroup", { name: "Studio preset", exact: true }).getByRole("radio", { name: dark ? "Graphite" : "Atelier", exact: true }).click();
    }
    const query = `organization-surface=properties&tags=empty&theme=${dark ? "dark" : "light"}${minimal ? "" : "&palette=classic"}`;
    await page.goto(minimal ? `/minimal-studio.html?view=library&${query}` : `/?mode=workflow-library&${query}`);
    const entry = page.getByRole("region", { name: "Workflow organization", exact: true });
    await expect(entry.getByRole("button", { name: "Move to folder", exact: true })).toBeVisible();
    const edit = entry.getByRole("button", { name: "Edit tags", exact: true });
    await edit.click();
    const dialog = page.getByRole("dialog", { name: "Tags for Welcome new customer", exact: true });
    await expect(dialog.getByRole("button", { name: "Move to folder", exact: true })).toHaveCount(0);
    const input = dialog.getByRole("combobox", { name: "Tags", exact: true });
    await input.fill("Customer-facing");
    await dialog.getByRole("option", { name: "Create “Customer-facing”", exact: true }).click();
    await expect(dialog.getByRole("button", { name: "Remove Customer-facing", exact: true })).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeEnabled();
    const box = (await dialog.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(width);
    expect(box.y + box.height).toBeLessThanOrEqual(900);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await expect(dialog.getByText("Finding tags…", { exact: true })).toHaveCount(0);
    expect(await seriousViolations(page)).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath("tag-chips.png") });
    await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(edit).toBeFocused();
    await edit.click();
    await expect(dialog.getByRole("button", { name: "Remove Customer-facing", exact: true })).toBeVisible();
    await dialog.getByRole("button", { name: "Close", exact: true }).click();
    await entry.getByRole("button", { name: "Move to folder", exact: true }).click();
    const move = page.getByRole("dialog", { name: "Move workflow definitions", exact: true });
    await move.getByRole("radio", { name: "Finance", exact: true }).check();
    await move.getByRole("button", { name: "Move", exact: true }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });
}

for (const width of [1280, 320]) test(`tag suggestions stay bounded with long content at ${width}`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width, height: 740 });
  await page.goto("/?mode=workflow-library&tags=many&long-folder=1&theme=dark&palette=classic");
  await expect(page.getByRole("link", { name: "Collect overdue payment", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Manage tags", exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("workflow-library.png") });
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment", exact: true });
  const input = dialog.getByRole("combobox", { name: "Tags", exact: true });
  await input.click();
  await expect(dialog.getByRole("option", { name: "Additional classification 40", exact: true })).toHaveCount(1);
  await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeInViewport();
  await expect(dialog.getByRole("button", { name: "Remove Owning team: Billing", exact: true })).toBeVisible();
  await input.fill("Critical");
  await expect(dialog.getByRole("option", { name: "Critical", exact: true })).toBeVisible();
  await input.press("Enter");
  await expect(dialog.getByRole("button", { name: "Remove Critical", exact: true })).toBeVisible();
  await expect(dialog.getByRole("option", { name: "Critical", exact: true })).toHaveCount(0);
  await expect(dialog.getByText("Finding tags…", { exact: true })).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath("tag-suggestions.png") });
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog).toBeHidden();
});

test("tag chips support replacement, keyboard removal and cancellation without writes", async ({ page }) => {
  await page.goto("/?mode=workflow-library");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment", exact: true });
  const input = dialog.getByRole("combobox", { name: "Tags", exact: true });
  await input.fill("Owning team");
  await dialog.getByRole("option", { name: "Owning team: Support", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Remove Owning team: Billing", exact: true })).toHaveCount(0);
  await expect(dialog.getByRole("button", { name: "Remove Owning team: Support", exact: true })).toBeVisible();
  await input.press("Backspace");
  await expect(dialog.getByRole("button", { name: "Remove Owning team: Support", exact: true })).toHaveCount(0);
  await input.fill("critical");
  await expect(dialog.getByRole("option", { name: "Critical", exact: true })).toBeVisible();
  await expect(dialog.getByRole("option", { name: "Create “critical”", exact: true })).toHaveCount(0);
  await input.press("Enter");
  await input.press("Escape");
  await expect(dialog).toBeVisible();
  await input.press("Escape");
  await expect(dialog).toBeHidden();
  expect(await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string }[] }).workflowLibraryRequests.filter(request => request.method !== "GET"))).toEqual([]);
});


test("inline tag creation refreshes the library labels after saving", async ({ page }) => {
  await page.goto("/?mode=workflow-library&columns=tags");
  await page.getByRole("button", { name: "Actions for Welcome new customer" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Welcome new customer", exact: true });
  await dialog.getByRole("combobox", { name: "Tags", exact: true }).fill("Customer-facing");
  await dialog.getByRole("option", { name: "Create “Customer-facing”", exact: true }).click();
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("row", { name: "Open workflow definition Welcome new customer", exact: true }).getByRole("cell", { name: "Customer-facing", exact: true })).toBeVisible();
});

test("tag assignment permission does not grant catalog creation", async ({ page }) => {
  await page.goto("/?mode=workflow-library&permissions=assign");
  await page.getByRole("button", { name: "Actions for Welcome new customer" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Welcome new customer", exact: true });
  const input = dialog.getByRole("combobox", { name: "Tags", exact: true });
  await input.fill("New vocabulary");
  await expect(dialog.getByText("No matching tags available", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("option")).toHaveCount(0);
  await input.press("Enter");
  await input.fill("Critical");
  await dialog.getByRole("option", { name: "Critical", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Remove Critical", exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: "Save tags", exact: true }).click();
  expect(await page.evaluate(() => (window as unknown as { workflowLibraryRequests: { method: string }[] }).workflowLibraryRequests.filter(request => request.method === "POST"))).toEqual([]);
});

test("failed inline creation retains existing chips and allows recovery", async ({ page }) => {
  await page.goto("/?mode=workflow-library&tag-create=reject");
  await page.getByRole("button", { name: "Actions for Collect overdue payment" }).click();
  await page.getByRole("menuitem", { name: "Edit tags…", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Tags for Collect overdue payment", exact: true });
  await dialog.getByRole("combobox", { name: "Tags", exact: true }).fill("Concurrent label");
  await dialog.getByRole("option", { name: "Create “Concurrent label”", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("409");
  await expect(dialog.getByRole("button", { name: "Remove Owning team: Billing", exact: true })).toBeVisible();
  await expect(dialog.getByRole("combobox", { name: "Tags", exact: true })).toHaveValue("Concurrent label");
  await expect(dialog.getByRole("button", { name: "Save tags", exact: true })).toBeDisabled();
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await expect(dialog).toBeHidden();
});

async function seriousViolations(page: Page) {
  await page.addScriptTag({ content: axe.source });
  return page.evaluate(async () => {
    const result = await (window as Window & { axe: { run(root: Element): Promise<{ violations: { id: string; impact: string }[] }> } }).axe.run(document.querySelector("[role='dialog']")!);
    return result.violations.filter(violation => violation.impact === "critical" || violation.impact === "serious").map(violation => violation.id);
  });
}
