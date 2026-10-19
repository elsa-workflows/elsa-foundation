import { expect, test } from "@playwright/test";

test("edits a reusable activity interface without losing unapplied defaults or saved bindings", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&theme=dark");
  await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
  await expect(page.getByText("Advanced interface details", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Stable reference key" })).toHaveCount(0);
  await page.getByRole("button", { name: "Add input", exact: true }).click();
  await page.getByRole("textbox", { name: "Input name", exact: true }).fill("Discount");
  await page.getByRole("textbox", { name: "Input name", exact: true }).press("Enter");
  const member = page.getByRole("region", { name: "Discount", exact: true });
  await expect(member.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
  await member.getByRole("combobox", { name: "Type", exact: true }).selectOption("Decimal");
  await member.getByRole("combobox", { name: /^Default/ }).selectOption("literal");
  await member.getByRole("textbox", { name: "Value", exact: true }).fill("2.5");
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Shipping Number", exact: true }).click();
  await expect(member).toBeVisible();
  await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveValue("2.5");
  await member.getByRole("textbox", { name: "Value", exact: true }).fill("2x");
  await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveAttribute("aria-invalid", "true");
  await expect(member.getByRole("button", { name: "Apply default", exact: true })).toBeDisabled();
  await member.getByRole("textbox", { name: "Value", exact: true }).fill("2.5");
  await member.getByRole("button", { name: "Apply default", exact: true }).click();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await page.reload();
  await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
  await page.getByRole("button", { name: "Discount Number", exact: true }).click();
  await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveValue("2.5");
  await page.locator(".ad-authoring-advanced > summary").click();
  await page.getByRole("button", { name: "Edit code", exact: true }).click();
  const projection = JSON.parse(await page.getByRole("textbox", { name: "Activity Definition JSON" }).inputValue());
  expect(projection.contract.inputs.find((input: { referenceKey: string }) => input.referenceKey === "discount").default).toEqual({ syntax: "Literal", value: 2.5 });
  expect(projection.implementation.payload.outputMappings).toEqual([{ outputReferenceKey: "amount", source: { syntax: "Input", value: { referenceKey: "subtotal" } } }]);
  expect(projection.implementation.payload.outcomeMappings).toEqual([{ sourceOutcomeReferenceKey: "done", boundaryOutcomeReferenceKey: "quoted" }]);
});

for (const theme of ["light", "dark"]) {
  test(`keeps focused interface editing inside a narrow ${theme} viewport`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
    await expect(page.getByRole("region", { name: "Subtotal", exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByRole("button", { name: "Add output", exact: true }).click();
    await expect(page.getByRole("textbox", { name: "Output name", exact: true })).toBeFocused();
    await page.getByRole("textbox", { name: "Output name", exact: true }).press("Escape");
    await expect(page.getByRole("button", { name: "Add output", exact: true })).toBeFocused();
  });
}

test("keeps existing members visible without misclassifying a capability outage", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&theme=dark&capabilities=unavailable");
  await page.getByRole("tab", { name: "Inputs & outputs", exact: true }).click();
  await expect(page.getByText("Contract options unavailable", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Subtotal", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Add input", exact: true })).toBeDisabled();
  await expect(page.getByText(/This mutable draft is invalid until/)).toHaveCount(0);
});
