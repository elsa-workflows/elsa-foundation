import { expect, test } from "@playwright/test";
import { isFieldUnobscured } from "../visibleContent";

for (const appearance of ["light", "dark"]) for (const height of [600, 844]) {
  test(`keeps the last Settings field clear of wrapped persistent toasts (${appearance}, ${height}px)`, async ({ page }, info) => {
    await page.setViewportSize({ width: 390, height });
    await page.goto(`/settings/feature-management?mode=settings&appearance=${appearance}&focus-notices`);
    await page.evaluate(() => document.fonts.ready);
    const field = page.getByRole("textbox", { name: "Setting 12", exact: true });
    await field.fill("Retained phone value");
    await field.evaluate(element => (element as HTMLInputElement).setSelectionRange(3, 9));
    for (let notice = 0; notice < 3; notice++) {
      await page.getByRole("button", { name: "Show focus notice", exact: true }).click();
      await expect(page.locator(".studio-toast")).toHaveCount(notice + 1);
      await expect(field).toBeFocused();
      await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    }
    await expect(field).toHaveValue("Retained phone value");
    expect(await field.evaluate(element => [(element as HTMLInputElement).selectionStart, (element as HTMLInputElement).selectionEnd])).toEqual([3, 9]);
    await page.screenshot({ path: info.outputPath("focused-field-above-notices.png") });
    await page.setViewportSize({ width: 390, height: height === 600 ? 844 : 600 });
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    await expect(field).toBeFocused();

    const dismiss = page.getByRole("button", { name: "Dismiss notification", exact: true }).first();
    await dismiss.focus();
    await expect(dismiss).toBeFocused();
    await expect.poll(() => page.locator(".content").evaluate(element => (element as HTMLElement).style.paddingBlockEnd)).toBe("");
    await dismiss.press("Enter");
    await expect(page.locator(".studio-toast")).toHaveCount(2);
    await expect(page.locator(".studio-toast").first()).toBeFocused();
    while (await page.locator(".studio-toast").count()) {
      await page.getByRole("button", { name: "Dismiss notification", exact: true }).first().click();
    }
    await field.focus();
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    await expect(field).toHaveValue("Retained phone value");
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  });
}

for (const appearance of ["light", "dark"]) for (const bound of ["start", "end"]) {
  test(`reveals the full field through an outer clip at the inner scrollport's ${bound} bound (${appearance})`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 600 });
    await page.goto(`/settings/feature-management?mode=settings&appearance=${appearance}&focus-notices&nested-focus`);
    await page.evaluate(() => document.fonts.ready);
    const field = page.getByRole("textbox", { name: bound === "start" ? "Setting 1" : "Setting 12", exact: true });
    await field.fill("Nested retained value");
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    // Reproduce a native input already visible while its legend/frame is clipped by the outer port.
    const clipped = await field.evaluate((element, edge) => {
      const input = element as HTMLInputElement;
      const inner = input.closest<HTMLElement>("[data-nested-focus-port]")!;
      const outer = input.closest<HTMLElement>(".content")!;
      inner.scrollTop = edge === "start" ? 0 : inner.scrollHeight - inner.clientHeight;
      const clip = outer.getBoundingClientRect();
      const target = input.getBoundingClientRect();
      outer.scrollTop += edge === "start" ? target.top - (clip.top + outer.clientTop + 1)
        : target.bottom - (clip.top + outer.clientTop + outer.clientHeight - 1);
      const native = input.getBoundingClientRect();
      const frame = input.closest<HTMLElement>(".studio-form-control")!.getBoundingClientRect();
      const top = clip.top + outer.clientTop, bottom = top + outer.clientHeight;
      return { nativeVisible: native.top >= top && native.bottom <= bottom, frameClipped: frame.top < top || frame.bottom > bottom };
    }, bound);
    expect(clipped).toEqual({ nativeVisible: true, frameClipped: true });
    await page.evaluate(() => window.dispatchEvent(new Event("resize")));
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    await expect(field).toBeFocused();
    await expect(field).toHaveValue("Nested retained value");
    await page.getByRole("button", { name: "Show focus notice", exact: true }).click();
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    await expect(field).toBeFocused();
    await expect(field).toHaveValue("Nested retained value");
  });
}

