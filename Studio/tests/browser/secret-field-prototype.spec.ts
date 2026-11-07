import { expect, test, type Page, type TestInfo } from "@playwright/test";
import axe from "axe-core";
import { builtInThemeDefinitions, getSupportedThemeModes } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";

async function appearance(page: Page, theme: string, mode: string, workspace?: string) {
  await page.addInitScript(({ theme, mode, workspace }) => {
    localStorage.clear();
    localStorage.setItem("elsa-studio-theme", theme);
    localStorage.setItem("elsa-studio-theme-mode", mode);
    if (workspace) localStorage.setItem("elsa-studio-workspace-style", workspace);
  }, { theme, mode, workspace });
}

async function capture(page: Page, info: TestInfo, name: string) {
  await page.waitForFunction(() => document.fonts.status === "loaded", undefined, { timeout: 10_000 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  const path = info.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true, animations: "disabled", timeout: 10_000 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await info.attach(name, { path, contentType: "image/png" });
}

async function namesAndContrast(page: Page, selector: string) {
  // Use the same finite-animation preparation as the retained screenshots before auditing paint.
  await page.screenshot({ animations: "disabled", timeout: 10_000 });
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async selector => (window as unknown as Window & { axe: typeof axe }).axe.run(document.querySelector(selector)!, {
    runOnly: ["color-contrast", "label", "button-name", "label-content-name-mismatch", "aria-valid-attr-value"]
  }).then(result => result.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => ({ target: n.target, summary: n.failureSummary })) }))), selector);
  expect(violations).toEqual([]);
}

