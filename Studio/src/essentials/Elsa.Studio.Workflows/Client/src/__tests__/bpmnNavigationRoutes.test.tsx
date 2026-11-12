import { flushSync } from "react-dom";
import { expect, it, vi } from "vitest";
import type { StudioApiCapabilityDocument, StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { renderRegisteredRoute, response, testApi } from "./routeRenderingHelpers";
import { register } from "../module";

function runtimeCapabilities(): StudioApiCapabilityDocument {
  return { capabilities: [
    { id: "elsa.api.bpmn-catalog", contractVersion: "1", links: [{ rel: "bpmn-publications", href: "bpmn/publications" }, { rel: "bpmn-executions", href: "bpmn/executions" }] },
    { id: "elsa.api.bpmn-runtime", contractVersion: "1", links: [{ rel: "bpmn-admission", href: "bpmn/admission" }, { rel: "bpmn-start", href: "bpmn/executions" }] },
    { id: "elsa.api.bpmn-inspection", contractVersion: "1", links: [{ rel: "bpmn-inspection", href: "bpmn/executions/{id}/inspection", templated: true }] }
  ] };
}

it("registers independent BPMN navigation for a selected Runtime-only backend", async () => {
  const capabilities = runtimeCapabilities();
  const fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.endsWith("bpmn/admission")) return response({ admitNewExecutions: false });
    if (url.includes("bpmn/publications") || url.includes("bpmn/executions?")) return response({ items: [], nextContinuationToken: null });
    throw new Error(`Unexpected Runtime-only request ${url}`);
  });
  vi.stubGlobal("fetch", fetch);
  const page = await renderRegisteredRoute("/workflows/bpmn?backend=runtime-only", api => {
    Object.defineProperty(api, "backendSession", { value: { key: "runtime-only", profile: { id: "runtime-only", baseUrl: api.backend.baseUrl, name: "Runtime", environment: "Test", source: "configuration" },
      context: api.backend, capabilities, identity: { status: "authenticated", subject: "author", roles: [], permissions: [] }, disposed: false, pendingWrites: 0, refreshCapabilities: async () => capabilities } satisfies StudioBackendSession });
  }, true, capabilities);
  await vi.waitFor(() => expect(page.container.textContent).toContain("New starts are disabled"), { timeout: 5000 });
  const area = page.api.featureAreas.list().find(item => item.id === "workflows")!;
  const navigation = "items" in area.nav ? area.nav.items.filter(item => item.path.startsWith("/workflows/bpmn")) : [];
  expect(navigation).toHaveLength(2); expect(navigation.every(item => item.placement === "main")).toBe(true);
  expect(navigation.every(item => item.requiresBackend?.every(requirement => requirement.id === "elsa.api.bpmn-catalog"))).toBe(true);
  const runs = page.container.querySelector<HTMLAnchorElement>("nav[aria-label='BPMN views'] a[href*='/runs']")!;
  flushSync(() => runs.click());
  await vi.waitFor(() => expect(page.container.querySelector("h2")?.textContent).toBe("BPMN runs"));
  expect(window.location.search).toBe("?backend=runtime-only");
  expect(fetch.mock.calls.every(([path]) => !String(path).includes("/design/"))).toBe(true);
  await page.unmount();
});

it("leaves legacy module registration unchanged when there is no scoped SDK session", () => {
  const api = testApi(); register(api);
  expect(api.routes.list().some(route => route.path.startsWith("/workflows/bpmn"))).toBe(false);
  expect(api.routes.list().some(route => route.path === "/workflows/definitions")).toBe(true);
});

it("refuses a direct navigation whose selected backend lacks the optional capability", async () => {
  const capabilities = { capabilities: [] };
  const fetch = vi.fn(async () => { throw new Error("No domain request should be sent."); }); vi.stubGlobal("fetch", fetch);
  const page = await renderRegisteredRoute("/workflows/bpmn?backend=unsupported", api => {
    Object.defineProperty(api, "backendSession", { value: { key: "unsupported", profile: { id: "unsupported", baseUrl: api.backend.baseUrl, name: "Backend", environment: "Test", source: "configuration" },
      context: api.backend, capabilities, identity: { status: "authenticated", subject: "author", roles: [], permissions: [] }, disposed: false, pendingWrites: 0, refreshCapabilities: async () => capabilities } satisfies StudioBackendSession });
  }, false, capabilities);
  await vi.waitFor(() => expect(page.container.textContent).toContain("BPMN navigation is unavailable"));
  expect(fetch).not.toHaveBeenCalled(); await page.unmount();
});
