import { writeFileSync } from "node:fs";
import { expect, test, type Page, type TestInfo } from "@playwright/test";

// The Core Preview journeys (tools/ux-acceptance/core_preview.py): a Workbench whose default shell serves its
// publications on the kernel, on a throwaway PostgreSQL store, with Studio and a local HTTP stub.
const backend = process.env.UX_BACKEND_URL;
const stub = process.env.UX_HTTP_STUB_URL;
if (process.env.UX_ACCEPTANCE_SUITE !== "core-preview" || !backend || !stub) {
  throw new Error("Run the Core Preview journeys through tools/ux-acceptance/core_preview.py.");
}

async function capture(page: Page, info: TestInfo, name: string) {
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function receipt(info: TestInfo, name: string, value: unknown) {
  const path = info.outputPath(name);
  writeFileSync(path, JSON.stringify(value, null, 2) + "\n");
  await info.attach(name, { path, contentType: "application/json" });
}

async function signIn(page: Page) {
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
}

const browserErrors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => {
  const errors: string[] = [];
  browserErrors.set(page, errors);
  page.on("pageerror", error => errors.push(error.message));
});
test.afterEach(async ({ page }) => {
  expect(browserErrors.get(page), "Uncaught browser errors").toEqual([]);
});

test("core preview serves kernel publications", async ({ page, request }, info) => {
  await signIn(page);
  await capture(page, info, "reference");

  // The kernel's HTTP edge takes /workflows/http on this host: a route no publication serves is its own 404.
  const unserved = await request.post(`${backend}/workflows/http/ux-core-preview-unserved`, { data: {} });
  const unservedBody = await unserved.text();
  expect(unserved.status()).toBe(404);
  expect(unservedBody).toBe("no workflow serves this route");

  // The local stub the workflows' outbound requests go to answers, and records what it got.
  const answered = await request.post(`${stub}/core-preview/smoke`, { data: { probe: "smoke" } });
  const stubBody = await answered.json() as { ok: boolean; method: string; path: string; body: string };
  expect(answered.status()).toBe(200);
  expect(stubBody).toMatchObject({ ok: true, method: "POST", path: "/core-preview/smoke" });
  expect(JSON.parse(stubBody.body)).toEqual({ probe: "smoke" });

  await receipt(info, "core-preview-smoke.json", {
    tenant: process.env.UX_CORE_PREVIEW_TENANT,
    kernelEdge: { route: "/workflows/http/ux-core-preview-unserved", status: unserved.status(), body: unservedBody },
    stub: { status: answered.status(), answer: stubBody }
  });
});