for (const fieldStyle of ["filled", "outlined"] as const) test.describe(fieldStyle, () => {

for (const theme of builtInThemeDefinitions) for (const mode of getSupportedThemeModes(theme)) {
  test(`${theme.id} / ${mode}: labels, states and Create secret`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await appearance(page, theme.id, mode);
    await page.goto(`/secret-field-prototype.html?view=states&field-style=${fieldStyle}`);
    await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", mode);
    await expect(page.getByLabel("Preview theme")).toHaveValue(theme.id);
    await expect(page.locator(".secret-prototype")).toHaveAttribute("data-field-style", fieldStyle);
    await expect(page.getByLabel("Disabled field", { exact: true })).toBeDisabled();
    await expect(page.getByLabel("Read-only field", { exact: true })).toHaveAttribute("readonly", "");
    await expect(page.getByLabel("Invalid field", { exact: true })).toHaveAttribute("aria-invalid", "true");
    const heights = await page.getByRole("region", { name: "Representative field states" }).evaluate(element => ["Populated field", "Invalid field", "Disabled field"].map(name => {
      const label = [...element.querySelectorAll("label")].find(item => item.textContent === name)!;
      return label.parentElement!.getBoundingClientRect().height;
    }));
    expect(Math.max(...heights) - Math.min(...heights)).toBeLessThanOrEqual(2);
    await namesAndContrast(page, ".secret-prototype-states");
    await capture(page, info, "field-states");
    await page.getByRole("button", { name: "Open Create secret", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
    const name = dialog.getByLabel("Technical name", { exact: true });
    await expect(name).toBeFocused();
    await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
    await capture(page, info, "empty-focused");
    await name.fill("invalid name");
    await expect(name).toHaveAttribute("aria-invalid", "true");
    await expect(name).toHaveAccessibleDescription("Use letters, numbers, dot, dash, underscore, or colon.");
    await name.fill("billing.connection");
    await expect(name).not.toHaveAttribute("aria-invalid");
    await dialog.getByLabel("Value", { exact: true }).fill("demonstration-value");
    await dialog.getByLabel("Expires", { exact: true }).fill("2030-01-02T03:04");
    await namesAndContrast(page, ".secrets-create-dialog");
    const geometry = await name.evaluate(element => {
      const input = element.getBoundingClientRect();
      const label = (element as HTMLInputElement).labels![0].getBoundingClientRect();
      return { labelTop: label.top - input.top, labelBottom: label.bottom - input.top, height: input.height };
    });
    if (fieldStyle === "outlined") {
      const notch = await name.evaluate(element => {
        const control = element.parentElement!.getBoundingClientRect();
        const label = (element as HTMLInputElement).labels![0].getBoundingClientRect();
        return { labelCenter: (label.top + label.bottom) / 2, borderTop: control.top, labelBottom: label.bottom, inputTop: element.getBoundingClientRect().top };
      });
      expect(Math.abs(notch.labelCenter - notch.borderTop)).toBeLessThanOrEqual(2);
      expect(notch.labelBottom).toBeLessThanOrEqual(notch.inputTop + 1);
    }
    expect(geometry.labelTop).toBeLessThan(10);
    expect(geometry.labelBottom).toBeLessThanOrEqual(24);
    await expect(dialog.getByLabel("Type", { exact: true })).not.toHaveCSS("background-image", "none");
    await info.attach("field-geometry", { body: JSON.stringify(geometry), contentType: "application/json" });
    await capture(page, info, "populated");
    await dialog.getByRole("button", { name: /^More options/ }).click();
    await dialog.getByLabel("Display name", { exact: true }).fill("Billing connection");
    await dialog.getByLabel("Description", { exact: true }).fill("Credentials used by billing workflows.");
    await dialog.getByLabel("Tags", { exact: true }).fill("billing, production");
    await dialog.getByLabel("Scope", { exact: true }).selectOption("Integration:Billing");
    await dialog.getByLabel("Scope", { exact: true }).scrollIntoViewIfNeeded();
    await capture(page, info, "optional-fields");
  });
}

for (const workspace of ["linen", "minimal", "quiet", "focus", "open"]) test(`${workspace}: narrow layout and enlarged text`, async ({ page }, info) => {
  await appearance(page, "classic", "dark", workspace);
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto(`/secret-field-prototype.html?open=1&field-style=${fieldStyle}`);
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  await dialog.getByRole("button", { name: /^More options/ }).click();
  const before = await dialog.getByLabel("Technical name", { exact: true }).evaluate(element => parseFloat(getComputedStyle(element).fontSize));
  await page.evaluate(before => {
    document.documentElement.style.setProperty("--studio-body-size", `${before * 2}px`);
    document.documentElement.style.setProperty("--studio-label-size", `${before * 2}px`);
  }, before);
  await expect.poll(() => dialog.getByLabel("Technical name", { exact: true }).evaluate(element => parseFloat(getComputedStyle(element).fontSize))).toBeCloseTo(before * 2, 0);
  await dialog.getByLabel("Technical name", { exact: true }).fill("enlarged.field");
  await page.screenshot({ animations: "disabled", timeout: 10_000 });
  const separation = await dialog.getByLabel("Technical name", { exact: true }).evaluate(element => {
    const input = element as HTMLInputElement;
    const style = getComputedStyle(input);
    const label = input.labels![0].getBoundingClientRect();
    return { labelBottom: label.bottom, valueTop: input.getBoundingClientRect().top + parseFloat(style.paddingTop), labelSize: parseFloat(getComputedStyle(input.labels![0]).fontSize) };
  });
  expect(separation.labelSize).toBeGreaterThanOrEqual(before * 1.8);
  expect(separation.labelBottom).toBeLessThanOrEqual(separation.valueTop);
  await dialog.getByLabel("Description", { exact: true }).fill("A long description\n".repeat(15));
  await dialog.getByLabel("Scope", { exact: true }).selectOption({ label: "Custom…" });
  await dialog.getByLabel("Custom scope value", { exact: true }).fill("Team:Billing");
  const clipped = await dialog.locator(".secrets-compact-control > label").evaluateAll(labels => labels.filter(label => label.scrollWidth > label.clientWidth + 1).map(label => label.textContent));
  expect(clipped).toEqual([]);
  await dialog.getByRole("button", { name: "Cancel", exact: true }).scrollIntoViewIfNeeded();
  await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeInViewport();
  await capture(page, info, "narrow-enlarged");
});

test("password, multiline and configuration sources preserve labels and draft recovery", async ({ page }, info) => {
  await appearance(page, "classic", "dark");
  await page.goto(`/secret-field-prototype.html?save=error&catalog=error&field-style=${fieldStyle}`);
  await page.getByRole("button", { name: "Open Create secret", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  await dialog.getByLabel("Technical name", { exact: true }).fill("billing.connection");
  await dialog.getByLabel("Value", { exact: true }).fill("demonstration-value");
  await dialog.getByRole("button", { name: "Show value", exact: true }).click();
  await expect(dialog.getByLabel("Value", { exact: true })).toHaveAttribute("type", "text");
  await dialog.getByRole("button", { name: "Hide value", exact: true }).click();
  await capture(page, info, "scope-error");
  await dialog.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  await expect(dialog.getByRole("status")).toContainText("Loading scope suggestions");
  await expect(dialog.getByRole("button", { name: "Refresh scope suggestions", exact: true })).toBeEnabled();
  await dialog.getByLabel("Type", { exact: true }).selectOption("rsa-key");
  await expect(dialog.getByLabel("Value", { exact: true })).toHaveJSProperty("tagName", "TEXTAREA");
  await dialog.getByLabel("Value", { exact: true }).fill("DEMONSTRATION KEY\n".repeat(12));
  const multiline = dialog.getByLabel("Value", { exact: true });
  await multiline.locator("..").locator("..").scrollIntoViewIfNeeded();
  const labelCover = await multiline.evaluate(element => {
    const textarea = element as HTMLTextAreaElement;
    textarea.scrollTop = textarea.scrollHeight;
    const label = textarea.labels![0].getBoundingClientRect();
    const viewport = textarea.closest(".secrets-create-content")!.getBoundingClientRect();
    return { labelTop: label.top - textarea.getBoundingClientRect().top, absoluteTop: label.top, absoluteBottom: label.bottom, viewportTop: viewport.top, viewportBottom: viewport.bottom, scrollTop: textarea.scrollTop };
  });
  expect(labelCover.scrollTop).toBeGreaterThan(0);
  expect(labelCover.labelTop).toBeLessThanOrEqual(2);
  expect(labelCover.absoluteTop).toBeGreaterThanOrEqual(labelCover.viewportTop);
  expect(labelCover.absoluteBottom).toBeLessThanOrEqual(labelCover.viewportBottom);
  await capture(page, info, "multiline");
  await dialog.locator(".secrets-create-content").evaluate(element => { element.scrollTop = 0; });
  await dialog.getByLabel("Type", { exact: true }).selectOption("text");
  await dialog.getByLabel("Store", { exact: true }).selectOption("configuration");
  await dialog.getByLabel("Configuration key", { exact: true }).fill("Billing:ApiKey");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(dialog.getByRole("alert")).toBeFocused();
  await expect(dialog.getByLabel("Configuration key", { exact: true })).toHaveValue("Billing:ApiKey");
  await capture(page, info, "save-error");
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Open Create secret", exact: true })).toBeFocused();
});

test("submitting disables draft editing and duplicate submission", async ({ page }, info) => {
  await appearance(page, "classic", "light");
  await page.goto(`/secret-field-prototype.html?open=1&save=pending&field-style=${fieldStyle}`);
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  await dialog.getByLabel("Technical name", { exact: true }).fill("billing.connection");
  await dialog.getByLabel("Value", { exact: true }).fill("demonstration-value");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(dialog.locator("form")).toHaveAttribute("aria-busy", "true");
  await expect(dialog.getByLabel("Technical name", { exact: true })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Show value", exact: true })).toBeDisabled();
  await expect(dialog.getByRole("button", { name: "Creating...", exact: true })).toBeDisabled();
  await capture(page, info, "submitting");
  await page.evaluate(() => window.dispatchEvent(new Event("prototype-finish-save")));
  await expect(dialog).toHaveCount(0);
});

test("native autofill, reduced motion and forced colors retain a label and focus", async ({ page }, info) => {
  await appearance(page, "classic", "light");
  await page.emulateMedia({ forcedColors: "active", reducedMotion: "reduce" });
  await page.goto(`/secret-field-prototype.html?view=states&field-style=${fieldStyle}`);
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "light");
  const input = page.getByLabel("Empty field", { exact: true });
  await input.evaluate(element => { (element as HTMLInputElement).value = "autofilled value"; });
  await page.getByLabel("Read-only field", { exact: true }).focus();
  const labelPosition = await input.evaluate(element => {
    const label = (element as HTMLInputElement).labels![0];
    return { top: parseFloat(getComputedStyle(label).top), labelBottom: label.getBoundingClientRect().bottom, inputTop: element.getBoundingClientRect().top };
  });
  if (fieldStyle === "filled") expect(labelPosition.top).toBeLessThan(6);
  else expect(labelPosition.labelBottom).toBeLessThanOrEqual(labelPosition.inputTop + 1);
  expect(await input.locator("..").locator("label").evaluate(element => parseFloat(getComputedStyle(element).transitionDuration))).toBeLessThanOrEqual(.001);
  await capture(page, info, "forced-colors-reduced-motion");
});

});

test("field style switches live without resetting values and persists across appearances and reload", async ({ page }, info) => {
  await page.goto("/secret-field-prototype.html?view=states");
  const toolbar = page.locator(".secret-prototype-toolbar");
  await expect(toolbar.getByRole("radio", { name: "Outlined", exact: true })).toBeChecked();
  await page.getByLabel("Empty field", { exact: true }).fill("Keep this value");
  await toolbar.getByRole("radio", { name: "Filled", exact: true }).check();
  await expect(page.getByLabel("Empty field", { exact: true })).toHaveValue("Keep this value");
  await toolbar.getByRole("radio", { name: "Outlined", exact: true }).check();
  await expect(page.getByLabel("Empty field", { exact: true })).toHaveValue("Keep this value");
  await page.getByLabel("Preview theme").selectOption("meridian");
  await page.getByLabel("Preview appearance").selectOption("dim");
  await expect(toolbar.getByRole("radio", { name: "Outlined", exact: true })).toBeChecked();
  await page.getByRole("button", { name: "Open Create secret", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  await dialog.getByLabel("Technical name", { exact: true }).fill("invalid name");
  await dialog.getByLabel("Value", { exact: true }).fill("demonstration-value");
  await dialog.getByRole("radio", { name: "Filled", exact: true }).check();
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveValue("invalid name");
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveAttribute("aria-invalid", "true");
  await capture(page, info, "live-filled");
  await dialog.getByRole("radio", { name: "Outlined", exact: true }).check();
  await expect(dialog.getByLabel("Value", { exact: true })).toHaveValue("demonstration-value");
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveAccessibleDescription("Use letters, numbers, dot, dash, underscore, or colon.");
  await capture(page, info, "live-outlined");
  const outlined = dialog.getByRole("radio", { name: "Outlined", exact: true });
  await expect(outlined).toBeFocused();
  await outlined.press("Shift+Tab");
  await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(outlined).toBeFocused();
  await outlined.press("ArrowLeft");
  await expect(dialog.getByRole("radio", { name: "Filled", exact: true })).toBeChecked();
  await page.keyboard.press("ArrowRight");
  await expect(outlined).toBeChecked();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Open Create secret", exact: true })).toBeFocused();
  // Remove the URL override and verify the stored preference independently.
  await page.evaluate(() => { history.replaceState(null, "", "/secret-field-prototype.html?view=states"); });
  await page.reload();
  await expect(toolbar.getByRole("radio", { name: "Outlined", exact: true })).toBeChecked();
});


test("Outlined keyboard scrolling keeps the border label inside the dialog viewport", async ({ page }, info) => {
  await page.setViewportSize({ width: 360, height: 640 });
  await page.goto("/secret-field-prototype.html?open=1&field-style=outlined");
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  await dialog.getByLabel("Type", { exact: true }).selectOption("rsa-key");
  await dialog.getByLabel("Value", { exact: true }).fill("DEMONSTRATION KEY\n".repeat(12));
  await dialog.getByRole("button", { name: /^More options/ }).click();
  await dialog.getByLabel("Display name", { exact: true }).focus();
  const value = dialog.getByLabel("Value", { exact: true });
  // Native date/time editors can expose multiple internal tab stops.
  for (let step = 0; step < 16 && !(await value.evaluate(element => element === document.activeElement)); step++) {
    await page.keyboard.press("Shift+Tab");
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  }
  await expect(value).toBeFocused();
  const bounds = await value.evaluate(element => {
    const label = (element as HTMLTextAreaElement).labels![0].getBoundingClientRect();
    const viewport = element.closest(".secrets-create-content")!.getBoundingClientRect();
    return { labelTop: label.top, labelBottom: label.bottom, viewportTop: viewport.top, viewportBottom: viewport.bottom };
  });
  expect(bounds.labelTop).toBeGreaterThanOrEqual(bounds.viewportTop);
  expect(bounds.labelBottom).toBeLessThanOrEqual(bounds.viewportBottom);
  await capture(page, info, "outlined-keyboard-scroll");
});
