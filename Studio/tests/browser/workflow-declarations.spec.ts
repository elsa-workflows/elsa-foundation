import { expect, test, type Locator } from "@playwright/test";
import { measureDeclarationControl } from "../declarationReadability";

const surfaces = [
  { region: "Workflow inputs", prefix: "Input", storage: true },
  { region: "Workflow outputs", prefix: "Output", storage: false },
  { region: "Workflow variables", prefix: "Variable", storage: true },
  { region: "Scoped variables", prefix: "Variable", storage: true }
];
const sizes = [
  { name: "360", viewport: 360, pane: 244 },
  { name: "390", viewport: 390, pane: 274 },
  { name: "constrained-desktop", viewport: 1280, pane: 244 },
  { name: "wide", viewport: 1440, pane: 1000 }
];
async function readable(control: Locator) {
  await control.scrollIntoViewIfNeeded();
  await expect(control).toBeInViewport({ ratio: 1 });
  await expect.poll(async () => (await control.evaluate(measureDeclarationControl)).fits).toBe(true);
  return control.evaluate(measureDeclarationControl);
}

for (const theme of ["light", "dark"]) {
  for (const size of sizes) test(`${theme} declaration editors retain readable values in ${size.name} panes`, async ({ page }, info) => {
    await page.setViewportSize({ width: size.viewport, height: 1000 });
    await page.goto(`/workflow-declarations.html?theme=${theme}&paneWidth=${size.pane}`);
    for (const surface of surfaces) {
      const region = page.getByRole("region", { name: surface.region, exact: true });
      const name = region.getByLabel(`${surface.prefix} name`, { exact: true });
      const type = region.getByRole("combobox", { name: `${surface.prefix} type`, exact: true });
      await expect(type).toHaveValue("System.Text.Json.JsonElement");
      const values = { name: await readable(name), type: await readable(type), collection: await readable(region.getByLabel(`${surface.prefix} collection kind`, { exact: true })) };
      await name.focus();
      await page.keyboard.press("Tab"); await expect(type).toBeFocused();
      await name.scrollIntoViewIfNeeded();
      await info.attach(`${surface.region}-top.png`, { body: await region.screenshot(), contentType: "image/png" });
      if (surface.storage) {
        const storage = region.getByLabel(`${surface.prefix} storage driver`, { exact: true });
        await expect(storage).toBeEnabled();
        await expect(storage).toHaveValue("elsa.json");
        await readable(storage);
        await info.attach(`${surface.region}-storage.png`, { body: await region.screenshot(), contentType: "image/png" });
      }
      await info.attach(`${surface.region}-geometry`, { body: JSON.stringify(values), contentType: "application/json" });
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  });

  test(`${theme} declaration reflow has a faithful disabled-container control and preserves unavailable selections`, async ({ page }, info) => {
    await page.setViewportSize({ width: 360, height: 1000 });
    await page.goto(`/workflow-declarations.html?theme=${theme}&paneWidth=244`);
    const output = page.getByRole("region", { name: "Workflow outputs", exact: true });
    const name = output.getByLabel("Output name", { exact: true });
    await expect(output.getByRole("combobox", { name: "Output type", exact: true })).toHaveValue("System.Text.Json.JsonElement");
    const positive = await readable(name);
    // Removing only the production size-container trigger restores the original compressed table.
    const mutation = await page.addStyleTag({ content: ".wf-properties-section { container-type: normal !important; }" });
    let negative;
    try {
      await expect.poll(async () => (await name.evaluate(measureDeclarationControl)).fits).toBe(false);
      negative = await name.evaluate(measureDeclarationControl);
    } finally {
      await mutation.evaluate(element => {
        if (!element.parentNode) throw new Error("The owned control stylesheet is missing");
        element.parentNode.removeChild(element);
      });
    }
    const restored = await readable(name);
    const variables = page.getByRole("region", { name: "Workflow variables", exact: true });
    const jsonDefault = variables.getByLabel("Variable default value", { exact: true });
    await jsonDefault.fill("{");
    await expect(jsonDefault).toHaveAttribute("aria-invalid", "true");
    await expect(variables.getByRole("alert")).toHaveText("Enter a valid JSON value.");
    await info.attach("variable-default-error.png", { body: await variables.screenshot(), contentType: "image/png" });
    await jsonDefault.fill('{"approved":false}');
    await expect(jsonDefault).not.toHaveAttribute("aria-invalid", "true");
    await name.fill("RenamedData");
    await page.getByRole("button", { name: "Remount fixture editors", exact: true }).click();
    await expect(name).toHaveValue("RenamedData");
    await expect(jsonDefault).toHaveValue('{"approved":false}');
    const inputs = page.getByRole("region", { name: "Workflow inputs", exact: true });
    await inputs.getByLabel("Input required", { exact: true }).check();
    await inputs.getByRole("button", { name: "Add input", exact: true }).click();
    await expect(inputs.getByLabel("Input name", { exact: true })).toHaveCount(2);
    await inputs.getByRole("button", { name: "Remove input Input1", exact: true }).click();
    await expect(inputs.getByLabel("Input name", { exact: true })).toHaveCount(1);
    await page.getByRole("button", { name: "Remount fixture editors", exact: true }).click();
    await expect(inputs.getByLabel("Input required", { exact: true })).toBeChecked();
    await info.attach("reflow-disabled-negative-and-restored", { body: JSON.stringify({ positive, negative, restored }), contentType: "application/json" });

    await page.goto(`/workflow-declarations.html?theme=${theme}&paneWidth=244&catalog=offline&readOnly=1`);
    for (const surface of surfaces) {
      const region = page.getByRole("region", { name: surface.region, exact: true });
      const type = region.getByLabel(`${surface.prefix} type`, { exact: true });
      await expect(type).toHaveValue("Legacy.Json");
      await readable(type);
      if (surface.storage) {
        const storage = region.getByLabel(`${surface.prefix} storage driver`, { exact: true });
        await expect(storage).toBeDisabled(); await expect(storage).toHaveValue("old");
        await expect(storage.locator("option").first()).toHaveText("Storage unavailable");
        await readable(storage);
      }
      await info.attach(`${surface.region}-unavailable.png`, { body: await region.screenshot(), contentType: "image/png" });
    }
    await expect(page.getByRole("region", { name: "Scoped variables", exact: true }).getByLabel("Variable name", { exact: true })).toBeDisabled();
  });
}
