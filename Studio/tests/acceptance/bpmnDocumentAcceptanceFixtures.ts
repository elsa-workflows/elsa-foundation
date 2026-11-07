import { readFileSync, writeFileSync } from "node:fs";
import { expect, type Page, type TestInfo } from "@playwright/test";

export type Node = {
  nodeId: string;
  activityVersionId: string;
  inputs: unknown[];
  outputs: unknown[];
  structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } | null;
  [key: string]: unknown;
};
export type Element = { elementId: string; elementType: string; name?: string; childNodeId?: string; defaultFlowId?: string };
export type Flow = { flowId: string; sourceRef: string; targetRef: string; conditionOutcome?: string | null; isDefault?: boolean };
export type Layout = { nodeId: string; x: number; y: number; width?: number; height?: number };
export type Draft = { id: string; definitionId: string; state: { rootActivity: Node; inputs: unknown[]; variables: unknown[]; outputs: unknown[] }; layout: Layout[] };
export type Submitted = { definition: { id: string }; draftId: string; version: { id: string } };
export type Published = { artifactId: string; sourceReferenceId: string };
export type Instance = {
  instance: { status: string; definitionId: string; definitionVersionId: string; sourceReferenceId: string };
  activities: { activityExecutionId: string; executableNodeId: string; authoredActivityId: string; status: string }[];
  incidents: unknown[];
  outputs: Record<string, { value: unknown; isRedacted: boolean }>;
};
export type Api = <T>(path: string, method?: string, data?: unknown) => Promise<T>;


const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });

export async function bpmnPrototypeGraph(api: Api) {
  async function version(type: string) {
    const list = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const item = list.items.find(item => item.definition.activityTypeKey === type);
    expect(item, `Real catalog supplies ${type}`).toBeTruthy();
    return item!.definition.headVersionId;
  }
  const [bpmnVersion, decisionVersion, inlineVersion, sequenceVersion] = await Promise.all([
    version("Elsa.Activities.Bpmn.Activities.BpmnProcess"), version("Elsa.Activities.Bpmn.Activities.BpmnDecision"),
    version("Elsa.Activities.Primitives.Activities.Inline"), version("Elsa.Activities.Sequence.Activities.Sequence")
  ]);
  const inline = (nodeId: string, expression: string): Node => ({ nodeId, activityVersionId: inlineVersion, structure: null,
    inputs: [{ referenceKey: "Expression", value: { expressionType: "JavaScript", value: expression } }], outputs: [] });
  const captureResult = (nodeId: string, producerNodeId: string, producerScopeId: string): Node => ({
    nodeId, activityVersionId: "elsa.intrinsic.set-output@1", structure: null, outputs: [],
    intrinsic: { kind: "setOutput", valueType: { alias: "Elsa.Any", collectionKind: "single" } },
    inputs: [literal("name", "result"), { referenceKey: "value", value: {
      expressionType: "ActivityResult", value: { producerNodeId, producerScopeId, projectionKey: "$result" }
    } }]
  });
  const nested: Node = { nodeId: "nested-low", activityVersionId: sequenceVersion, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: {
      activities: [inline("low-inline", '"Nested low"'), captureResult("capture-low", "low-inline", "nested-low")]
    } } };
  // Required ActivityResult ordering is currently established by Sequence, not by the BPMN
  // validator. Keep the real Inline result/capture in that supported scope; attempt3 separately
  // retains the rejected direct-BPMN-sibling case rather than making its reference optional.
  const nestedHigh: Node = { nodeId: "nested-high", activityVersionId: sequenceVersion, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: {
      activities: [inline("high", '"Original high"'), captureResult("capture-high", "high", "nested-high")]
    } } };
  const elements: Element[] = [
    { elementId: "start", elementType: "startEvent" },
    { elementId: "gateway", elementType: "exclusiveGateway", name: "Order routing", childNodeId: "decision", defaultFlowId: "high-flow" },
    { elementId: "high-task", elementType: "subProcess", name: "High", childNodeId: "nested-high" },
    { elementId: "low-task", elementType: "subProcess", name: "Nested low", childNodeId: "nested-low" },
    { elementId: "end-high", elementType: "endEvent" }, { elementId: "end-low", elementType: "endEvent" }
  ];
  const sequenceFlows: Flow[] = [
    { flowId: "enter", sourceRef: "start", targetRef: "gateway" },
    { flowId: "high-flow", sourceRef: "gateway", targetRef: "high-task", isDefault: true },
    { flowId: "low-flow", sourceRef: "gateway", targetRef: "low-task", conditionOutcome: "Low" },
    { flowId: "high-end-flow", sourceRef: "high-task", targetRef: "end-high" },
    { flowId: "low-end-flow", sourceRef: "low-task", targetRef: "end-low" }
  ];
  const root: Node = { nodeId: "process", activityVersionId: bpmnVersion, inputs: [], outputs: [], structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      activities: [{ nodeId: "decision", activityVersionId: decisionVersion, structure: null, inputs: [{ referenceKey: "Outcome", value: {
        expressionType: "WorkflowRequest", value: { memberKey: "route" }
      } }], outputs: [] }, nestedHigh, nested],
      elements, sequenceFlows
    }
  } };
  const layout: Layout[] = [
    { nodeId: "start", x: 60, y: 220 }, { nodeId: "gateway", x: 240, y: 210 },
    { nodeId: "high-task", x: 440, y: 100 },
    { nodeId: "low-task", x: 440, y: 360 }, { nodeId: "end-high", x: 700, y: 120 }, { nodeId: "end-low", x: 700, y: 380 },
    { nodeId: "high", x: 120, y: 120 }, { nodeId: "capture-high", x: 360, y: 120 },
    { nodeId: "low-inline", x: 120, y: 120 }, { nodeId: "capture-low", x: 360, y: 120 }
  ];
  const inputs = [{ referenceKey: "route", name: "route", displayName: "Route", isNullable: true,
    type: { alias: "String", collectionKind: "single" }, storageDriverType: null }];
  return { root, layout, inputs };
}

