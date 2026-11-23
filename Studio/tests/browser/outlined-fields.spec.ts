import { test, expect, type Page, type TestInfo } from "@playwright/test";
import { allThemeModes, builtInThemeDefinitions } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { workspaceStyles } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles";

for (const theme of builtInThemeDefinitions) for (const mode of allThemeModes) {
  test(`Outlined ${theme.id} ${mode}`, async ({ page }, info) => {
    await page.goto(`/outlined-fields.html?theme=${theme.id}&mode=${mode}`);
    await page.getByLabel("Focused", { exact: true }).focus();
    await expect(page.getByLabel("Focused", { exact: true })).toBeFocused();
    const frames = page.getByRole("region", { name: "Field states", exact: true }).locator(".studio-form-control");
    await expect(frames).toHaveCount(20);
    const ownedEditor = page.getByLabel("Host actions", { exact: true });
    await expect(ownedEditor).toHaveValue("Opaque editor value");
    const ownedFrame = frames.filter({ has: ownedEditor });
    await expect(ownedFrame.getByRole("button", { name: "Editor action", exact: true })).toBeVisible();
    await expect(ownedFrame.getByRole("button", { name: "Host action", exact: true })).toBeVisible();
    const geometry = await frames.evaluateAll(elements => elements.map(element => {
      const box = element.getBoundingClientRect(), legend = element.querySelector("legend")!.getBoundingClientRect(), style = getComputedStyle(element);
      return { border: style.borderLeftWidth, overflows: element.scrollWidth > element.clientWidth + 1, labelFits: legend.right <= box.right + 1 && legend.left >= box.left - 1 };
    }));
    expect(geometry.every(item => item.border === "1px" && !item.overflows && item.labelFits)).toBe(true);
    expect(await page.locator(".studio-form-control [data-studio-field-control]").evaluateAll(elements => elements.every(element => getComputedStyle(element).borderLeftWidth === "0px"))).toBe(true);
    const record = page.getByRole("region", { name: "Completed response", exact: true }).getByLabel("Note", { exact: true });
    await expect(record).toBeDisabled();
    await expect(record).toHaveValue("Reviewed purchase request");
    await expect(record).toHaveCSS("resize", "none");
    expect(await record.evaluate(element => { const style = getComputedStyle(element.closest("fieldset")!); return { border: style.borderTopWidth, opacity: style.opacity }; })).toEqual({ border: "0px", opacity: "1" });
    await expect(page.getByLabel("Disabled", { exact: true })).toBeDisabled();
    expect(await page.locator(".studio-form-control [data-studio-field-control]:disabled").evaluateAll(elements => elements.every(element => getComputedStyle(element).opacity === "1"))).toBe(true);
    await expect(page.getByLabel("Read only", { exact: true })).toHaveAttribute("readonly", "");
    await expect(page.getByLabel("Invalid", { exact: true })).toHaveAttribute("aria-invalid", "true");

    // Focus changes the existing notched border, including hover and invalid fields.
    for (const label of ["Focused", "Select", "Description", "Read only", "Invalid"]) {
      const control = page.getByLabel(label, { exact: true });
      const frame = control.locator("xpath=ancestor::fieldset[1]");
      const before = await frame.boundingBox();
      await control.focus();
      await control.hover();
      const focusStyle = await frame.evaluate(element => {
        const style = getComputedStyle(element);
        const probe = document.createElement("span");
        probe.style.color = "var(--studio-focus-ring-solid, var(--studio-accent))";
        element.append(probe);
        const color = getComputedStyle(probe).color; probe.remove();
        return { border: style.borderColor, focus: color, outline: style.outlineStyle };
      });
      expect(focusStyle.outline).toBe("none");
      expect(focusStyle.border).toBe(focusStyle.focus);
      expect(await frame.boundingBox()).toEqual(before);
    }
    await page.getByLabel("Focused", { exact: true }).focus();
    await capture(page, info, `${theme.id}-${mode}-states`);
    const draft = page.getByLabel("Invalid", { exact: true });
    await draft.fill("Recovered draft"); await expect(draft).not.toHaveAttribute("aria-invalid", "true");
    await page.getByRole("button", { name: "Show value" }).click();
    await expect(page.getByLabel("Secret value", { exact: true })).toHaveValue("protected draft");
    await expect(page.getByLabel("Secret value", { exact: true })).toHaveAttribute("type", "text");
    await page.getByLabel("Focused", { exact: true }).focus();
    await capture(page, info, `${theme.id}-${mode}`);
  });
}
for (const style of workspaceStyles) for (const mode of ["light", "dark"] as const) {
  test(`Outlined ${style.id} narrow ${mode}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(`/outlined-fields.html?theme=classic&mode=${mode}&style=${style.id}&rtl=1`);
    await page.evaluate(() => { document.documentElement.style.setProperty("--studio-body-size", "28px"); document.documentElement.style.setProperty("--studio-small-size", "24px"); });
    await expect(page.getByLabel("Populated", { exact: true })).toHaveValue("Draft value");
    await page.getByLabel("Description", { exact: true }).focus();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await capture(page, info, `${style.id}-${mode}-narrow-rtl`);
  });
}
test("Outlined forced colours preserve field and keyboard focus", async ({ page }, info) => {
  await page.emulateMedia({ forcedColors: "active", reducedMotion: "reduce" });
  await page.goto("/outlined-fields.html");
  const field = page.getByLabel("Populated", { exact: true }); await field.focus();
  await expect(field).toBeFocused();
  const frame = field.locator("xpath=ancestor::fieldset[1]");
  await expect(frame).toHaveCSS("forced-color-adjust", "none");
  const highlight = await page.evaluate(() => { const probe = document.createElement("span"); probe.style.color = "Highlight"; document.body.append(probe); const color = getComputedStyle(probe).color; probe.remove(); return color; });
  await expect(frame).toHaveCSS("border-color", highlight);
  await expect(frame).toHaveCSS("outline-style", "none");
  await capture(page, info, "forced-colours");
});

async function capture(page: Page, info: TestInfo, name: string) {
  const note = page.getByRole("region", { name: "Completed response", exact: true }).getByLabel("Note", { exact: true });
  await expect(note).toHaveValue("Reviewed purchase request");
  await expect(note).toBeDisabled();
  const before = await page.evaluate(async () => {
    await document.fonts.ready;
    const finite = document.getAnimations().filter(animation => {
      const end = animation.effect?.getComputedTiming().endTime;
      return (animation.playState === "running" || animation.pending) && typeof end === "number" && Number.isFinite(end);
    });
    await Promise.allSettled(finite.map(animation => animation.finished));
    const note = document.querySelector<HTMLTextAreaElement>('[aria-label="Completed response"] textarea')!;
    const measure = () => ({
      width: Math.max(document.documentElement.scrollWidth, document.body.scrollWidth),
      height: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight),
      noteBottom: Math.ceil(note.getBoundingClientRect().bottom + scrollY),
      noteFits: note.scrollHeight <= note.clientHeight + 1,
      pixelRatio: devicePixelRatio,
    });
    let previous = measure(), stable = 0;
    // Font-size transitions and the textarea's ResizeObserver can change the full-page extent.
    while (stable < 3) {
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
      const current = measure();
      stable = JSON.stringify(current) === JSON.stringify(previous) ? stable + 1 : 0;
      previous = current;
    }
    return previous;
  });
  const path = info.outputPath(`${name.replace(/[^a-zA-Z0-9_-]/g, "-")}.png`);
  const png = await page.screenshot({ path, fullPage: true });
  await info.attach(name, { path, contentType: "image/png" });
  const after = await note.evaluate(note => ({
    width: Math.max(document.documentElement.scrollWidth, document.body.scrollWidth),
    height: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight),
    noteBottom: Math.ceil(note.getBoundingClientRect().bottom + scrollY),
    noteFits: note.scrollHeight <= note.clientHeight + 1,
    pixelRatio: devicePixelRatio,
  }));
  const header = new DataView(png.buffer, png.byteOffset, png.byteLength);
  const dimensions = { width: header.getUint32(16), height: header.getUint32(20) };
  await info.attach(`${name}-extent`, { body: JSON.stringify({ before, after, dimensions }), contentType: "application/json" });
  expect(after).toEqual(before);
  expect(after.noteFits).toBe(true);
  expect(dimensions.width).toBeGreaterThanOrEqual(Math.ceil(after.width * after.pixelRatio));
  expect(dimensions.height).toBeGreaterThanOrEqual(Math.ceil(Math.max(after.height, after.noteBottom) * after.pixelRatio));
}
