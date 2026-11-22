import { expect, it, vi } from "vitest";
import { flowchartActivity } from "./fixtures/workflowCatalog";
import { capabilityDocument, definition, renderRegisteredRoute, response } from "./routeRenderingHelpers";
import type { ActivityCatalogItem } from "../workflowTypes";

type CreateBody = {
  name: string;
  initialState?: { rootActivity?: { activityVersionId?: string } } | null;
};

const flowchart = flowchartActivity();

it("waits for the activity catalog before creating a workflow with its default root", async () => {
  let resolveCatalog!: (result: Response) => void;
  const pendingCatalog = new Promise<Response>(resolve => { resolveCatalog = resolve; });
  const createBodies: CreateBody[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/design/activities/catalog")) return pendingCatalog;
    if (url.endsWith("/design/workflows/definitions") && init?.method === "POST") {
      const body = JSON.parse(String(init.body)) as CreateBody;
      createBodies.push(body);
      if (!body.initialState?.rootActivity) return response({ detail: "A root activity is required." }, 400);
      return response({ definition: definition({ id: "created-definition", name: body.name }), draft: null, versions: [] });
    }
    if (url.includes("/design/workflows/definitions/created-definition")) {
      return response({ definition: definition({ id: "created-definition" }), draft: null, versions: [] });
    }
    if (url.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
    return response({ items: [definition()] });
  });
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute();

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await fill(inputByLabel(dialog(), "Display name"), "Catalog race");

  const createButton = buttonByText(dialog(), "Create");
  if (!createButton) throw new Error("Create button not found.");
  expect.soft(createButton.getAttribute("aria-disabled"), "submission should be marked unavailable while the catalog is loading").toBe("true");
  expect.soft(createButton.getAttribute("aria-describedby"), "the blocked action should reference the catalog loading status").toBe("wf-activity-catalog-loading");
  dialog().dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  await new Promise(resolve => setTimeout(resolve, 0));
  expect.soft(createBodies, "creating while the catalog is pending must not send a rootless definition").toHaveLength(0);
  if (createBodies.length > 0) {
    expect.soft(createBodies[0].initialState?.rootActivity, "the in-flight catalog currently produces no root activity").toBeDefined();
  }

  resolveCatalog(response({ activities: [flowchart] }));
  await vi.waitFor(() => expect(inputByLabel(dialog(), "Flowchart")?.disabled).toBe(false));
  await vi.waitFor(() => expect(buttonByText(dialog(), "Create")?.getAttribute("aria-disabled")).toBe("false"));
  const previousCreateCount = createBodies.length;
  await click(buttonByText(dialog(), "Create"));
  await vi.waitFor(() => expect(createBodies).toHaveLength(previousCreateCount + 1));

  const successfulBody = createBodies.at(-1);
  expect(successfulBody?.initialState?.rootActivity?.activityVersionId).toBe("flowchart-v1");
});

it.each([
  ["no root template", { authoringTemplate: undefined }, "Flowchart has no available authoring template in this catalog."],
  ["an unavailable root", { available: false }, "Flowchart is listed but unavailable in this activity catalog."]
])("does not create a workflow when the catalog has %s", async (_reason, overrides, availabilityReason) => {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/design/activities/catalog")) return response({ activities: [{ ...flowchart, ...overrides }] });
    if (url.endsWith("/design/workflows/definitions") && init?.method === "POST") {
      return response({ definition: definition({ id: "unexpected-definition" }), draft: null, versions: [] });
    }
    return response({ items: [definition()] });
  });
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute();

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await waitForText(dialog(), availabilityReason);
  expect(inputByLabel(dialog(), "Flowchart")?.disabled).toBe(true);
  await fill(inputByLabel(dialog(), "Display name"), "Unavailable starting point");

  expect(dialog().textContent).toContain(availabilityReason);
  expect(buttonByText(dialog(), "Create")?.getAttribute("aria-disabled")).toBe("true");
  dialog().dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  await new Promise(resolve => setTimeout(resolve, 0));

  expect(fetchMock.mock.calls.some(([url, init]) => String(url).endsWith("/design/workflows/definitions") && init?.method === "POST")).toBe(false);
});

it("explains missing HTTP starter activities while Blank remains selected", async () => {
  const fetchMock = catalogFetch([flowchart]);
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute();

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await waitForText(dialog(), "HTTP Endpoint is not present in the activity catalog");
  expect(dialog().querySelector<HTMLOptionElement>('[aria-label="Starting point"] option[value="http"]')?.disabled).toBe(true);

  const startingPoint = dialog().querySelector<HTMLSelectElement>('[aria-label="Starting point"]');
  const reasonId = startingPoint?.getAttribute("aria-describedby");
  expect(startingPoint?.value).toBe("blank");
  await fill(inputByLabel(dialog(), "Display name"), "Blank with unavailable HTTP starter");
  expect(reasonId).toBeTruthy();
  const reason = reasonId ? dialog().querySelector<HTMLElement>(`#${reasonId}`)?.textContent : "";
  expect(reason).toContain("HTTP Endpoint is not present in the activity catalog");
  expect(reason).toContain("Write HTTP Response is not present in the activity catalog");
  expect(reason).not.toMatch(/package|permission/i);
  expect(buttonByText(dialog(), "Create")?.getAttribute("aria-disabled")).toBe("false");
});

