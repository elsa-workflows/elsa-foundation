import { expect, type Page, type TestInfo } from "@playwright/test";
import { attachDashboardJson, dashboardBackend, observeNativeWeaverBoundary, type Capture } from "./dashboardJourneySupport";
import { captureDashboardAppearances } from "./nativeDashboardsJourney";

type Layout = { source: string; revision?: string; schemaVersion: number; value: { widgets: { id: string; size: string; visible: boolean; options?: unknown }[] } };
const canonicalAuthenticatedHome = ["home.get-started", "home.quick-actions", "home.recent-workflows", "home.recent-runs", "home.needs-attention", "home.tasks"];
const homeOwners = {
  authenticatedDefault: "Essentials/src/Studio/Preferences/Core/StudioHomeLayout.cs",
  page: "Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage.tsx",
  anonymousFallback: "Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/homeLayout.ts",
  moduleRegistration: "Extensions/Workflows.Dashboard/studio/Elsa.Studio.Workflows.Dashboard/Client/src/module.tsx"
};

/** Existing module-contributed Home widgets and preference/default authorities remain the native Home. */
export async function dashboardHomeJourney(page: Page, info: TestInfo, capture: Capture) {
  const viewport = info.project.use.viewport;
  if (viewport && viewport.width < 700) await page.setViewportSize({ width: 320, height: viewport.height });
  const backend = await dashboardBackend(page.request);
  const boundary = observeNativeWeaverBoundary(page);
  const initialRead = page.waitForResponse(response => response.request().method() === "GET" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
  await page.goto("/dashboard");
  const initialResponse = await initialRead; expect(initialResponse.ok()).toBe(true);
  const original = await initialResponse.json() as Layout;
  const scope = { "X-Elsa-Studio-Host-Id": initialResponse.request().headers()["x-elsa-studio-host-id"] ?? "default" };
  const previousDefault = await backend.api<Layout>("/_elsa/studio/home-layout/default", "GET", undefined, scope);
  let changedDefault = false;
  const homeWidgets = page.locator(".dashboard-grid > .dashboard-widget");
  const normalizeSize = (value: string) => value === "L" ? "full" : value === "M" ? "medium" : value === "S" ? "small" : value;
  async function waitForSettledHome(expected: Layout["value"]) {
    await expect(page.getByRole("heading", { name: "Home", level: 2, exact: true })).toBeVisible({ timeout: 30_000 });
    const visible = expected.widgets.filter(widget => widget.visible).map(widget => ({ id: widget.id, size: normalizeSize(widget.size) }));
    await expect.poll(() => homeWidgets.evaluateAll(nodes => nodes.map(node => ({ id: node.getAttribute("data-widget-id"), size: node.getAttribute("data-size") }))), { timeout: 30_000 }).toEqual(visible);
    await expect(page.locator('.dashboard-grid > .dashboard-widget[aria-busy="true"]')).toHaveCount(0, { timeout: 30_000 });
    await expect(homeWidgets.locator(".dashboard-widget-body").getByText(/^Loading(?:…|\.\.\.)$/)).toHaveCount(0, { timeout: 30_000 });
    await expect(homeWidgets.locator(".dashboard-widget-body").getByRole("alert")).toHaveCount(0);
  }
  try {
    await waitForSettledHome(original.value);
    const widgetIds = await homeWidgets.evaluateAll(nodes => nodes.map(node => node.getAttribute("data-widget-id")));
    expect(widgetIds).toEqual(canonicalAuthenticatedHome);
    const expectedBuiltIn = canonicalAuthenticatedHome.map((id, index) => ({ id, size: index === 0 ? "full" : "medium", visible: true }));
    expect(original.value.widgets.map(({ id, size, visible }) => ({ id, size, visible }))).toEqual(expectedBuiltIn);
    expect(previousDefault.value.widgets.map(({ id, size, visible }) => ({ id, size, visible }))).toEqual(expectedBuiltIn);
    await attachDashboardJson(info, "home-default-baseline.json", { source: process.env.UX_SOURCE_SHA ?? "unknown", owners: homeOwners, documentation: "Studio/src/essentials/Elsa.Studio.Dashboard/README.md#widgets", authenticatedDefault: original.value, anonymousFallback: "The client fallback is a separate source and is not exercised by this authenticated server-default journey. Main-source comparison belongs to the review evidence, not this runtime result." });
    await capture(page, info, "home-widgets");
    await page.getByRole("combobox", { name: "Refresh", exact: true }).selectOption("0");
    await page.getByRole("button", { name: "Edit home", exact: true }).press("Enter");
    for (const [index, title] of ["Workflow run health", "Workflow portfolio"].entries()) {
      await page.getByRole("button", { name: "Add widget", exact: true }).first().click();
      const modulePicker = page.getByRole("dialog", { name: "Add widget", exact: true });
      await modulePicker.getByRole("textbox", { name: "Search widgets", exact: true }).fill(title);
      await expect(modulePicker.getByRole("heading", { name: "From modules", exact: true })).toBeVisible();
      await expect(modulePicker.getByRole("button", { name: title, exact: true })).toBeVisible();
      await capture(page, info, `home-module-picker-${index + 1}`);
      const added = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
      await modulePicker.getByRole("button", { name: title, exact: true }).click();
      expect((await added).ok()).toBe(true); await expect(modulePicker).toBeHidden();
    }
    await page.getByRole("button", { name: "Done", exact: true }).click(); await page.reload();
    const runHealth = page.locator('[data-widget-id="workflows.run-health"]');
    const portfolio = page.locator('[data-widget-id="workflows.portfolio"]');
    const addedLayout = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope);
    await waitForSettledHome(addedLayout.value);
    await expect(runHealth.locator(".workflow-run-health")).toBeVisible(); await expect(portfolio.locator(".workflow-portfolio")).toBeVisible();
    for (const id of ["workflows.run-health", "workflows.portfolio"]) expect(addedLayout.value.widgets.find(widget => widget.id === id)?.visible).toBe(true);
    await runHealth.scrollIntoViewIfNeeded(); await capture(page, info, "home-module-run-health");
    await portfolio.scrollIntoViewIfNeeded(); await capture(page, info, "home-module-portfolio");
    await page.getByRole("button", { name: "Edit home", exact: true }).click();
    await runHealth.getByRole("button", { name: "Settings for Workflow run health", exact: true }).click();
    const runSettings = page.getByRole("dialog", { name: "Workflow run health settings", exact: true });
    await runSettings.getByRole("combobox", { name: "Date range", exact: true }).selectOption({ label: "7 days" });
    await runSettings.getByRole("checkbox", { name: "Include test runs", exact: true }).check();
    await capture(page, info, "home-module-settings");
    const settingsWrite = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
    await runSettings.getByRole("button", { name: "Save", exact: true }).click(); expect((await settingsWrite).ok()).toBe(true);
    await expect(runSettings).toBeHidden();
    await expect(runHealth.locator(".workflow-run-health__scope")).toContainText("Test runs included");
    const configured = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope);
    expect(configured.value.widgets.find(widget => widget.id === "workflows.run-health")?.options).toMatchObject({ range: "7d", includeTestRuns: true });
    await page.getByRole("button", { name: "Done", exact: true }).click(); await page.reload();
    await waitForSettledHome(configured.value);
    await expect(runHealth.locator(".workflow-run-health__scope")).toContainText("Test runs included");
    await page.getByRole("button", { name: "Edit home", exact: true }).click();
    await runHealth.getByRole("button", { name: "Settings for Workflow run health", exact: true }).click();
    await expect(runSettings.getByRole("combobox", { name: "Date range", exact: true })).toHaveValue('"7d"');
    await expect(runSettings.getByRole("checkbox", { name: "Include test runs", exact: true })).toBeChecked();
    await capture(page, info, "home-module-settings-reopened");
    await runSettings.getByRole("button", { name: "Cancel", exact: true }).press("Escape");
    await attachDashboardJson(info, "home-module-widgets.json", { source: process.env.UX_SOURCE_SHA ?? "unknown", registrationOwner: homeOwners.moduleRegistration, addedThroughPicker: ["workflows.run-health", "workflows.portfolio"], persistedLayout: configured.value, settings: { widget: "workflows.run-health", range: "7d", includeTestRuns: true }, portfolioSettings: "No settings descriptor is registered by its owning module." });
    const first = homeWidgets.first(); const firstId = await first.getAttribute("data-widget-id"); expect(firstId).toBeTruthy();
    const move = first.getByRole("button", { name: /^Move .+ later$/ });
    const title = (await move.getAttribute("aria-label"))!.slice(5, -6);
    const write = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
    await move.press("Enter"); expect((await write).ok()).toBe(true);
    await expect(page.locator(`[data-widget-id="${firstId}"]`).getByRole("button", { name: `Move ${title} later`, exact: true })).toBeFocused();
    const resized = page.locator(`[data-widget-id="${firstId}"]`); const size = resized.getByRole("combobox", { name: `Size of ${title}`, exact: true });
    const sizes = await size.locator("option").evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
    const chosen = sizes.find(value => normalizeSize(value) !== original.value.widgets.find(widget => widget.id === firstId)?.size) ?? sizes[0];
    const chosenSize = normalizeSize(chosen);
    const sizeWrite = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
    await size.selectOption(chosen); expect((await sizeWrite).ok()).toBe(true); await expect(resized).toHaveAttribute("data-size", chosenSize);
    const resizedLayout = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope);
    await waitForSettledHome(resizedLayout.value);
    await capture(page, info, "home-layout-edit");
    await page.getByRole("button", { name: "Done", exact: true }).click(); await page.reload();
    await waitForSettledHome(resizedLayout.value);
    await expect(homeWidgets.nth(1)).toHaveAttribute("data-widget-id", firstId!);
    await expect(page.locator(`[data-widget-id="${firstId}"]`)).toHaveAttribute("data-size", chosenSize);
    await capture(page, info, "home-layout-reopened");
    await captureDashboardAppearances(page, info, capture, "home-populated", async name => {
      await waitForSettledHome(resizedLayout.value);
      await expect(page.locator(`[data-widget-id="${firstId}"]`)).toHaveAttribute("data-size", chosenSize);
      await expect(page.getByRole("button", { name: "Edit home", exact: true })).toBeVisible();
      await capture(page, info, name);
    });
    await page.getByRole("button", { name: "Edit home", exact: true }).click();
    const target = homeWidgets.filter({ has: page.getByRole("button", { name: /^Hide / }) }).first();
    const hiddenId = await target.getAttribute("data-widget-id"); const hide = target.getByRole("button", { name: /^Hide / });
    const targetTitle = (await hide.getAttribute("aria-label"))!.slice(5);
    await hide.click();
    await expect(page.locator(`[data-widget-id="${hiddenId}"]`)).toHaveCount(0);
    await page.getByRole("button", { name: "Add widget", exact: true }).first().click();
    const picker = page.getByRole("dialog", { name: "Add widget", exact: true });
    await expect(picker).toBeVisible(); await picker.getByRole("textbox", { name: "Search widgets", exact: true }).fill(targetTitle);
    await capture(page, info, "home-widget-picker");
    await picker.getByRole("button", { name: `${targetTitle} Dismissed`, exact: true }).click();
    await expect(page.locator(`[data-widget-id="${hiddenId}"]`)).toBeVisible();
    const defaultWrite = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout\/default$/.test(response.url()));
    await page.getByRole("button", { name: "Save as server default", exact: true }).click(); expect((await defaultWrite).ok()).toBe(true); changedDefault = true;
    const savedDefault = await backend.api<Layout>("/_elsa/studio/home-layout/default", "GET", undefined, scope);
    const personal = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope); expect(savedDefault.value).toEqual(personal.value);
    await page.getByRole("button", { name: "Reset to default", exact: true }).click();
    await expect(page.getByRole("button", { name: "Edit home", exact: true })).toBeVisible(); await page.reload();
    const inherited = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope); expect(inherited.source).toBe("default"); expect(inherited.value).toEqual(savedDefault.value);
    await waitForSettledHome(inherited.value);
    await capture(page, info, "home-server-default");
    await page.getByRole("button", { name: "Refresh all", exact: true }).click(); await waitForSettledHome(inherited.value);
    for (const alias of ["/", "/overview"]) { await page.goto(alias); await waitForSettledHome(inherited.value); }
    await capture(page, info, "home-existing-aliases"); await boundary.verify(info);
  } finally {
    boundary.dispose();
    if (changedDefault) {
      const current = await backend.api<Layout>("/_elsa/studio/home-layout/default", "GET", undefined, scope);
      await backend.api("/_elsa/studio/home-layout/default", "PUT", { schemaVersion: 1, value: previousDefault.value }, { ...scope, ...(current.revision ? { "If-Match": `"${current.revision}"` } : { "If-None-Match": "*" }) });
    }
    await backend.api("/_elsa/studio/home-layout", "DELETE", undefined, scope);
    if (original.source === "user") await backend.api("/_elsa/studio/home-layout", "PUT", { schemaVersion: original.schemaVersion, value: original.value }, { ...scope, "If-None-Match": "*" });
    const restoredDefault = await backend.api<Layout>("/_elsa/studio/home-layout/default", "GET", undefined, scope);
    const restoredUser = await backend.api<Layout>("/_elsa/studio/home-layout", "GET", undefined, scope);
    expect(restoredDefault.value).toEqual(previousDefault.value); expect(restoredUser.value).toEqual(original.value);
    await attachDashboardJson(info, "home-preferences-restored.json", { originalUserSource: original.source, restoredUserSource: restoredUser.source, originalDefaultSource: previousDefault.source, restoredDefaultSource: restoredDefault.source, userValue: restoredUser.value, defaultValue: restoredDefault.value, restoration: "Original effective layout and options restored. The public default API supports GET/PUT; a previously builtin default is restored as an equivalent explicit default value." });
  }
}
