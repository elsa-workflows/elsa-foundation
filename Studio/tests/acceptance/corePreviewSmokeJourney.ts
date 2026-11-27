import { expect, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";

export type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
export type Receipt = (info: TestInfo, name: string, value: unknown) => Promise<void>;

export type CorePreviewStack = { backend: string; stub: string; tenant: string | undefined };

/**
 * The owned Core Preview stack is ready: Studio signs in, the Workbench's kernel edge answers /workflows/http itself,
 * and the local stub the workflows' outbound requests go to answers and records what it got. It proves the stack the
 * kernel journeys run on, not a workflow run.
 */
export async function corePreviewSmokeJourney(page: Page, request: APIRequestContext, info: TestInfo, stack: CorePreviewStack,
  capture: Capture, receipt: Receipt) {
  await capture(page, info, "reference");

  // The kernel's HTTP edge takes /workflows/http on this host: a route no publication serves is its own 404.
  const unserved = await request.post(`${stack.backend}/workflows/http/ux-core-preview-unserved`, { data: {} });
  const unservedBody = await unserved.text();
  expect(unserved.status()).toBe(404);
  expect(unservedBody).toBe("no workflow serves this route");

  const answered = await request.post(`${stack.stub}/core-preview/smoke`, { data: { probe: "smoke" } });
  const stubBody = await answered.json() as { ok: boolean; method: string; path: string; body: string };
  expect(answered.status()).toBe(200);
  expect(stubBody).toMatchObject({ ok: true, method: "POST", path: "/core-preview/smoke" });
  expect(JSON.parse(stubBody.body)).toEqual({ probe: "smoke" });

  await receipt(info, "core-preview-smoke.json", {
    tenant: stack.tenant,
    kernelEdge: { route: "/workflows/http/ux-core-preview-unserved", status: unserved.status(), body: unservedBody },
    stub: { status: answered.status(), answer: stubBody }
  });
}
