import { flushSync } from "react-dom";
import { expect, it, vi } from "vitest";
import type { StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { bpmnActivity } from "./fixtures";
import { capabilityDocument, definition, renderRegisteredRoute, response } from "./routeRenderingHelpers";

async function editor(direct: boolean, service = false) {
  const root = { nodeId: "root", activityVersionId: bpmnActivity.activityVersionId, inputs: [], outputs: [], structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: { activities: [], elements: [{ elementId: "keep", name: "Keep this task", elementType: "task" }], sequenceFlows: [] }
  } };
  const draft = { id: "draft-1", definitionId: "definition-1", sourceVersionId: null, state: { rootActivity: root, inputs: [], outputs: [], variables: [] }, layout: [], activityPresentation: [], validationErrors: [] };
  const capabilities = capabilityDocument();
  if (direct) capabilities.capabilities.push({ id: "elsa.api.bpmn-publishing", contractVersion: "1", links: [
    { rel: "bpmn-authored-publish", href: "interchange/bpmn/versions/{versionId}/publish-direct", templated: true },
    ...(service ? [{ rel: "bpmn-authored-service-publish", href: "interchange/bpmn/versions/{versionId}/publish-direct", templated: true }] : [])] });
  if (service) capabilities.capabilities[0].links.push({ rel: "workflow-definitions-page", href: "design/workflows/definitions/page" });
  const calls = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (service && url.includes("/definitions/page?")) return response({ items: [{ ...definition(), id: "worker-1", name: "ETL service worker" }], nextContinuationToken: null });
    if (service && url.includes("/definitions/worker-1")) return response({ definition: { ...definition(), id: "worker-1", name: "ETL service worker" }, draft: null, versions: [] });
    if (service && url.endsWith("activation-slots/worker-1")) return response({ items: [{ slotId: "slot-1", definitionId: "worker-1", slotName: "Published", activeActivationId: "publication-1", sourceKind: "publishing", sourceId: "publication-1" }] });
    if (service && url.endsWith("publications/publication-1")) return response({ publicationId: "publication-1", definitionId: "worker-1", versionId: "worker-version", slotName: "Published", sourceReferenceId: "worker-source", artifactId: "worker-artifact", status: "active" });
    if (service && url.includes("executables/worker-artifact?ref=worker-source")) return response({ artifactId: "worker-artifact", artifactHash: "a".repeat(64), chosenReference: { sourceReferenceId: "worker-source", selection: "requested", layout: [] },
      references: [{ sourceReferenceId: "worker-source", artifactId: "worker-artifact", definitionId: "worker-1", definitionVersionId: "worker-version", artifactVersion: "1.0.0", scope: "Published" }] });
    if (url.endsWith("interchange/bpmn/import")) return response({ processNode: { ...root, structure: { ...root.structure, payload: { activities: [], elements: [], sequenceFlows: [] } } }, analysis: { processIds: ["process"], elementCounts: {}, issues: [{ severity: "Dropped", message: "Unsupported event would be lost" }] } });
    if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft, versions: [] });
    if (url.includes("/drafts/draft-1") && init?.method === "PUT") return response(draft);
    if (url.includes("/activities/catalog") || url.includes("/activities")) return response({ activities: [bpmnActivity] });
    return response({ items: [] });
  });
  vi.stubGlobal("fetch", calls);
  const rendered = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
    api.runtime.workflows = { autosaveEnabledByDefault: false };
    Object.defineProperty(api, "backendSession", { value: { key: "session", profile: { id: "backend", baseUrl: api.backend.baseUrl, name: "Backend", source: "configuration", environment: "Test" },
      context: api.backend, identity: { status: "authenticated", subject: "author", roles: [], permissions: [] }, capabilities, disposed: false,
      pendingWrites: 0, refreshCapabilities: async () => capabilities } satisfies StudioBackendSession });
  }, false, capabilities);
  await vi.waitFor(() => expect(rendered.container.querySelector(".wf-editor-toolbar")).not.toBeNull(), { timeout: 5000 });
  return { ...rendered, calls };
}

it.each([false, true])("offers direct publication and a human-wait palette only when the backend supports them: %s", async direct => {
  const view = await editor(direct);
  await vi.waitFor(() => expect(view.container.querySelector(".react-flow__node[data-id='keep']")).not.toBeNull());
  const category = view.container.querySelector<HTMLButtonElement>(".wf-palette-category-toggle[data-category='BPMN']");
  if (category?.getAttribute("aria-expanded") === "false") flushSync(() => category.click());
  const buttons = [...view.container.querySelectorAll("button")];
  expect(buttons.some(button => button.textContent?.trim() === "Publish BPMN")).toBe(direct);
  expect(view.container.textContent?.includes("Human wait")).toBe(direct);
  // The established native publication remains available alongside the optional action.
  expect(view.container.querySelector("[aria-label='Workflow publication actions']")).not.toBeNull();
  await view.unmount();
});

