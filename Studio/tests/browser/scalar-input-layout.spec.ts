import { expect, test } from "@playwright/test";

for (const [theme, width] of [["light", 900], ["dark", 900], ["dark", 320]] as const) {
  test(`aligns scalar inputs and syntax pickers horizontally (${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=scalar-inputs&theme=${theme}`);

    for (const name of ["Policy reference", "Proposed premium", "Renewal count", "Rate", "Duration", "Priority", "Identifier"]) {
      const field = page.getByLabel(name, { exact: true });
      const picker = page.getByRole("button", { name: `${name} expression syntax`, exact: true });
      await field.scrollIntoViewIfNeeded();
      const fieldBox = (await field.boundingBox())!;
      const pickerBox = (await picker.boundingBox())!;
      expect(Math.abs(fieldBox.y + fieldBox.height / 2 - pickerBox.y - pickerBox.height / 2), `${name}: ${JSON.stringify({ fieldBox, pickerBox })}`).toBeLessThanOrEqual(1);
      expect(pickerBox.x).toBeGreaterThan(fieldBox.x);
      expect(pickerBox.x + pickerBox.width).toBeLessThanOrEqual(width - 20);
      if (name !== "Policy reference") expect(pickerBox.x).toBeGreaterThanOrEqual(fieldBox.x + fieldBox.width - 1);
    }

    const notesRow = page.locator(".wf-property-row").filter({ has: page.getByLabel("Notes", { exact: true }) });
    await expect(notesRow.locator(".wf-expression-field")).toHaveCount(0);
    const referencesRow = page.locator(".wf-property-row").filter({ has: page.getByText("References", { exact: true }) });
    await expect(referencesRow.locator(".wf-expression-field")).toHaveCount(0);
  });
}

test("keeps numeric validation below the input without stretching its syntax picker", async ({ page }) => {
  await page.goto("/?mode=scalar-inputs&theme=dark");
  const field = page.getByLabel("Renewal count", { exact: true });
  const picker = page.getByRole("button", { name: "Renewal count expression syntax" });
  const before = await picker.boundingBox();
  await field.fill("1.5");
  await expect(field).toHaveAttribute("aria-invalid", "true");
  await expect(page.getByRole("alert")).toContainText("Enter a whole number");
  expect(await picker.boundingBox()).toEqual(before);
  await field.fill("2");
  await expect(field).not.toHaveAttribute("aria-invalid", "true");
  await expect(page.getByRole("alert")).toHaveCount(0);
  await picker.click();
  await page.getByRole("option", { name: "JavaScript", exact: true }).click();
  await expect(picker).toHaveText("JavaScript");
  await expect(page.getByLabel("Renewal count expression", { exact: true })).toHaveValue("2");
  await expect(page.getByRole("button", { name: "Open expanded Renewal count editor" })).toBeVisible();
});