export async function seedBpmnPrototype(api: Api, suffix: string) {
  const { root, layout, inputs } = await bpmnPrototypeGraph(api);
  const submitted = await api<Submitted>("/design/workflows/definitions/submit", "POST", {
    name: `BPMN same-process ${suffix}`, state: { rootActivity: root, variables: [], outputs: [], inputs }
  });
  const initial = await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`);
  const baseline = await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`, "PUT", { state: initial.state, layout });
  return { submitted, baseline };
}

export type Editor = "integrated" | "bpmn-js";
export const native = (page: Page) => page.getByRole("region", { name: "bpmn-js process editor", exact: true });
export const properties = (page: Page) => page.getByRole("region", { name: "BPMN properties", exact: true });
export const nativeShape = (page: Page, id: string) => native(page).locator(`.djs-shape[data-element-id='${id}']`);
export const integratedShape = (page: Page, id: string) => page.locator(`[data-bpmn-document-canvas] .react-flow__node[aria-label*='BPMN element ${id}.']`);
const canvasFailures = new WeakMap<Page, string[]>();
/** Node-side evidence survives document reloads and navigation; the browser bridge only observes UI. */
export async function readCanvasFailures(page: Page): Promise<string[]> {
  if (!page.isClosed()) await page.evaluate(async () => {
    await Promise.all((window as unknown as { __elsaBpmnCanvasReports?: Promise<unknown>[] }).__elsaBpmnCanvasReports ?? []);
  }).catch(() => undefined);
  return [...canvasFailures.get(page) ?? []];
}