it.each([
  ["a catalog-provided reason", [{ ...flowchart, available: false, availabilityReason: "Withheld by the activity catalog." }], "Withheld by the activity catalog."],
  ["an unavailable activity without a supplied reason", [{ ...flowchart, available: false }], "Flowchart is listed but unavailable in this activity catalog."],
  ["a missing activity", [], "Flowchart isn't included in this activity catalog."],
  ["an activity without an authoring template", [{ ...flowchart, authoringTemplate: undefined }], "Flowchart has no available authoring template in this catalog."]
] as const)("associates a useful reason with an unavailable root choice caused by %s", async (_condition, catalog, expectedReason) => {
  const fetchMock = catalogFetch(catalog);
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute();

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await waitForText(dialog(), expectedReason);
  await vi.waitFor(() => expect(inputByLabel(dialog(), "Flowchart")?.disabled).toBe(true));

  const rootChoice = inputByLabel(dialog(), "Flowchart");
  const reasonId = rootChoice?.getAttribute("aria-describedby");
  expect(reasonId).toBeTruthy();
  expect(reasonId ? dialog().querySelector<HTMLElement>(`#${reasonId}`)?.textContent : "").toContain(expectedReason);
  expect(buttonByText(dialog(), "Create")?.getAttribute("aria-disabled")).toBe("true");
  expect(buttonByText(dialog(), "Create")?.getAttribute("aria-describedby")).toBe("wf-root-create-readiness");
  dialog().dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(fetchMock.mock.calls.some(([url, init]) => String(url).endsWith("/design/workflows/definitions") && init?.method === "POST")).toBe(false);
});

it("creates with the explicitly selected current root version and opens the created workflow", async () => {
  const currentSequence = sequenceActivity("sequence-v7", "7.2.0");
  let createdBody: CreateBody | undefined;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/design/activities/catalog")) return response({ activities: [flowchart, currentSequence] });
    if (url.endsWith("/design/workflows/definitions") && init?.method === "POST") {
      createdBody = JSON.parse(String(init.body)) as CreateBody;
      return response({ definition: definition({ id: "selected-root-definition", name: createdBody.name }), draft: null, versions: [] });
    }
    if (url.includes("/design/workflows/definitions/selected-root-definition")) {
      return response({ definition: definition({ id: "selected-root-definition" }), draft: null, versions: [] });
    }
    return response({ items: [definition()] });
  });
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute("/workflows/definitions", undefined, true, capabilityDocument());

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await fill(inputByLabel(dialog(), "Display name"), "Selected root version");
  await click(inputByLabel(dialog(), "Sequence"));
  await click(buttonByText(dialog(), "Create"));

  await vi.waitFor(() => expect(createdBody).toBeDefined());
  expect(createdBody?.initialState?.rootActivity?.activityVersionId).toBe("sequence-v7");
  await vi.waitFor(() => expect(new URLSearchParams(window.location.search).get("definition")).toBe("selected-root-definition"));
});

it("does not issue duplicate requests from a rapid repeated submit while creation is pending", async () => {
  let resolveCreate!: (result: Response) => void;
  const pendingCreate = new Promise<Response>(resolve => { resolveCreate = resolve; });
  let createRequests = 0;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/design/activities/catalog")) return response({ activities: [flowchart] });
    if (url.endsWith("/design/workflows/definitions") && init?.method === "POST") {
      createRequests += 1;
      return pendingCreate;
    }
    if (url.includes("/design/workflows/definitions/pending-definition")) {
      return response({ definition: definition({ id: "pending-definition" }), draft: null, versions: [] });
    }
    return response({ items: [definition()] });
  });
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute();

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await fill(inputByLabel(dialog(), "Display name"), "Pending workflow");
  const createButton = buttonByText(dialog(), "Create");
  if (!createButton) throw new Error("Create button not found.");

  createButton.click();
  createButton.click();
  await vi.waitFor(() => expect(createRequests).toBeGreaterThan(0));
  expect(createRequests).toBe(1);
  expect(buttonByText(dialog(), "Cancel")?.disabled).toBe(true);
  expect(dialog().querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);

  resolveCreate(response({ definition: definition({ id: "pending-definition" }), draft: null, versions: [] }));
  await vi.waitFor(() => expect(document.querySelector('form[role="dialog"]')).toBeNull());
});

