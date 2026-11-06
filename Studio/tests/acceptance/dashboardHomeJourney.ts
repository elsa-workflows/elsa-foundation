import { expect, type Page, type TestInfo } from "@playwright/test";
import { dashboardBackend, observeNativeAgentBoundary, type Capture } from "./dashboardJourneySupport";
import { captureDashboardAppearances } from "./nativeDashboardsJourney";

type Layout = { source: string; revision?: string; schemaVersion: number; value: { widgets: { id: string; size: string; visible: boolean; options?: unknown }[] } };

/** Existing module-contributed Home widgets and preference/default authorities remain the native Home. */
export async function dashboardHomeJourney(page: Page, info: TestInfo, capture: Capture) {
  const viewport = info.project.use.viewport;
  if (viewport && viewport.width < 700) await page.setViewportSize({ width: 320, height: viewport.height });
  const backend = await dashboardBackend(page.request);
  const boundary = observeNativeAgentBoundary(page);
  const initialRead = page.waitForResponse(response => response.request().method() === "GET" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
  await page.goto("/dashboard");
  const initialResponse = await initialRead; expect(initialResponse.ok()).toBe(true);
  const original = await initialResponse.json() as Layout;
  const scope = { "X-Elsa-Studio-Host-Id": initialResponse.request().headers()["x-elsa-studio-host-id"] ?? "default" };
  const previousDefault = await backend.api<Layout>("/_elsa/studio/home-layout/default", "GET", undefined, scope);
  let changedDefault = false;
  const homeWidgets = page.locator(".dashboard-grid > .dashboard-widget");
  try {
    await expect(page.getByRole("heading", { name: "Home", exact: true })).toBeVisible();
    await expect(homeWidgets.first()).toBeVisible();
    const widgetIds = await homeWidgets.evaluateAll(nodes => nodes.map(node => node.getAttribute("data-widget-id")));
    expect(widgetIds).toContain("workflows.run-health"); expect(widgetIds).toContain("workflows.portfolio");
    await capture(page, info, "home-widgets");
    await page.getByRole("combobox", { name: "Refresh", exact: true }).selectOption("0");
    await page.getByRole("button", { name: "Edit home", exact: true }).press("Enter");
    const first = homeWidgets.first(); const firstId = await first.getAttribute("data-widget-id"); expect(firstId).toBeTruthy();
    const move = first.getByRole("button", { name: /^Move .+ later$/ });
    const title = (await move.getAttribute("aria-label"))!.slice(5, -6);
    const write = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
    await move.press("Enter"); expect((await write).ok()).toBe(true);
    await expect(page.locator(`[data-widget-id="${firstId}"]`).getByRole("button", { name: `Move ${title} later`, exact: true })).toBeFocused();
    const resized = page.locator(`[data-widget-id="${firstId}"]`); const size = resized.getByRole("combobox", { name: `Size of ${title}`, exact: true });
    const sizes = await size.locator("option").evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));
    const chosen = sizes.find(value => value !== original.value.widgets.find(widget => widget.id === firstId)?.size) ?? sizes[0];
    const sizeWrite = page.waitForResponse(response => response.request().method() === "PUT" && /\/_elsa\/studio\/home-layout$/.test(response.url()));
    await size.selectOption(chosen); expect((await sizeWrite).ok()).toBe(true); await expect(resized).toHaveAttribute("data-size", chosen);
    await capture(page, info, "home-layout-edit");
    await page.getByRole("button", { name: "Done", exact: true }).click(); await page.reload();
    await expect(homeWidgets.nth(1)).toHaveAttribute("data-widget-id", firstId!);
    await expect(page.locator(`[data-widget-id="${firstId}"]`)).toHaveAttribute("data-size", chosen);
    await capture(page, info, "home-layout-reopened");
    await captureDashboardAppearances(page, info, capture, "home-populated", async name => {
      await expect(page.getByRole("heading", { name: "Home", exact: true })).toBeVisible();
      await expect(page.locator(`[data-widget-id="${firstId}"]`)).toHaveAttribute("data-size", chosen);
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
    await capture(page, info, "home-server-default");
    await page.getByRole("button", { name: "Refresh all", exact: true }).click(); await expect(homeWidgets.first()).toBeVisible();
    for (const alias of ["/", "/overview"]) { await page.goto(alias); await expect(page.getByRole("heading", { name: "Home", exact: true })).toBeVisible(); }
    await capture(page, info, "home-existing-aliases"); await boundary.verify(info);
  } finally {
    boundary.dispose();
    if (changedDefault) {
      const current = await backend.api<Layout>("/_elsa/studio/home-layout/default", "GET", undefined, scope);
      await backend.api("/_elsa/studio/home-layout/default", "PUT", { schemaVersion: 1, value: previousDefault.value }, { ...scope, ...(current.revision ? { "If-Match": `"${current.revision}"` } : { "If-None-Match": "*" }) });
    }
    await backend.api("/_elsa/studio/home-layout", "DELETE", undefined, scope);
    if (original.source === "user") await backend.api("/_elsa/studio/home-layout", "PUT", { schemaVersion: original.schemaVersion, value: original.value }, { ...scope, "If-None-Match": "*" });
  }
}
