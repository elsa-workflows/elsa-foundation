import { expect, type APIRequestContext, type Page, type Request, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";

const pendingDashboardCleanup = new WeakMap<Page, () => Promise<void>>();

/** Teardown runs in afterEach's own budget even if an interaction used its test timeout. */
export function registerDashboardCleanup(page: Page, cleanup: () => Promise<void>) {
  pendingDashboardCleanup.set(page, cleanup);
}
export async function cleanupDashboardJourney(page: Page) {
  const cleanup = pendingDashboardCleanup.get(page);
  if (cleanup) { await cleanup(); pendingDashboardCleanup.delete(page); }
}

export type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
export type Resource = { id: string; revision: string; name: string; description: string; definition?: unknown; definitionState: string; ownerId: string; capabilities: { canRead: boolean; canEdit: boolean; canShare: boolean; canDelete: boolean }; tiles: { id: string; questionId?: string; size: string; referenceState: string }[] };
export type Result = { outcome: string; coverage: { status: string }; rows: { values: Record<string, { value: string | null }>; drill?: unknown }[] };

/** Every call reaches the owned source-built backend, using its normal password/token protocol. */
export async function dashboardBackend(request: APIRequestContext) {
  const base = process.env.UX_BACKEND_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned backend URL is required");
  const login = await request.post(`${base}/_elsa/identity/login`, { data: { username: "admin", password: "Password123!" } });
  expect(login.ok()).toBe(true);
  const exchanged = await request.get(`${base}/_elsa/identity/token`); expect(exchanged.ok()).toBe(true);
  const { accessToken } = await exchanged.json() as { accessToken: string };
  expect(typeof accessToken).toBe("string");
  const headers = { Authorization: `Bearer ${accessToken}` };
  return {
    base,
    async response(path: string, method = "GET", data?: unknown, extra?: Record<string, string>) {
      return request.fetch(`${base}${path}`, { method, data, headers: { ...headers, ...extra } });
    },
    async api<T>(path: string, method = "GET", data?: unknown, extra?: Record<string, string>): Promise<T> {
      const response = await request.fetch(`${base}${path}`, { method, data, headers: { ...headers, ...extra } });
      expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
      return response.status() === 204 ? undefined as T : await response.json() as T;
    }
  };
}

export type Backend = Awaited<ReturnType<typeof dashboardBackend>>;
export const tag = (revision: string) => ({ "If-Match": `"${revision}"` });

/** No direct store writes or intercepted query responses: publish and execute three actual runs. */
export async function seedDashboardRuns(backend: Backend, suffix: string) {
  const key = "Elsa.Activities.Primitives.Activities.WriteLine";
  const catalog = await backend.api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
  const activity = catalog.items.find(item => item.definition.activityTypeKey === key); expect(activity).toBeTruthy();
  const submitted = await backend.api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
    name: `Dashboard native oracle ${suffix}`, state: { variables: [], inputs: [], outputs: [], rootActivity: {
      nodeId: "native-oracle", activityVersionId: activity!.definition.headVersionId,
      inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: "Owned Dashboard qualification run" } }], outputs: []
    } }
  });
  try {
    const published = await backend.api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    const ids: string[] = [];
    const observedInstances: { workflowExecutionId: string; status: string }[] = [];
    for (let index = 0; index < 3; index++) {
      const run = await backend.api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
      ids.push(run.workflowExecutionId);
      await expect.poll(async () => (await backend.api<{ instance: { status: string } }>(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
      observedInstances.push({ workflowExecutionId: run.workflowExecutionId, status: (await backend.api<{ instance: { status: string } }>(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status });
    }
    return { definitionId: submitted.definition.id, executionIds: ids, observedInstances, published };
  } catch (error) {
    await backend.api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); throw error;
  }
}

/** Counts only actual native browser requests that would submit a generic Weaver turn. */
export function observeNativeAgentBoundary(page: Page) {
  const attempts: string[] = [];
  const collect = (request: Request) => {
    if (request.method() === "POST" && /\/_elsa\/agent\/sessions\/[^/]+\/messages(?:\?|$)/.test(request.url())) attempts.push(request.url());
  };
  page.on("request", collect);
  return {
    async verify(info: TestInfo, prefix = "native") {
      expect(attempts, "Native editing/save/refresh/drill must submit no generic Agent message").toEqual([]);
      await attachDashboardJson(info, `${prefix}-agent-boundary.json`, { genericAgentMessagePosts: attempts.length, provider: "disabled in private owned host", physicalAdapterCalls: "not measured by browser instrumentation" });
    },
    dispose: () => page.off("request", collect)
  };
}

export async function attachDashboardJson(info: TestInfo, name: string, value: unknown) {
  const path = info.outputPath(name); writeFileSync(path, JSON.stringify(value, null, 2));
  await info.attach(name, { path, contentType: "application/json" });
}

export async function signInDashboardReader(page: Page, info: TestInfo) {
  await page.addInitScript(mode => { localStorage.setItem("elsa-studio-theme-mode", mode); localStorage.setItem("elsa-studio-workspace-style", "linen"); }, info.project.use.colorScheme ?? "light");
  await page.goto("/questions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("dashboard-reader");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Questions", level: 2, exact: true })).toBeVisible({ timeout: 60_000 });
}