it("allows a new create attempt after the previous request fails", async () => {
  let createRequests = 0;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/design/activities/catalog")) return response({ activities: [flowchart] });
    if (url.endsWith("/design/workflows/definitions") && init?.method === "POST") {
      createRequests += 1;
      if (createRequests === 1) return response({ detail: "Temporary create failure." }, 503);
      return response({ definition: definition({ id: "retried-definition", name: "Retry create" }), draft: null, versions: [] });
    }
    if (url.includes("/design/workflows/definitions/retried-definition")) {
      return response({ definition: definition({ id: "retried-definition" }), draft: null, versions: [] });
    }
    if (url.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
    return response({ items: [definition()] });
  });
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute("/workflows/definitions", undefined, true, capabilityDocument());

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await fill(inputByLabel(dialog(), "Display name"), "Retry create");
  await click(buttonByText(dialog(), "Create"));
  await waitForText(dialog(), "Temporary create failure.");
  expect(inputByLabel(dialog(), "Display name")?.value).toBe("Retry create");
  await vi.waitFor(() => expect(document.activeElement).toBe(dialog().querySelector('[role="alert"]')));
  expect(createRequests).toBe(1);

  await click(buttonByText(dialog(), "Create"));
  await vi.waitFor(() => expect(createRequests).toBe(2));
  await vi.waitFor(() => expect(new URLSearchParams(window.location.search).get("definition")).toBe("retried-definition"));
  expect(fetchMock.mock.calls.filter(([url, init]) => String(url).endsWith("/design/workflows/definitions") && init?.method === "POST")).toHaveLength(2);
});

it("can recover from an activity catalog failure with the dialog retry action", async () => {
  let catalogRequests = 0;
  let createdBody: CreateBody | undefined;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/design/activities/catalog")) {
      catalogRequests += 1;
      return catalogRequests === 1
        ? response({ detail: "Activity catalog is temporarily unavailable." }, 503)
        : response({ activities: [flowchart] });
    }
    if (url.endsWith("/design/workflows/definitions") && init?.method === "POST") {
      createdBody = JSON.parse(String(init.body)) as CreateBody;
      return response({ definition: definition({ id: "recovered-definition", name: createdBody.name }), draft: null, versions: [] });
    }
    if (url.includes("/design/workflows/definitions/recovered-definition")) {
      return response({ definition: definition({ id: "recovered-definition" }), draft: null, versions: [] });
    }
    if (url.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
    return response({ items: [definition()] });
  });
  vi.stubGlobal("fetch", fetchMock);
  const { container } = await renderRegisteredRoute("/workflows/definitions", undefined, false, capabilityDocument());

  await waitForText(container, "Hello World");
  await click(buttonByText(container, "New workflow"));
  await waitForDialog();
  await vi.waitFor(() => expect(catalogRequests).toBe(1));
  await vi.waitFor(() => expect(dialog().querySelector('[role="alert"]')?.textContent).toContain("Activity catalog is temporarily unavailable."));
  expect(buttonByText(dialog(), "Create")?.getAttribute("aria-describedby")).toBe("wf-activity-catalog-error");

  await click(buttonByText(dialog(), "Retry"));
  await vi.waitFor(() => expect(catalogRequests).toBe(2));
  await vi.waitFor(() => expect(inputByLabel(dialog(), "Flowchart")?.disabled).toBe(false));
  await fill(inputByLabel(dialog(), "Display name"), "Recovered workflow");
  await vi.waitFor(() => expect(buttonByText(dialog(), "Create")?.getAttribute("aria-disabled")).toBe("false"));
  await click(buttonByText(dialog(), "Create"));
  await vi.waitFor(() => expect(createdBody).toBeDefined());

  expect(createdBody?.initialState?.rootActivity?.activityVersionId).toBe("flowchart-v1");
});

function buttonByText(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll("button")).find(button => button.textContent?.trim() === text) ?? null;
}

function catalogFetch(catalog: readonly ActivityCatalogItem[]) {
  return vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async input => {
    return String(input).endsWith("/design/activities/catalog")
      ? response({ activities: catalog })
      : response({ items: [definition()] });
  });
}

function inputByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLInputElement>("input")).find(input => input.getAttribute("aria-label") === label) ?? null;
}

function sequenceActivity(activityVersionId: string, version: string): ActivityCatalogItem {
  const base = flowchartActivity();
  return {
    ...base,
    activityVersionId,
    activityTypeKey: "Elsa.Activities.Sequence",
    version,
    displayName: "Sequence",
    authoringTemplate: {
      ...base.authoringTemplate!,
      activityVersionId,
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [] } }
    }
  };
}

function dialog() {
  const result = document.querySelector<HTMLElement>('form[role="dialog"]');
  if (!result) throw new Error("Create dialog not found.");
  return result;
}

async function waitForDialog() {
  await vi.waitFor(() => expect(document.querySelector('form[role="dialog"]')).not.toBeNull());
}

async function click(element: Element | null) {
  if (!(element instanceof HTMLElement)) throw new Error("Element not found.");
  element.click();
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function fill(element: HTMLInputElement | null, value: string) {
  if (!element) throw new Error("Input not found.");
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(element, value);
  element.dispatchEvent(new Event("input", { bubbles: true }));
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitForText(container: HTMLElement, text: string) {
  await vi.waitFor(() => expect(container.textContent).toContain(text), { timeout: 5_000, interval: 20 });
}
