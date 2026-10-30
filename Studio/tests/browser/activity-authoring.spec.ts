import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`activity variables have a direct tab and share the root inspector editor (${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    const views = page.getByRole("tablist", { name: "Activity Definition authoring views" });
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    const variables = page.getByRole("tabpanel", { name: "Variables", exact: true });
    await expect(variables.getByText("Private to each execution of this activity and available throughout its designer.")).toBeVisible();
    await variables.getByRole("button", { name: "Add variable", exact: true }).click();
    const storage = variables.getByRole("combobox", { name: "Variable storage driver", exact: true });
    await expect(storage).toBeEnabled();
    expect(await storage.locator("option").evaluateAll(options => options.map(option => (option as HTMLOptionElement).value))).toEqual(["", "elsa.json"]);
    await storage.selectOption("elsa.json");
    await variables.getByRole("textbox", { name: "Variable name", exact: true }).fill("LineCount");
    await variables.getByRole("textbox", { name: "Variable default value", exact: true }).fill("0");
    await expect(page.locator(".ad-revision-gate")).toHaveCount(0);
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    const fieldset = variables.locator("fieldset.wf-container-variables");
    expect(await fieldset.evaluate(element => getComputedStyle(element).borderWidth)).toBe("0px");
    await page.screenshot({ path: testInfo.outputPath("activity-variables.png") });

    await views.getByRole("tab", { name: "Designer", exact: true }).click();
    const inspector = page.getByRole("complementary", { name: "Inspector panel", exact: true });
    await inspector.getByRole("tab", { name: "Variables", exact: true }).click();
    await expect(inspector.getByRole("textbox", { name: "Variable name", exact: true })).toHaveValue("LineCount");
    await expect(inspector.getByRole("textbox", { name: "Variable default value", exact: true })).toHaveValue("0");
    expect(await inspector.locator("fieldset.wf-container-variables").evaluate(element => getComputedStyle(element).borderWidth)).toBe("0px");
    await inspector.getByRole("button", { name: "Add variable", exact: true }).click();
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    await page.reload();
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    await expect(variables.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(2);
    await expect(variables.getByRole("textbox", { name: "Variable name", exact: true }).first()).toHaveValue("LineCount");

    await page.locator(".ad-authoring-advanced > summary").click();
    await page.getByRole("button", { name: "Edit code", exact: true }).click();
    const projection = JSON.parse(await page.getByRole("textbox", { name: "Activity Definition JSON" }).inputValue());
    expect(projection.implementation.payload.variables[0]).toMatchObject({ name: "LineCount", storageDriverKey: "elsa.json", initialValue: { syntax: "Literal", value: "0" } });
    expect(projection.contract.inputs.map((input: { name: string }) => input.name)).toEqual(["Subtotal", "Shipping"]);
    await expect(page.getByRole("tab", { name: "Code", exact: true })).toBeFocused();

    await page.setViewportSize({ width: 390, height: 844 });
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}

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

for (const theme of ["light", "dark", "material"]) for (const width of [390, 1440]) {
  test(`Add variable has shared themed hover, focus and disabled styling (${theme}, ${width})`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 });
    await page.goto(`/?mode=activity-authoring&theme=${theme === "light" ? "light" : "dark"}`);
    await page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: "Variables", exact: true }).click();
    if (theme === "material") await page.evaluate(() => {
      document.documentElement.dataset.themeMaterial = "browser-fixture";
      document.documentElement.style.setProperty("--studio-surface", "var(--studio-accent-soft)");
      document.documentElement.style.setProperty("--studio-border", "var(--studio-accent)");
    });
    const panel = page.getByRole("tabpanel", { name: "Variables", exact: true });
    const add = panel.getByRole("button", { name: "Add variable", exact: true });
    const bounds = (await add.boundingBox())!;
    expect(bounds.height).toBeGreaterThanOrEqual(30);
    expect(bounds.width).toBeGreaterThanOrEqual(95);
    const normal = await add.evaluate(element => ({ color: getComputedStyle(element).color, background: getComputedStyle(element).backgroundColor }));
    await add.hover();
    expect(await add.evaluate(element => getComputedStyle(element).color)).not.toBe(normal.color);
    await add.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(add).toBeFocused();
    expect(await add.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("solid");
    expect(await add.evaluate(element => parseFloat(getComputedStyle(element).outlineWidth))).toBeGreaterThanOrEqual(2);
    await page.screenshot({ path: `/tmp/argument-button-${theme}-${width}.png` });
    await add.click();
    await expect(panel.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(1);
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    // The shared editor also runs inside disabled fieldsets in read-only views.
    await panel.locator("fieldset.wf-container-variables").evaluate(element => { (element as HTMLFieldSetElement).disabled = true; });
    await expect(add).toBeDisabled();
    await expect.poll(() => add.evaluate(element => parseFloat(getComputedStyle(element).opacity))).toBeLessThan(1);
    expect(await add.evaluate(element => getComputedStyle(element).cursor)).toBe("not-allowed");
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(0);
  });
}
