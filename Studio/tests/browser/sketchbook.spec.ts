import { expect, test } from "@playwright/test";

async function seed(page: import("@playwright/test").Page, mode = "light") {
  await page.addInitScript(mode => {
    localStorage.setItem("elsa-studio-workspace-style", "sketchbook");
    localStorage.setItem("elsa-studio-color-scheme", "builtin:sketchbook");
    localStorage.setItem("elsa-studio-theme-mode", mode);
  }, mode);
}

test("Sketchbook complete preset is discoverable and survives reload with its independent choices", async ({ page }) => {
  await page.goto("/workspace-appearance.html?screen=appearance");
  await page.getByRole("radiogroup", { name: "Studio preset", exact: true }).getByRole("radio", { name: "Sketchbook", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "sketchbook");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "builtin:sketchbook");
  await page.getByRole("radiogroup", { name: "Color scheme", exact: true }).getByRole("radio", { name: "Drift", exact: true }).click();
  await page.locator(".settings-page").getByRole("radiogroup", { name: "Colour mode", exact: true }).getByRole("radio", { name: "Dim", exact: true }).click();
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "sketchbook");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "drift");
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dim");
  await expect(page.locator(".brand img")).toHaveAttribute("src", /elsa-sketchbook/);
});

for (const mode of ["light", "dark", "dim", "high-contrast"]) {
  test(`Sketchbook ${mode} keeps BPMN notation and authoring focus precise`, async ({ page }, testInfo) => {
    await seed(page, mode);
    await page.goto("/workspace-appearance.html?composition=bpmn&palette-context");
    const search = page.getByRole("searchbox", { name: "Search element palette" });
    await search.fill("Start event");
    await page.getByRole("treeitem", { name: "Start event", exact: true }).click();
    await search.fill("End event");
    await page.getByRole("treeitem", { name: "End event", exact: true }).click();
    await expect(page.locator(".wf-bpmn-event-start")).toBeVisible();
    await expect(page.locator(".wf-bpmn-event-end")).toBeVisible();
    const start = page.locator(".wf-bpmn-event-start");
    const end = page.locator(".wf-bpmn-event-end");
    const borders = await Promise.all([start, end].map(node => node.evaluate(el => ({ width: Number.parseFloat(getComputedStyle(el).borderTopWidth), radius: getComputedStyle(el).borderTopLeftRadius }))));
    expect(borders[1].width).toBeGreaterThan(borders[0].width);
    expect(borders[0].radius).toBe("50%");
    const node = page.locator(".react-flow__node").first();
    await node.focus();
    await expect(node).toBeFocused();
    const canvas = page.locator(".wf-canvas");
    const background = await canvas.evaluate(el => getComputedStyle(el).backgroundImage);
    if (mode === "high-contrast") expect(background).toBe("none");
    else expect(background).toContain("linear-gradient");
    await page.screenshot({ path: testInfo.outputPath(`sketchbook-${mode}.png`) });
  });
}

test("Sketchbook respects the host logo and removes its drawing treatment when switching style", async ({ page }) => {
  await seed(page);
  await page.addInitScript(() => { window.__ELSA_STUDIO_RUNTIME__ = { branding: { productName: "Northstar", logoUrl: "/customer-logo.svg" } }; });
  await page.route("**/customer-logo.svg", route => route.fulfill({ contentType: "image/svg+xml", body: '<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><circle cx="16" cy="16" r="14" fill="navy"/></svg>' }));
  await page.goto("/workspace-appearance.html?screen=appearance");
  await expect(page.locator(".brand img")).toHaveAttribute("src", "/customer-logo.svg");
  await page.getByRole("radiogroup", { name: "Workspace style", exact: true }).getByRole("radio", { name: "Minimal", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
  await expect(page.locator(".brand img")).toHaveAttribute("src", "/customer-logo.svg");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "builtin:sketchbook");
});

for (const forcedColors of ["none", "active"] as const) {
  test(`Sketchbook high contrast removes icon washes with forced colors ${forcedColors}`, async ({ page }) => {
    await seed(page, "high-contrast");
    await page.emulateMedia({ forcedColors });
    await page.goto("/workspace-appearance.html?composition=flowchart");
    await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "sketchbook");
    await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "high-contrast");
    await expect(page.locator(".wf-editor-body")).toHaveCSS("border-top-left-radius", "10px");
    const icons = page.locator(".wf-node-icon, .wf-activity-icon");
    await expect(icons.first()).toBeVisible();
    expect(await icons.evaluateAll(elements => elements.map(element => getComputedStyle(element).backgroundImage))).not.toContainEqual(expect.stringContaining("gradient"));
  });
}
