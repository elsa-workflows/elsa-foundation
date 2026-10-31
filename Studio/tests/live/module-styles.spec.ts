import { expect, test } from "@playwright/test";
import { signIn } from "./support/journey";

test.skip(!process.env.ELSA_STUDIO_URL, "Requires an owned normal Workbench/Studio pair.");

test("normal host defers canvas styles until a real workflow editor opens", async ({ page }, testInfo) => {
  await signIn(page);
  const session = await page.context().newCDPSession(page);
  await session.send("Network.enable");
  await session.send("Network.setCacheDisabled", { cacheDisabled: true });
  const assets = new Map<string, { bytes: number; css: boolean; canvas: boolean; status: number }>();
  const pending: Promise<void>[] = [];
  const cssRequests: string[] = [];
  page.on("response", response => {
    const url = new URL(response.url());
    if (!url.pathname.startsWith("/_content/Elsa.Studio.Workflows/") || !/\.(css|js)$/.test(url.pathname)) return;
    if (url.pathname.endsWith(".css")) cssRequests.push(url.href);
    pending.push((async () => {
      const body = await response.body();
      const css = url.pathname.endsWith(".css");
      assets.set(url.href, { bytes: body.length, css, canvas: css && body.toString().includes(".react-flow__renderer"), status: response.status() });
    })());
  });
  await page.goto("/workflows/definitions");
  await expect(page.getByRole("region", { name: "Workflow library organization" })).toBeVisible({ timeout: 60_000 });
  await page.waitForLoadState("networkidle");
  await Promise.all(pending);
  const landing = [...assets.entries()];
  expect(landing.filter(([, asset]) => asset.css).length).toBeGreaterThan(0);
  expect(landing.every(([, asset]) => asset.status === 200)).toBe(true);
  expect(landing.some(([, asset]) => asset.canvas)).toBe(false);
  expect(landing.reduce((total, [, asset]) => total + asset.bytes, 0)).toBeLessThanOrEqual(402_000);
  expect(cssRequests.every(url => /\/module-[A-Za-z0-9_-]+\.css$/.test(url))).toBe(true);
  expect(new Set(cssRequests).size).toBe(cssRequests.length);

  await page.getByRole("button", { name: "Create workflow", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Create Workflow", exact: true });
  await dialog.getByLabel("Display name").fill(`Stylesheet qualification ${Date.now()}`);
  const flowchart = dialog.getByRole("radio", { name: "Flowchart", exact: true });
  await expect(flowchart).toBeEnabled();
  await flowchart.focus();
  await flowchart.press("Space");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const renderer = page.locator(".react-flow__renderer").first();
  await expect(renderer).toBeVisible();
  await page.waitForLoadState("networkidle");
  await Promise.all(pending);
  expect([...assets.values()].some(asset => asset.canvas)).toBe(true);
  expect([...assets.values()].every(asset => asset.status === 200)).toBe(true);
  await expect(renderer).toHaveCSS("position", "absolute");
  expect(new Set(cssRequests).size).toBe(cssRequests.length);
  await testInfo.attach("normal-host-styles", { body: JSON.stringify({ landing, all: [...assets], cssRequests }, null, 2), contentType: "application/json" });
  await testInfo.attach("real-canvas", { body: await page.screenshot(), contentType: "image/png" });
});
