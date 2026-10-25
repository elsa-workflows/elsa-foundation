import { expect, test } from "@playwright/test";
import axe from "axe-core";

async function openCreate(page: import("@playwright/test").Page, scenario = "default", mode: "light" | "dark" = "light", expiresAt?: number) {
  const query = new URLSearchParams({ scenario, mode });
  if (expiresAt !== undefined) query.set("expiresAt", String(expiresAt));
  await page.goto(`/connection-secrets.html?${query}`);
  await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", mode);
  const opener = page.locator(".connections-header").getByRole("button", { name: "Create connection", exact: true });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

async function measureRenderedTextContrast(
  page: import("@playwright/test").Page,
  selector: string,
  backgroundOwnerSelector = ".connections-modal"
) {
  const screenshot = await page.screenshot({ fullPage: true, scale: "css" });
  return page.evaluate(async ({ selector, backgroundOwnerSelector, screenshotBase64 }) => {
    const target = document.querySelector<HTMLElement>(selector);
    if (!target) throw new Error(`Could not find rendered text for ${selector}`);
    const owner = target.matches(backgroundOwnerSelector) ? target : target.closest<HTMLElement>(backgroundOwnerSelector);
    if (!owner || !owner.contains(target)) throw new Error(`Could not find the rendered background owner ${backgroundOwnerSelector}`);
    if (backgroundOwnerSelector === ".connections-create-error" && owner !== target) {
      throw new Error("The create-error text must be measured inside its own callout");
    }

    const image = new Image();
    image.src = `data:image/png;base64,${screenshotBase64}`;
    await image.decode();
    const backgroundCanvas = document.createElement("canvas");
    backgroundCanvas.width = image.width;
    backgroundCanvas.height = image.height;
    const backgroundContext = backgroundCanvas.getContext("2d");
    if (!backgroundContext) throw new Error("Could not sample the rendered page screenshot");
    backgroundContext.drawImage(image, 0, 0);

    const bounds = target.getBoundingClientRect();
    const ownerBounds = owner.getBoundingClientRect();
    const left = Math.floor(bounds.left + window.scrollX + 3);
    const right = Math.floor(bounds.right + window.scrollX - 3);
    const top = Math.floor(bounds.top + window.scrollY + 3);
    const bottom = Math.floor(bounds.bottom + window.scrollY - 3);
    const textRects: DOMRect[] = [];
    const walker = document.createTreeWalker(target, NodeFilter.SHOW_TEXT);
    let textNode = walker.nextNode();
    while (textNode) {
      if (textNode.textContent?.trim()) {
        const range = document.createRange();
        range.selectNodeContents(textNode);
        textRects.push(...Array.from(range.getClientRects()));
      }
      textNode = walker.nextNode();
    }

    const overlapsText = (x: number, y: number) => textRects.some(rect =>
      x >= rect.left + window.scrollX - 4
      && x <= rect.right + window.scrollX + 4
      && y >= rect.top + window.scrollY - 4
      && y <= rect.bottom + window.scrollY + 4
    );
    let backgroundPoint: { x: number; y: number } | null = null;
    if (backgroundOwnerSelector === ".connections-modal") {
      const x = Math.floor(ownerBounds.left + window.scrollX + 4);
      const y = Math.floor(ownerBounds.top + window.scrollY + 4);
      if (!overlapsText(x, y)) backgroundPoint = { x, y };
    } else {
      for (let y = top; y <= bottom && !backgroundPoint; y++) {
        for (let x = right; x >= left; x--) {
          if (!overlapsText(x, y)) {
            backgroundPoint = { x, y };
            break;
          }
        }
      }
    }
    const ownerLeft = ownerBounds.left + window.scrollX;
    const ownerRight = ownerBounds.right + window.scrollX;
    const ownerTop = ownerBounds.top + window.scrollY;
    const ownerBottom = ownerBounds.bottom + window.scrollY;
    if (!backgroundPoint || backgroundPoint.x < ownerLeft || backgroundPoint.x >= ownerRight
      || backgroundPoint.y < ownerTop || backgroundPoint.y >= ownerBottom
      || backgroundPoint.x >= image.width || backgroundPoint.y >= image.height) {
      throw new Error("Could not find a text-free pixel inside the rendered error surface");
    }
    const background = Array.from(backgroundContext.getImageData(backgroundPoint.x, backgroundPoint.y, 1, 1).data);

    const foregroundCanvas = document.createElement("canvas");
    foregroundCanvas.width = 1;
    foregroundCanvas.height = 1;
    const foregroundContext = foregroundCanvas.getContext("2d");
    if (!foregroundContext) throw new Error("Could not resolve the rendered text color");
    const foregroundColor = getComputedStyle(target).color;
    foregroundContext.fillStyle = foregroundColor;
    foregroundContext.fillRect(0, 0, 1, 1);
    const foreground = Array.from(foregroundContext.getImageData(0, 0, 1, 1).data);

    let effectiveOpacity = 1;
    const filteredAncestors: string[] = [];
    let current: HTMLElement | null = target;
    while (current) {
      const style = getComputedStyle(current);
      effectiveOpacity *= Number(style.opacity);
      if (style.filter !== "none") filteredAncestors.push(`${current.tagName.toLowerCase()}.${String(current.className).trim().replace(/\s+/g, ".")}`);
      current = current.parentElement;
    }

    const luminance = (rgb: number[]) => {
      const channels = rgb.slice(0, 3).map(value => value / 255).map(value =>
        value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
      );
      return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    };
    const foregroundLuminance = luminance(foreground);
    const backgroundLuminance = luminance(background);

    return {
      foregroundColor,
      backgroundPixel: `rgb(${background.slice(0, 3).join(", ")})`,
      backgroundPoint,
      backgroundOwner: backgroundOwnerSelector,
      foregroundAlpha: foreground[3],
      backgroundAlpha: background[3],
      effectiveOpacity,
      filteredAncestors,
      contrast: (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) /
        (Math.min(foregroundLuminance, backgroundLuminance) + 0.05)
    };
  }, { selector, backgroundOwnerSelector, screenshotBase64: screenshot.toString("base64") });
}

function expectAccessibleTextContrast(measurement: Awaited<ReturnType<typeof measureRenderedTextContrast>>) {
  const details = JSON.stringify(measurement);
  expect(measurement.foregroundAlpha, details).toBe(255);
  expect(measurement.backgroundAlpha, details).toBe(255);
  expect(measurement.effectiveOpacity, details).toBe(1);
  expect(measurement.filteredAncestors, details).toEqual([]);
  expect(measurement.contrast, details).toBeGreaterThanOrEqual(4.5);
}

for (const mode of ["light", "dark"] as const) {
  test(`Connections picker error text contrast is at least 4.5:1 in Classic ${mode} mode`, async ({ page }) => {
    const { dialog } = await openCreate(page, "picker-503", mode);
    const alert = dialog.locator(".connection-secret-picker-status.error[role=alert]");
    await expect(alert).toContainText("Existing Secrets could not be loaded.");
    await expect(alert).not.toContainText("Fixture provider diagnostic");
    const contrast = await measureRenderedTextContrast(page, ".connection-secret-picker-status.error[role=alert]", ".connections-modal");
    expect(contrast.backgroundOwner).toBe(".connections-modal");
    expectAccessibleTextContrast(contrast);
  });

  test(`Connections expired-reference error text contrast is at least 4.5:1 in Classic ${mode} mode`, async ({ page }) => {
    const fixtureTime = new Date("2026-01-01T00:00:00.000Z");
    const expiresAt = fixtureTime.getTime() + 5000;
    await page.clock.install({ time: fixtureTime });
    const { dialog } = await openCreate(page, "expiring-secret", mode, expiresAt);
    const select = dialog.getByLabel("Existing Secret for Token", { exact: true });
    const option = select.getByRole("option", { name: /Customer API token/ });
    await expect(option).toHaveCount(1);
    await select.selectOption((await option.getAttribute("value"))!);
    await page.clock.fastForward(6000);
    await dialog.getByRole("button", { name: "Refresh Secrets", exact: true }).click();

    const alert = dialog.locator(".connection-credential-error[role=alert]");
    await expect(select).toHaveAttribute("aria-invalid", "true");
    await expect(alert).toContainText("Select an available active text Secret");
    const contrast = await measureRenderedTextContrast(page, ".connection-credential-error[role=alert]", ".connections-modal");
    expect(contrast.backgroundOwner).toBe(".connections-modal");
    expectAccessibleTextContrast(contrast);
  });

  test(`Connections create error text contrast is at least 4.5:1 in Classic ${mode} mode`, async ({ page }) => {
    const { dialog } = await openCreate(page, "create-error", mode);
    await dialog.getByLabel("Name", { exact: true }).fill("customer.connection");
    await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
    const select = dialog.getByLabel("Existing Secret for Token", { exact: true });
    const option = select.getByRole("option", { name: /Customer API token/ });
    await expect(option).toHaveCount(1);
    await select.selectOption((await option.getAttribute("value"))!);
    await dialog.getByRole("button", { name: "Create", exact: true }).click();

    const alert = dialog.locator(".connections-create-error[role=alert]");
    await expect(alert).toContainText("Fixture connection create failed.");
    const contrast = await measureRenderedTextContrast(page, ".connections-create-error[role=alert]", ".connections-create-error");
    expect(contrast.backgroundOwner).toBe(".connections-create-error");
    expectAccessibleTextContrast(contrast);
  });
}

for (const viewport of [
  { width: 1440, height: 900 },
  { width: 1280, height: 800 },
  { width: 360, height: 640 }
]) {
  test(`connection create keeps actions visible, body scrollable, and values on errors at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const { dialog, opener } = await openCreate(page, "tall-error");
    const content = dialog.locator(".connections-create-content");
    const cancel = dialog.getByRole("button", { name: "Cancel", exact: true });
    const create = dialog.getByRole("button", { name: "Create", exact: true });
    await expect.poll(() => content.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();

    await dialog.getByLabel("Name", { exact: true }).fill("preserved.connection");
    await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
    await dialog.getByLabel("Extra setting 12", { exact: true }).fill("preserved setting");
    await dialog.getByLabel("Search Secrets for Token", { exact: true }).fill("customer");
    const select = dialog.getByLabel("Existing Secret for Token", { exact: true });
    const option = select.getByRole("option", { name: /Customer API token/ });
    await expect(option).toHaveCount(1);
    await select.selectOption((await option.getAttribute("value"))!);

    await dialog.getByLabel("Extra setting 12", { exact: true }).scrollIntoViewIfNeeded();
    await expect.poll(() => content.evaluate(element => element.scrollTop > 0)).toBe(true);
    expect(await dialog.evaluate(element => element.scrollTop)).toBe(0);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();

    await page.setViewportSize({ ...viewport, height: viewport.height - 24 });
    await expect(dialog.getByLabel("Name", { exact: true })).toHaveValue("preserved.connection");
    await expect(dialog.getByLabel("Base URL", { exact: true })).toHaveValue("https://api.example");
    await expect(dialog.getByLabel("Extra setting 12", { exact: true })).toHaveValue("preserved setting");
    await expect(create).toBeInViewport();
    await create.click();

    const alert = dialog.getByRole("alert");
    await expect(alert).toContainText("Fixture connection create failed.");
    await expect(alert).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect.poll(() => dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await page.keyboard.press("Tab");
    const name = dialog.getByLabel("Name", { exact: true });
    await expect(name).toBeFocused();
    await name.scrollIntoViewIfNeeded();
    await expect(name).toBeInViewport();
    await expect(name).toHaveValue("preserved.connection");
    await expect(dialog.getByLabel("Extra setting 12", { exact: true })).toHaveValue("preserved setting");
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();
    await cancel.click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
  });
}

for (const width of [320, 768, 1024, 1440]) {
  test(`existing Secret creation stays usable at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const { dialog, opener } = await openCreate(page);
    await dialog.getByLabel("Name", { exact: true }).fill("customer-api");
    await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
    await dialog.getByLabel("Search Secrets for Token", { exact: true }).fill("customer");
    const select = dialog.getByLabel("Existing Secret for Token", { exact: true });
    const option = select.getByRole("option", { name: /Customer API token/ });
    await expect(option).toHaveCount(1);
    await select.scrollIntoViewIfNeeded();
    await select.selectOption((await option.getAttribute("value"))!);
    await expect(select).toBeInViewport({ ratio: 1 });
    await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    const payload = JSON.parse(await page.getByTestId("last-connection-payload").innerText());
    expect(payload.secretFields).toEqual({ token: { sourceKind: "secret", existingSecretName: "customer-api" } });
  });
}

test("dialog traps keyboard focus, restores opener, and passes automated accessibility checks", async ({ page }) => {
  const { dialog, opener } = await openCreate(page);
  await expect(dialog.getByLabel("Name", { exact: true })).toBeFocused();
  await dialog.getByRole("button", { name: "Cancel", exact: true }).focus();
  await page.keyboard.press("Tab");
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const root = document.querySelector('[role="dialog"]')!;
    const result = await window.axe.run(root);
    return result.violations.filter(item => item.impact === "critical" || item.impact === "serious").map(item => ({ id: item.id, targets: item.nodes.map(node => node.target) }));
  });
  expect(violations).toEqual([]);
  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});
