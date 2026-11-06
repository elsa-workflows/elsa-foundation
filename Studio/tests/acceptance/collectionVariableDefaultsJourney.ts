import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Variable = {
  referenceKey: string; name: string; type: { alias: string; collectionKind: string };
  storageDriverType: null; default: { expressionType: string; value: string } | null;
};
type State = { rootActivity: Record<string, unknown>; variables: Variable[]; inputs: unknown[]; outputs: unknown[] };

export async function collectionVariableDefaultsJourney(page: Page, info: TestInfo, capture: Capture) {
  const base = process.env.ELSA_WORKBENCH_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Workbench is required");
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const sequenceType = "Elsa.Activities.Sequence.Activities.Sequence";
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(sequenceType)}`);
  const sequence = catalog.items.find(item => item.definition.activityTypeKey === sequenceType)!.definition.headVersionId;
  const variable = (key: string, name: string, alias: string, kind: string, value: string | null): Variable => ({
    referenceKey: key, name, type: { alias, collectionKind: kind }, storageDriverType: null,
    default: value === null ? null : { expressionType: "Literal", value }
  });
  const readOutput = (key: string, alias: string, kind: string) => ({
    nodeId: `read-${key}`, activityVersionId: "elsa.intrinsic.set-output@1", outputs: [],
    intrinsic: { kind: "setOutput", valueType: { alias, collectionKind: kind } },
    inputs: [
      { referenceKey: "name", value: { expressionType: "Literal", value: key } },
      { referenceKey: "value", value: { expressionType: "Variable", value: { referenceKey: key, declaringScopeId: "workflow" } } }
    ]
  });
  const exactJson = '{"Id":9007199254740993,"amount":1234567890.123456789012345}';
  const state: State = {
    inputs: [], outputs: [],
    variables: [
      variable("strings", "Items", "String", "List", null),
      variable("numbers", "Counts", "Int32", "Array", "[1,2]"),
      variable("flags", "Flags", "Boolean", "List", "[true,false]"),
      variable("json", "JSON", "System.Text.Json.JsonElement", "Array", `[${exactJson}]`),
      variable("legacy", "Legacy", "String", "Array", "[]")
    ],
    rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [], structure: {
      kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [readOutput("strings", "String", "List"), readOutput("numbers", "Int32", "Array")] }
    } }
  };
  const submitted = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
    name: `Collection defaults ${info.project.name} ${Date.now()}`, state
  });
  const row = (name: string) => page.locator(".wf-properties-table tbody tr").filter({ has: page.locator(`input[aria-label='Variable name'][value='${name}']`) });
  async function showVariables() {
    for (const name of ["Collapse activities panel", "Collapse inspector panel"]) {
      const collapse = page.getByRole("button", { name, exact: true });
      if (info.project.name.startsWith("narrow") && await collapse.isVisible()) await collapse.click();
    }
    await page.getByRole("tablist", { name: "Editor view tabs", exact: true }).getByRole("tab", { name: "Variables", exact: true }).click();
    await expect(row("Items")).toBeVisible();
  }
  try {
    // A draft can retain an unfinished legacy value; publication must not silently split it.
    const draft = await api<{ state: State }>(`/design/workflows/drafts/${submitted.draftId}`);
    draft.state.variables.find(item => item.referenceKey === "legacy")!.default = { expressionType: "Literal", value: "a, b" };
    await api(`/design/workflows/drafts/${submitted.draftId}`, "PUT", { state: draft.state, layout: [] });
    await page.goto(`/workflows/definitions?definition=${submitted.definition.id}&backend=legacy`);
    await showVariables();
    const actions = page.getByRole("button", { name: "More workflow actions", exact: true });
    await actions.click();
    const autosave = page.getByRole("menuitemcheckbox", { name: /Autosave/ });
    if (await autosave.getAttribute("aria-checked") === "true") await autosave.click();
    await page.keyboard.press("Escape");

    await expect(row("Items").getByText("No items yet.", { exact: true })).toBeVisible();
    await capture(page, info, "collection-empty");
    const legacy = row("Legacy");
    await legacy.scrollIntoViewIfNeeded();
    await expect(legacy.getByRole("textbox", { name: "Variable default value", exact: true })).toHaveValue("a, b");
    await expect(legacy.getByRole("alert")).toContainText("JSON array");
    await capture(page, info, "collection-invalid");
    await legacy.getByRole("textbox", { name: "Variable default value", exact: true }).fill('["kept,as one item"]');

    const strings = row("Items");
    const add = strings.getByRole("button", { name: "Add Variable default value item", exact: true });
    await add.press("Enter");
    await expect(strings.getByRole("textbox", { name: "Variable default value item 1", exact: true })).toBeFocused();
    await strings.getByRole("textbox", { name: "Variable default value item 1", exact: true }).fill("first,second");
    await add.click();
    await strings.getByRole("textbox", { name: "Variable default value item 2", exact: true }).fill('quoted "value"');
    await add.click();
    await strings.getByRole("button", { name: "Remove Variable default value item 3", exact: true }).press("Enter");
    await expect(strings.getByRole("textbox", { name: "Variable default value item 2", exact: true })).toBeFocused();
    await row("Counts").getByRole("spinbutton", { name: "Variable default value item 2", exact: true }).fill("3");
    await row("Flags").getByRole("checkbox", { name: "Variable default value item 2", exact: true }).check();
    await strings.scrollIntoViewIfNeeded();
    await capture(page, info, "collection-populated");
    const json = row("JSON");
    await json.scrollIntoViewIfNeeded();
    await expect(json.getByRole("textbox", { name: "Variable default value item 1", exact: true })).toHaveValue(exactJson);
    await capture(page, info, "collection-json");

    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(async () => (await api<{ state: State }>(`/design/workflows/drafts/${submitted.draftId}`)).state.variables.find(item => item.referenceKey === "strings")!.default?.value).toBe(JSON.stringify(["first,second", 'quoted "value"']));
    const saved = await api<{ state: State }>(`/design/workflows/drafts/${submitted.draftId}`);
    expect(saved.state.variables.find(item => item.referenceKey === "numbers")!.default).toEqual({ expressionType: "Literal", value: "[1,3]" });
    expect(saved.state.variables.find(item => item.referenceKey === "json")!.default?.value).toBe(`[${exactJson}]`);
    await page.reload();
    await showVariables();
    await expect(row("Items").getByRole("textbox", { name: "Variable default value item 1", exact: true })).toHaveValue("first,second");
    await expect(row("Legacy").getByRole("textbox", { name: "Variable default value item 1", exact: true })).toHaveValue("kept,as one item");
    await row("Items").scrollIntoViewIfNeeded();
    await capture(page, info, "collection-reopened");

    const version = await api<{ id: string }>(`/design/workflows/drafts/${submitted.draftId}/promote`, "POST", {});
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${version.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    type Instance = { instance: { status: string }; incidents: unknown[]; outputs: Record<string, { value: { value: unknown } }> };
    await expect.poll(async () => (await api<Instance>(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const completed = await api<Instance>(`/runtime/workflows/instances/${run.workflowExecutionId}`);
    expect(completed.incidents).toHaveLength(0);
    expect(completed.outputs.strings.value.value).toEqual(["first,second", 'quoted "value"']);
    expect(completed.outputs.numbers.value.value).toEqual([1, 3]);
    await page.goto(`/workflows/definitions?definition=${submitted.definition.id}&backend=legacy&version=${version.id}`);
    await showVariables();
    await expect(row("Items").getByRole("textbox", { name: "Variable default value item 1", exact: true })).toBeDisabled();
    await capture(page, info, "collection-read-only");
  } finally {
    await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE");
  }
}