it.each([false, true])("offers the service binding editor only with explicit authored-service support: %s", async service => {
  const view = await editor(true, service);
  const node = await vi.waitFor(() => { const node = view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']"); expect(node).not.toBeNull(); return node!; });
  flushSync(() => node.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await vi.waitFor(() => expect(view.container.querySelector("[aria-label='Task execution']")).not.toBeNull());
  expect([...view.container.querySelectorAll("button")].some(button => button.textContent === "Bind a published service workflow")).toBe(service);
  await view.unmount();
});

it("selects an immutable service publication through the real picker, undo, redo and normal save", async () => {
  const view = await editor(true, true);
  const node = await vi.waitFor(() => { const node = view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']"); expect(node).not.toBeNull(); return node!; });
  flushSync(() => node.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  const bind = await vi.waitFor(() => {
    const button = [...view.container.querySelectorAll("button")].find(button => button.textContent === "Bind a published service workflow"); expect(button).toBeDefined(); return button!;
  });
  flushSync(() => bind.click());
  const search = await vi.waitFor(() => {
    const input = view.container.querySelector<HTMLInputElement>("[role='combobox'][aria-label='Workflow']"); expect(input).not.toBeNull(); return input!;
  });
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(search, "ETL service");
    search.dispatchEvent(new Event("input", { bubbles: true }));
  });
  const worker = await vi.waitFor(() => {
    const option = view.container.querySelector<HTMLElement>("[role='option']"); expect(option?.textContent).toContain("ETL service worker"); return option!;
  });
  flushSync(() => worker.click());
  const publications = await vi.waitFor(() => {
    const select = view.container.querySelector<HTMLSelectElement>("[aria-label='Service publication']"); expect(select?.options.length).toBe(2); return select!;
  });
  flushSync(() => { publications.value = "slot-1"; publications.dispatchEvent(new Event("change", { bubbles: true })); });
  flushSync(() => [...view.container.querySelectorAll("button")].find(button => button.textContent === "Use this service publication")!.click());
  await vi.waitFor(() => expect(view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")?.value).toBe("service"));
  expect(view.container.querySelector(".react-flow__node[data-id='keep']")?.textContent).toContain("Native service");
  flushSync(() => view.container.querySelector<HTMLButtonElement>("[aria-label='Undo']")!.click());
  flushSync(() => view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']")!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await vi.waitFor(() => expect(view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")?.value).toBe("unbound"));
  flushSync(() => view.container.querySelector<HTMLButtonElement>("[aria-label='Redo']")!.click());
  flushSync(() => view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']")!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await vi.waitFor(() => expect(view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")?.value).toBe("service"));
  flushSync(() => [...view.container.querySelectorAll("button")].find(button => button.textContent?.trim() === "Save")!.click());
  await vi.waitFor(() => expect(view.calls.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(true));
  const body = JSON.parse(view.calls.mock.calls.find(([, init]) => init?.method === "PUT")![1]!.body as string);
  const saved = body.state.rootActivity.structure.payload.elements[0];
  expect(saved.elementType).toBe("serviceTask");
  expect(JSON.parse(saved.properties["elsa.direct.binding"])).toMatchObject({ sourceReferenceId: "worker-source", executable: { definitionId: "worker-1", definitionVersionId: "worker-version", artifactHash: "a".repeat(64) }, faultPolicy: { disposition: "HoldForRetry", maxAttempts: 3 } });
  expect(view.calls.mock.calls.filter(([url]) => String(url).includes("/definitions/page?")).every(([url]) => String(url).includes("pageSize=20"))).toBe(true);
  await view.unmount();
});

it("refuses a lossy direct import before replacing the actual editor document and retains its original file", async () => {
  const view = await editor(true);
  const source = new File(["<bpmn:definitions>original unsupported event</bpmn:definitions>"], "original.bpmn", { type: "application/xml" });
  Object.defineProperty(source, "text", { value: async () => "<bpmn:definitions>original unsupported event</bpmn:definitions>" });
  const input = view.container.querySelector<HTMLInputElement>("[aria-label='Import BPMN file']")!;
  Object.defineProperty(input, "files", { value: [source] });
  flushSync(() => input.dispatchEvent(new Event("change", { bubbles: true })));
  await vi.waitFor(() => expect(view.container.textContent).toContain("current process is unchanged"));
  expect(view.container.querySelector(".react-flow__node[data-id='keep']")).not.toBeNull();
  const menu = view.container.querySelector<HTMLButtonElement>("[aria-label='More workflow actions']")!;
  flushSync(() => menu.click());
  await vi.waitFor(() => expect(document.body.textContent).toContain("Download refused BPMN source"));
  expect(view.calls.mock.calls.filter(([, init]) => init?.method === "PUT")).toHaveLength(0);
  await view.unmount();
});

it("edits human binding through normal undo, redo and save in the actual editor", async () => {
  const view = await editor(true);
  const node = await vi.waitFor(() => {
    const node = view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']"); expect(node).not.toBeNull(); return node!;
  });
  flushSync(() => node.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  const select = await vi.waitFor(() => {
    const select = view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']"); expect(select).not.toBeNull(); return select!;
  });
  expect(select.value).toBe("unbound");
  flushSync(() => { select.value = "human"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  await vi.waitFor(() => expect(view.container.querySelector<HTMLButtonElement>("[aria-label='Undo']")?.disabled).toBe(false));
  flushSync(() => view.container.querySelector<HTMLButtonElement>("[aria-label='Undo']")!.click());
  flushSync(() => view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']")!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await vi.waitFor(() => expect(view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")?.value).toBe("unbound"));
  flushSync(() => view.container.querySelector<HTMLButtonElement>("[aria-label='Redo']")!.click());
  flushSync(() => view.container.querySelector<HTMLElement>(".react-flow__node[data-id='keep']")!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await vi.waitFor(() => expect(view.container.querySelector<HTMLSelectElement>("[aria-label='Task execution']")?.value).toBe("human"));
  const save = [...view.container.querySelectorAll("button")].find(button => button.textContent?.trim() === "Save")!;
  flushSync(() => save.click());
  await vi.waitFor(() => expect(view.calls.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(true));
  const saved = view.calls.mock.calls.find(([, init]) => init?.method === "PUT")!;
  expect(saved[1]?.body).toContain('"elsa.direct.binding"');
  expect(saved[1]?.body).toContain('"elementType":"userTask"');
  await view.unmount();
});
