import { expect, type Page, type Response, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Writes = { variables: Array<{ name: string; scopeId: string; referenceKey: string }>; mayWriteEveryVisibleVariable: boolean };
type Analysis = { scriptWrites: Writes; visibleVariables: Array<{ name: string; scopeId: string; referenceKey: string }> };
type Node = { nodeId: string; inputs: Array<{ referenceKey: string; value: { expressionType: string; value: unknown } }>; structure?: { payload: { activities: Node[] } } };

/** FR-036 on the normal source composition, real analysis endpoint and saved draft; no intercepted responses. */
export async function scriptVariableWritesJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown, timeout = 15_000): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = result.items.find(item => item.definition.activityTypeKey === key);
    expect(entry, `Composed activity ${key}`).toBeTruthy();
    return entry!.definition.headVersionId;
  }
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const scriptVersion = await version("Elsa.Activities.Scripting.Activities.RunJavaScript");
  const variable = (referenceKey: string, name: string) => ({ referenceKey, name, type: { alias: "Object", collectionKind: "Single" }, storageDriverType: null, default: null });
  const literal = "setVariable('total', 1); variables.order = { total: 2 };";
  const computed = "const key = 'total'; variables[key] = 3;";
  const readOnly = "return variables.total + 1;";
  async function seed(shadowed = false) {
    const submitted = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
      name: `Script writes ${shadowed ? "scopes" : "edits"} ${info.project.name} ${Date.now()}`,
      state: {
        inputs: [], outputs: [], variables: [variable("total-key", "total"), variable("order-key", "order")],
        rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
          structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: {
            variables: shadowed ? [variable("local-total-key", "total")] : [],
            activities: [{ nodeId: "script", activityVersionId: scriptVersion, outputs: [],
              inputs: [{ referenceKey: "script", value: { expressionType: "Literal", value: shadowed ? "variables.total = 2;" : literal } }] }]
          } }
        }
      }
    });
    registerCleanup(async () => { await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); });
    return submitted;
  }
  const evidence: Array<{ state: string; analysis: Analysis }> = [];
  const scriptRow = () => page.getByRole("group", { name: "Script property", exact: true });
  const summary = () => page.locator(".wf-variable-writes");
  function nextAnalysis(source?: string) {
    return page.waitForResponse(response => {
      if (!new URL(response.url()).pathname.endsWith("/scoped-variables/analyze") || response.request().method() !== "POST") return false;
      const request = response.request().postDataJSON() as { nodeId?: string; state?: { rootActivity: Node } };
      if (request.nodeId !== "script") return false;
      return source === undefined || request.state?.rootActivity.structure?.payload.activities[0]?.inputs.find(input => input.referenceKey === "script")?.value.value === source;
    });
  }
  async function analyzed(response: Promise<Response>, state: string) {
    const result = await response;
    expect(result.ok(), "Real scoped-variable analysis").toBe(true);
    const analysis = await result.json() as Analysis;
    expect(analysis.scriptWrites, "Server analyzed this script body").toBeTruthy();
    evidence.push({ state, analysis });
    return analysis;
  }
  async function open(id: string, state: string) {
    const response = nextAnalysis();
    const designerUrl = new URL(page.url());
    designerUrl.searchParams.set("definition", id);
    await page.goto(designerUrl.toString());
    const node = page.locator('.wf-sequence-step[data-id="script"]');
    await expect(node).toBeAttached();
    // The same keyboard selection works when the inspector covers most of the narrow canvas.
    await node.press("Enter");
    const expandInspector = page.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expandInspector.isVisible()) await expandInspector.click();
    await expect(scriptRow()).toBeVisible();
    return analyzed(response, state);
  }
  async function storedScript(draftId: string) {
    const draft = await api<{ state: { rootActivity: Node } }>(`/design/workflows/drafts/${draftId}`);
    return draft.state.rootActivity.structure?.payload.activities[0]?.inputs.find(input => input.referenceKey === "script")?.value;
  }
  async function edit(source: string, state: string, draftId: string) {
    const response = nextAnalysis(source);
    const editor = scriptRow().getByRole("textbox").first();
    await editor.fill(source);
    await editor.press("Tab");
    const analysis = await analyzed(response, state);
    await expect.poll(() => storedScript(draftId), { timeout: 30_000 }).toEqual({ expressionType: "Literal", value: source });
    return analysis;
  }

  const authored = await seed();
  const listed = await open(authored.definition.id, "literal");
  expect(listed.scriptWrites.mayWriteEveryVisibleVariable).toBe(false);
  expect(listed.scriptWrites.variables).toEqual(expect.arrayContaining([
    { name: "total", scopeId: "workflow", referenceKey: "total-key" },
    { name: "order", scopeId: "workflow", referenceKey: "order-key" }
  ]));
  await expect(summary()).toHaveText("May write: total, order");
  await capture(page, info, "literal-writes");

  expect((await edit(computed, "computed", authored.draftId)).scriptWrites.mayWriteEveryVisibleVariable).toBe(true);
  await expect(summary()).toHaveText("May write any visible variable (its script writes by a computed name)");
  await capture(page, info, "computed-writes");
  expect((await open(authored.definition.id, "reopened-computed")).scriptWrites.mayWriteEveryVisibleVariable).toBe(true);
  await expect(summary()).toHaveText("May write any visible variable (its script writes by a computed name)");
  await capture(page, info, "reopened-computed");

  const none = await edit(readOnly, "none", authored.draftId);
  expect(none.scriptWrites).toEqual({ variables: [], mayWriteEveryVisibleVariable: false });
  await expect(summary()).toHaveCount(0);
  const reopenedNone = await open(authored.definition.id, "reopened-none");
  expect(reopenedNone.scriptWrites).toEqual({ variables: [], mayWriteEveryVisibleVariable: false });
  await expect(summary()).toHaveCount(0);
  await capture(page, info, "no-writes");

  const scoped = await seed(true);
  const nearest = await open(scoped.definition.id, "nearest-scope");
  expect(nearest.visibleVariables.filter(item => item.name === "total")).toHaveLength(2);
  expect(nearest.scriptWrites.variables).toEqual([{ name: "total", scopeId: "root", referenceKey: "local-total-key" }]);
  await expect(summary()).toHaveText("May write: total");
  await capture(page, info, "nearest-scope");
  await info.attach("scoped-analysis.json", { body: JSON.stringify(evidence, null, 2), contentType: "application/json" });
}
