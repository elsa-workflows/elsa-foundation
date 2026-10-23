import { flushSync } from "react-dom";
import { vi } from "vitest";
import { createEndpointContext, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { capabilityDocument, definition, renderRegisteredRoute, response } from "./routeRenderingHelpers";

export { definition, response };

export function pagedDefinitionCapabilities({ folders = false }: { folders?: boolean } = {}) {
  const capabilities = capabilityDocument();
  capabilities.capabilities[0].links.push({ rel: "workflow-definitions-page", href: "design/workflows/definition-pages" });
  if (folders) capabilities.capabilities[0].links.push({ rel: "workflow-folders", href: "design/workflows/folders" });
  return capabilities;
}

export function workflowEditorResponse(url: URL) {
  if (url.pathname.endsWith("/definitions/definition-1")) {
    return response({
      definition: definition(),
      draft: { id: "draft-1", definitionId: "definition-1", state: { variables: [], rootActivity: null, inputs: [], outputs: [] }, layout: [], validationErrors: [] },
      versions: []
    });
  }
  if (url.pathname.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
  if (url.pathname.includes("/activities")) return response({ activities: [] });
  return undefined;
}

export async function renderWorkflowRoute(
  path: string,
  fetchMock: typeof fetch,
  capabilities = pagedDefinitionCapabilities(),
  options: { followNavigation?: boolean; historyState?: unknown } = {}
) {
  vi.stubGlobal("fetch", fetchMock);
  let context = createEndpointContext("https://server.example/");
  const rendered = await renderRegisteredRoute(path, (api: ElsaStudioModuleApi) => {
    // Use the public SDK HTTP client so denial cases exercise StudioHttpError.status.
    Object.defineProperty(api, "backend", { get: () => context });
  }, options.followNavigation, capabilities, options.historyState);
  return {
    ...rendered,
    updateEndpoint(baseUrl: string) {
      context = createEndpointContext(baseUrl);
      rendered.rerender();
    }
  };
}

export function text(container: ParentNode, value: string) {
  return container.textContent?.includes(value) ?? false;
}

export function button(container: ParentNode, label: string) {
  return Array.from(container.querySelectorAll("button"))
    .find(candidate => candidate.textContent?.trim() === label || candidate.getAttribute("aria-label") === label) ?? null;
}

export function input(container: ParentNode, label: string) {
  return Array.from(container.querySelectorAll<HTMLInputElement>("input"))
    .find(candidate => candidate.getAttribute("aria-label") === label) ?? null;
}

export function row(container: ParentNode, name: string) {
  return Array.from(container.querySelectorAll<HTMLElement>(".wf-grid-row[role='row']"))
    .find(candidate => candidate.textContent?.includes(name)) ?? null;
}

export async function click(element: Element | null) {
  if (!(element instanceof HTMLElement)) throw new Error("Element not found");
  flushSync(() => element.click());
  await settleReact();
}

export async function fill(element: HTMLInputElement | null, value: string) {
  if (!element) throw new Error("Input not found");
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  flushSync(() => {
    setter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await settleReact();
}

export async function select(element: HTMLSelectElement | null, value: string) {
  if (!element) throw new Error("Select not found");
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
  flushSync(() => {
    setter?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await settleReact();
}

export async function waitForText(container: ParentNode, value: string) {
  await vi.waitFor(() => {
    if (!text(container, value)) throw new Error(`Timed out waiting for text: ${value}`);
  }, { timeout: 5_000, interval: 20 });
}

export async function waitForCondition(assertion: () => void) {
  await vi.waitFor(assertion, { timeout: 5_000, interval: 20 });
}

export async function settleReact() {
  await Promise.resolve();
  flushSync(() => {});
  await Promise.resolve();
}