export async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const path = info.outputPath(`${name}.png`); await page.screenshot({ path, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`); writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}
export async function attachment(info: TestInfo, name: string, value: unknown) {
  await info.attach(name, { body: JSON.stringify(value, null, 2), contentType: "application/json" });
}
export async function login(page: Page, info: TestInfo): Promise<Api> {
  const failures: string[] = []; canvasFailures.set(page, failures);
  await page.exposeFunction("__elsaReportBpmnCanvasFailure", (status: string, path: string) => { failures.push(`${status}: ${path}`); });
  await page.addInitScript(mode => { localStorage.setItem("elsa-studio-theme-mode", mode); localStorage.setItem("elsa-studio-workspace-style", "linen"); }, info.project.use.colorScheme ?? "light");
  await page.addInitScript(() => {
    const state = window as unknown as { __elsaBpmnUnexpectedRefusals: string[]; __elsaBpmnCanvasReports: Promise<unknown>[];
      __elsaReportBpmnCanvasFailure(status: string, path: string): Promise<void> };
    state.__elsaBpmnUnexpectedRefusals = []; state.__elsaBpmnCanvasReports = [];
    const report = (status: string | null | undefined) => {
      if (status !== "refused" && status !== "failed") return;
      state.__elsaBpmnUnexpectedRefusals.push(`Native canvas entered ${status} during normal authoring`);
      state.__elsaBpmnCanvasReports.push(state.__elsaReportBpmnCanvasFailure(status, location.pathname + location.search).catch(() => undefined));
    };
    const inspectAdded = (node: unknown) => {
      if (!(node instanceof window.Element)) return;
      if (node.matches("[data-bpmn-js-state]")) report(node.getAttribute("data-bpmn-js-state"));
      node.querySelectorAll("[data-bpmn-js-state]").forEach(element => report(element.getAttribute("data-bpmn-js-state")));
    };
    new MutationObserver(records => {
      for (const record of records) {
        if (record.type === "childList") record.addedNodes.forEach(inspectAdded);
        else if (record.type === "attributes") { report(record.oldValue); report((record.target as HTMLElement).getAttribute("data-bpmn-js-state")); }
      }
    }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ["data-bpmn-js-state"], attributeOldValue: true });
  });
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin"); await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
  const api: Api = async <T>(path: string, method = "GET", data?: unknown): Promise<T> => {
    const response = await page.request.fetch(`${process.env.ELSA_WORKBENCH_URL}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  };
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  expect(await api("/interchange/bpmn/authoring-capabilities")).toEqual({ structureKind: "elsa.bpmn.structure", schemaVersion: "2.0.0" });
  await info.attach("source", { body: process.env.UX_SOURCE_SHA ?? "unknown", contentType: "text/plain" });
  return api;
}
export async function choose(page: Page, editor: Editor) {
  await page.getByRole("combobox", { name: "BPMN editor", exact: true }).selectOption(editor);
  if (editor === "bpmn-js") await expect(native(page)).toHaveAttribute("data-bpmn-js-state", "ready");
  else await expect(page.locator("[data-bpmn-document-canvas]")).toBeVisible();
}
export async function select(page: Page, editor: Editor, id: string) {
  if (editor === "bpmn-js") {
    await nativeShape(page, id === "end-high" ? "start" : "end-high").locator(".djs-hit").click();
    await nativeShape(page, id).locator(".djs-hit").click();
  }
  else await integratedShape(page, id).click();
  await expect(properties(page)).toContainText(id);
}
export async function label(page: Page, editor: Editor, id: string, name: string, apply = true) {
  await select(page, editor, id);
  if (editor === "bpmn-js") {
    await nativeShape(page, id).locator(".djs-hit").dblclick();
    const input = page.locator(".djs-direct-editing-content[contenteditable='true']");
    await input.fill(name); if (apply) await input.press("Enter");
  } else {
    await properties(page).getByRole("button", { name: "Edit label", exact: true }).click();
    await properties(page).getByLabel("Label", { exact: true }).fill(name);
    if (apply) await properties(page).getByRole("button", { name: "Apply BPMN property", exact: true }).click();
  }
}
export async function savedName(page: Page, source: string, id: string) {
  return page.evaluate(({ source, id }) => new DOMParser().parseFromString(source, "application/xml").querySelector(`[id='${id}']`)?.getAttribute("name"), { source, id });
}
export async function disableAutosave(page: Page) {
  await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
  const toggle = page.getByRole("menuitemcheckbox", { name: /^Autosave/ });
  if (await toggle.getAttribute("aria-checked") === "true") await toggle.click();
  await page.keyboard.press("Escape");
}
export async function noRefusals(page: Page) {
  expect(await readCanvasFailures(page)).toEqual([]);
}
export async function exportXml(page: Page, info: TestInfo, name: string) {
  await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
  const downloading = page.waitForEvent("download"); await page.getByRole("menuitem", { name: "Export BPMN", exact: true }).click();
  const path = info.outputPath(name); await (await downloading).saveAs(path);
  await info.attach(name, { path, contentType: "application/xml" }); return readFileSync(path, "utf8");
}