for (const appearance of ["light", "dark"]) {
  test(`reveals a large composite field's active input and label through the inner clip (${appearance})`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 600 });
    await page.goto(`/settings/feature-management?mode=settings&appearance=${appearance}&focus-notices&nested-focus&large-frame`);
    await page.evaluate(() => document.fonts.ready);
    const field = page.getByRole("textbox", { name: "Setting 1", exact: true });
    await field.fill("Large composite retained value");
    await expect.poll(() => field.evaluate(isFieldUnobscured, false)).toBe(true);
    const clipped = await field.evaluate(element => {
      const input = element as HTMLInputElement;
      const inner = input.closest<HTMLElement>("[data-nested-focus-port]")!;
      const label = input.labels![0];
      const top = inner.getBoundingClientRect().top + inner.clientTop;
      inner.scrollTop += label.getBoundingClientRect().top - top + 4;
      const control = input.getBoundingClientRect();
      return { nativeVisible: control.top >= top && control.bottom <= top + inner.clientHeight,
        labelClipped: label.getBoundingClientRect().top < top,
        frameTooTall: input.closest<HTMLElement>(".studio-form-control")!.getBoundingClientRect().height > inner.clientHeight };
    });
    expect(clipped).toEqual({ nativeVisible: true, labelClipped: true, frameTooTall: true });
    await page.evaluate(() => window.dispatchEvent(new Event("resize")));
    await expect.poll(() => field.evaluate(isFieldUnobscured, false)).toBe(true);
    await expect(field).toBeFocused();
    await expect(field).toHaveValue("Large composite retained value");
    await page.getByRole("button", { name: "Show focus notice", exact: true }).click();
    await expect.poll(() => field.evaluate(isFieldUnobscured, false)).toBe(true);
    await expect(field).toBeFocused();
    await expect(field).toHaveValue("Large composite retained value");
    const dismiss = page.getByRole("button", { name: "Dismiss notification", exact: true });
    await dismiss.focus();
    await expect(dismiss).toBeInViewport();
    await expect(dismiss).toBeFocused();
    await dismiss.press("Enter");
    await expect(page.locator(".studio-toast")).toHaveCount(0);
  });
}


for (const appearance of ["light", "dark"]) {
  test(`keeps a dock-reduced scrollport stable across repeated toast reveals and shrink (${appearance})`, async ({ page }, info) => {
    await page.setViewportSize({ width: 390, height: 600 });
    await page.addInitScript(() => {
      localStorage.setItem("elsa-studio-bottom-panel-collapsed", "false");
      localStorage.setItem("elsa-studio-bottom-panel-height", "260");
    });
    await page.goto(`/settings/feature-management?mode=settings&appearance=${appearance}&focus-notices&short-focus`);
    await page.evaluate(() => document.fonts.ready);
    const port = page.locator(".content");
    const link = page.getByRole("link", { name: "Inline Settings link", exact: true });
    const resize = page.getByRole("separator", { name: "Resize bottom panel", exact: true });
    const setNaturalHeight = async (height: number) => {
      const current = await port.evaluate(element => element.clientHeight);
      const handle = (await resize.boundingBox())!;
      await page.mouse.move(handle.x + 2, handle.y + handle.height / 2);
      await page.mouse.down();
      await page.mouse.move(handle.x + 2, handle.y + handle.height / 2 - (current - height));
      await page.mouse.up();
      await expect.poll(() => port.evaluate(element => element.clientHeight)).toBe(height);
    };
    await setNaturalHeight(150);
    await link.focus();
    for (let notice = 0; notice < 3; notice++) await page.getByRole("button", { name: "Show focus notice", exact: true }).click();
    await expect(page.locator(".studio-toast")).toHaveCount(3);
    const assertStable = async (naturalHeight: number) => {
      await expect.poll(() => link.evaluate(isFieldUnobscured)).toBe(true);
      for (let repeat = 0; repeat < 6; repeat++) {
        await page.evaluate(async () => {
          window.dispatchEvent(new Event("resize"));
          for (let frame = 0; frame < 3; frame++) await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
        });
        const geometry = await port.evaluate(element => {
          const node = element as HTMLElement, style = getComputedStyle(node);
          return { height: node.clientHeight, padding: parseFloat(style.paddingBlockStart) + parseFloat(style.paddingBlockEnd), reserved: node.style.paddingBlockEnd !== "" };
        });
        expect(geometry.height).toBe(naturalHeight);
        expect(geometry.padding).toBeLessThanOrEqual(naturalHeight);
        expect(geometry.reserved).toBe(true);
        await expect(link).toBeFocused();
        expect(await link.evaluate(isFieldUnobscured)).toBe(true);
        await expect(page.locator(".studio-toast")).toHaveCount(3);
      }
    };
    await assertStable(150);
    // Dragging the real dock does not steal focus from the inline control.
    await setNaturalHeight(120);
    await assertStable(120);
    await page.screenshot({ path: info.outputPath("short-workspace-stable-clearance.png") });
    const dismiss = page.getByRole("button", { name: "Dismiss notification", exact: true }).first();
    await dismiss.focus();
    await expect.poll(() => port.evaluate(element => (element as HTMLElement).style.paddingBlockEnd)).toBe("");
    await dismiss.press("Enter");
    await expect(page.locator(".studio-toast")).toHaveCount(2);
  });
}
