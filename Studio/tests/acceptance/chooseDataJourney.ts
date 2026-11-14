import { activityDataSupport, attachActivityDataJson } from "./activityDataJourneySupport";
import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
type Binding = { expressionType: string; value: unknown } | null;
// Teardown runs in the spec's afterEach with its own budget; each request is bounded so a stuck call cannot hold it up.
const cleanupRequestTimeout = 15_000;

/**
 * Choose data for whole activity outputs (spec 1215, #3094) on the real composed host: an untouched input stores the
 * server's ActivityResult binding, keeps it through reload, publishes and runs with the producer's value; a loop
 * producer is shown with its reason and stores nothing; keyboard choice, Replace and Clear keep focus in place.
 * Every wait is on observable UI or server state. Seeded definitions are unique per run and deleted afterwards.
 */
export async function chooseDataJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const enhanced = process.env.UX_ACTIVITY_DATA_COMPOSITION !== "default-off";
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown, timeout?: number): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = result.items.find(item => item.definition.activityTypeKey === key);
    expect(entry, key).toBeTruthy(); return entry!.definition.headVersionId;
  }
  const [sequenceVersion, flowchartVersion, timerVersion, lineVersion] = await Promise.all([
    version("Elsa.Activities.Sequence.Activities.Sequence"), version("Elsa.Activities.Flowchart.Activities.Flowchart"),
    version("Elsa.Activities.Scheduling.Activities.Timer"), version("Elsa.Activities.Primitives.Activities.WriteLine")
  ]);
  const timer = (nodeId: string): Node => ({ nodeId, activityVersionId: timerVersion, outputs: [],
    inputs: [{ referenceKey: "Interval", value: { expressionType: "Literal", value: "00:00:01" } }] });
  const line: Node = { nodeId: "consumer", activityVersionId: lineVersion, inputs: [], outputs: [] };
  const sequence = (activities: Node[]): Node => ({ nodeId: "root", activityVersionId: sequenceVersion, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } } });
  const flowchart = (edges: Array<[string, string]>): Node => ({ nodeId: "root", activityVersionId: flowchartVersion, inputs: [], outputs: [],
    structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: {
      activities: [timer("producer"), line], startNodeId: "producer", nodeMetadata: {}, connectionMetadata: {},
      connections: edges.map(([from, to], index) => ({ id: `edge-${index}`, source: { nodeId: from, port: "Done" }, target: { nodeId: to } }))
    } } });

  const definitions: string[] = [];
  // Registered before the first interaction: the spec's afterEach runs it even when this journey times out.
  registerCleanup(async () => {
    for (const id of definitions) await api(`/design/workflows/definitions/${id}`, "DELETE", undefined, cleanupRequestTimeout);
  });
  const stamp = `${info.project.name} ${Date.now()}`;
  async function seed(name: string, root: Node) {
    const submitted = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
      name: `Choose data ${name} ${stamp}`, state: { rootActivity: root, variables: [], inputs: [], outputs: [] }
    });
    definitions.push(submitted.definition.id);
    return submitted;
  }
  async function storedText(draftId: string): Promise<Binding> {
    const draft = await api<{ state: { rootActivity: Node } }>(`/design/workflows/drafts/${draftId}`);
    const children = (draft.state.rootActivity.structure?.payload.activities ?? []) as Node[];
    const consumer = children.find(child => child.nodeId === "consumer");
    const text = (consumer?.inputs as Array<{ referenceKey: string; value: Binding }> | undefined)?.find(input => input.referenceKey === "text");
    return text?.value ?? null;
  }
  const outputBinding = (producerNodeId: string, projectionKey: string): Binding =>
    ({ value: { producerNodeId, projectionKey, isOptional: false }, expressionType: "ActivityResult" });

  async function openConsumer(definitionId: string) {
    await page.goto(`/workflows/definitions?definition=${definitionId}&backend=legacy`);
    const consumer = page.locator('.wf-sequence-step[data-id="consumer"], .react-flow__node[data-id="consumer"]').first();
    await expect(consumer).toBeAttached();
    // Phone authoring starts with the canvas; explicitly open the selected activity's details.
    await consumer.press("Enter");
    if (page.viewportSize()!.width <= 600) await page.getByRole("button", { name: "Expand inspector panel", exact: true }).click();
    await expect(page.locator(".wf-inspector-context h3")).toHaveText("Write Line");
    // A freshly loaded draft may autosave its normalized form; let that settle so the chooser answers the saved draft.
    await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  }
  // The chooser keeps its list while it asks about a newer revision; capture only the settled answer.
  async function settledCapture(name: string) {
    if (enhanced) await expect(chooser().locator(".wf-data-members")).not.toHaveAttribute("aria-busy", "true");
    else await expect(chooser().getByRole("listbox")).not.toHaveAttribute("aria-busy", "true");
    await capture(page, info, name);
  }
  const trigger = () => page.getByRole("button", { name: "Choose data for Text", exact: true });
  const chooser = () => page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const option = (producer: string, output: string): Locator => {
    if (!enhanced) return chooser().getByRole("group", { name: producer, exact: true }).getByRole("option", { name: new RegExp(`^${output}`) });
    const [label, nodeId] = producer.split(" · ");
    const matches = chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: `${label} › ${output}` }) });
    return nodeId ? matches.filter({ hasText: nodeId }) : matches;
  };
  const reference = () => page.locator(".wf-data-reference");

  // Sequence: choose the Timer's Interval output on the untouched Text input.
  const ordered = await seed("sequence", sequence([timer("producer"), line]));
  await openConsumer(ordered.definition.id);
  await expect(reference()).toHaveCount(0);
  // Discoverability: the Choose data action at rest beside the type, and the same choice in the syntax picker.
  const syntaxPicker = page.getByRole("button", { name: /^Text expression syntax/ });
  await syntaxPicker.click();
  const pickerEntry = page.getByRole("option", { name: "Activity output…", exact: true });
  await expect(pickerEntry).toBeVisible();
  await expect(trigger()).toBeVisible();
  await capture(page, info, "choose-data-entry");
  await pickerEntry.click();
  await expect(option("Timer", "Interval")).toBeVisible();
  await expect(option("Timer", "Whole output")).toBeVisible();
  await settledCapture("chooser-open");
  await option("Timer", "Interval").click();
  await expect(chooser()).toHaveCount(0);
  await expect(reference().locator(".wf-data-reference-source")).toContainText("Timer");
  await expect(reference().locator(".wf-data-reference-source")).toContainText("Interval");
  await expect(page.getByRole("button", { name: "Replace data for Text", exact: true })).toBeFocused();
  // The autosaved draft holds the server's binding exactly, as an object.
  await expect.poll(() => storedText(ordered.draftId), { timeout: 30_000 }).toEqual(outputBinding("producer", "Result"));
  await capture(page, info, "output-reference");
  // Reopen: a fresh load reads the saved draft back from the server.
  await openConsumer(ordered.definition.id);
  await expect(reference().locator(".wf-data-reference-source")).toContainText("Interval");
  await expect(page.getByRole("button", { name: "Text expression syntax: Activity output", exact: true })).toBeVisible();
  await publishAndRun(ordered.definition.id);

  // Top-level Flowchart: the same choice, saved, reopened, published and run.
  const flow = await seed("flowchart", flowchart([["producer", "consumer"]]));
  await openConsumer(flow.definition.id);
  await trigger().click();
  await option("Timer", "Interval").click();
  await expect.poll(() => storedText(flow.draftId), { timeout: 30_000 }).toEqual(outputBinding("producer", "Result"));
  await openConsumer(flow.definition.id);
  await expect(reference().locator(".wf-data-reference-source")).toContainText("Interval");
  await publishAndRun(flow.definition.id);

  // A producer on a Flowchart loop is listed with one shared reason and cannot be chosen.
  const loop = await seed("loop", flowchart([["producer", "consumer"], ["consumer", "producer"]]));
  await openConsumer(loop.definition.id);
  await trigger().click();
  const loopGroup = chooser().getByRole("group", { name: enhanced ? "Activity outputs" : "Timer", exact: true });
  const blocked = loopGroup.getByRole("option");
  await expect(blocked).toHaveCount(2);
  // Each unavailable output is described by a reason; the whole output's differs from the declared output's.
  for (const row of await blocked.all()) {
    await expect(row).toHaveAttribute("aria-disabled", "true");
    if (enhanced) await expect(row.locator("small")).not.toBeEmpty();
    else {
      const reasonId = await row.getAttribute("aria-describedby");
      expect(reasonId, "A reason for the unavailable output").toBeTruthy();
      await expect(page.locator(`[id="${reasonId}"]`)).not.toBeEmpty();
    }
  }
  await settledCapture("not-selectable");
  // Choosing an unavailable output does nothing: focus and selection stay put and the input stays untouched.
  const firstBlocked = blocked.first();
  await firstBlocked.focus();
  await page.keyboard.press("Enter");
  await expect(firstBlocked).toBeFocused();
  await expect(firstBlocked).toHaveAttribute("aria-selected", "false");
  await expect(chooser()).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(chooser()).toHaveCount(0);
  await expect(trigger()).toBeFocused();
  await expect(reference()).toHaveCount(0);
  await expect(page.getByRole("button", { name: /^Text expression syntax: (?!Activity output)/ })).toBeVisible();
  await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  expect(await storedText(loop.draftId)).toBeNull();

  // Keyboard-only choice, then Replace and Clear, with focus kept on the row's controls.
  const twice = await seed("keyboard", sequence([timer("producer"), timer("second"), line]));
  await openConsumer(twice.definition.id);
  await trigger().focus();
  await page.keyboard.press("Enter");
  // Nearest producer first; same-named producers are told apart by their node id.
  const second = enhanced ? chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: /^Timer ›/ }) }).filter({ hasText: "second" })
    : chooser().getByRole("group", { name: "Timer · second", exact: true }).getByRole("option");
  await expect(second.first()).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await expect(second.nth(1)).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(chooser()).toHaveCount(0);
  const replace = page.getByRole("button", { name: "Replace data for Text", exact: true });
  await expect(replace).toBeFocused();
  await expect.poll(() => storedText(twice.draftId), { timeout: 30_000 }).toEqual(outputBinding("second", "Result"));
  // The reference names the same activity the chooser did, including which of the two same-named ones it is.
  await expect(reference().locator(".wf-data-reference-source")).toContainText("Timer · second");
  await replace.press("Enter");
  // Legacy list marks the selected whole source; enhanced list keeps navigation separate from selection.
  await expect(second.nth(1)).toHaveAttribute("aria-selected", enhanced ? "false" : "true");
  expect(await storedText(twice.draftId)).toEqual(outputBinding("second", "Result"));
  await settledCapture("replace-chooser");
  await option("Timer · producer", "Whole output").click();
  if (enhanced) await chooser().getByRole("button", { name: "Use whole value", exact: true }).click();
  await expect(replace).toBeFocused();
  await expect.poll(() => storedText(twice.draftId), { timeout: 30_000 }).toEqual(outputBinding("producer", "$result"));
  await expect(reference().locator(".wf-data-reference-source")).toContainText("Timer · producer");
  // The reference and chooser at 320px, the runner's focused narrow width (as the decisions journey records it).
  const viewport = page.viewportSize()!;
  await page.setViewportSize({ width: 320, height: 1000 });
  await openConsumer(twice.definition.id);
  await replace.scrollIntoViewIfNeeded();
  await replace.click();
  await expect(chooser().getByRole("option").first()).toBeVisible();
  await settledCapture("chooser-320");
  await page.keyboard.press("Escape");
  await expect(chooser()).toHaveCount(0);
  await page.setViewportSize(viewport);
  await openConsumer(twice.definition.id);
  await page.getByRole("button", { name: "Clear data for Text", exact: true }).press("Enter");
  await expect(reference()).toHaveCount(0);
  await expect(trigger()).toBeFocused();
  await expect.poll(() => storedText(twice.draftId), { timeout: 30_000 }).toBeNull();

  await activityDataCapabilityControl(page, info, capture, registerCleanup);

  async function publishAndRun(definitionId: string) {
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    const publish = review.getByRole("button", { name: "Publish", exact: true });
    await expect(publish).toBeEnabled({ timeout: 60_000 });
    await publish.click();
    const published = page.getByRole("dialog", { name: "Published", exact: true });
    await expect(published).toBeVisible({ timeout: 60_000 });
    await published.getByRole("button", { name: "Close", exact: true }).click();
    const slots = await api<{ items: { slotName: string; activeActivationId: string | null }[] }>(`/runtime/workflows/activation-slots/${definitionId}`);
    const activation = slots.items.find(slot => slot.slotName === "default")?.activeActivationId;
    expect(activation, "Active publication").toBeTruthy();
    const publication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/publications/${activation}`);
    // Identical documents share an artifact, so the run names the publication's own source reference.
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${publication.artifactId}/execute`, "POST",
      { inputs: {}, sourceReferenceId: publication.sourceReferenceId });
    const instance = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(instance)).instance.status, { timeout: 60_000 }).toBe("Completed");
    const executions = await api<{ items: { execution: { activityExecutionId: string; authoredActivityId: string } }[] }>(`${instance}/activity-executions`);
    const consumer = executions.items.find(item => item.execution.authoredActivityId === "consumer")!.execution.activityExecutionId;
    const detail = await api<{ valueSnapshots: { evidenceId: string; inputKey: string | null }[] }>(`${instance}/activity-executions/${consumer}`);
    const evidence = detail.valueSnapshots.find(snapshot => snapshot.inputKey === "text");
    expect(evidence, "Committed Text input evidence").toBeTruthy();
    const payload = await api<{ payload: { preview: string } }>(`${instance}/activity-executions/${consumer}/value-evidence/${evidence!.evidenceId}/payload`);
    // The consumer received the producer's committed output, not a literal or a variable.
    expect(payload.payload.preview).toBe("00:00:01");
    await attachActivityDataJson(info, `whole-output-runtime-${definitionId}.json`, { definitionId, workflowExecutionId: run.workflowExecutionId, expected: "00:00:01", committed: payload.payload.preview });
  }
}

/** Also invoked alone by the owned enabled-host negative: the same typed control must refuse a false off claim. */
export async function activityDataCapabilityControl(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const support = await activityDataSupport(page, info, registerCleanup);
  const enhanced = process.env.UX_ACTIVITY_DATA_COMPOSITION !== "default-off";
  // The off control uses a genuinely member-capable typed object and an existing canonical member reference.
  // Flipping only the host capability to true must fail this branch: a scalar Timer alone would be vacuous.
  const customerVersion = await support.version("Elsa.Workbench.ActivityDataSelectionProof.GetCustomer");
  const memberBinding = { expressionType: "ActivityResultMember", value: { producerNodeId: "producer", projectionKey: "Customer", isOptional: false, memberPath: ["customerId"] } };
  const capable = await support.seed("capability control", support.root([
    { nodeId: "producer", activityVersionId: customerVersion, inputs: [], outputs: [] },
    support.line()
  ]));
  await support.open(capable);
  await support.trigger().click();
  await expect(support.chooser().getByRole("option").first()).toBeFocused();
  if (enhanced) await expect(support.chooser().locator(".wf-data-members")).toBeVisible();
  else {
    await expect(support.chooser().locator(".wf-data-members")).toHaveCount(0);
    await expect(support.chooser().getByRole("option", { name: /^Customer/ })).toBeVisible();
    await expect(support.chooser().getByRole("option", { name: /^customerId/ })).toHaveCount(0);
    await expect(support.chooser().getByRole("textbox", { name: "Member name", exact: true })).toHaveCount(0);
  }
  await capture(page, info, "capability-control");
  await page.keyboard.press("Escape"); await expect(support.chooser()).toHaveCount(0); await expect(support.trigger()).toBeFocused();
  expect(await support.stored(capable)).toBeNull();
  await support.assertComposition();
  const retained = await support.saved(capable);
  retained.state.rootActivity.structure!.payload.activities[1].inputs = [{ referenceKey: "text", value: memberBinding }];
  await support.api(`/design/workflows/drafts/${capable.draftId}`, "PUT", retained);
  await support.open(capable); expect(await support.stored(capable)).toEqual(memberBinding);
  await expect(support.reference()).toContainText("customerId");
  await expect(support.reference()).toContainText("Get Customer");
  await capture(page, info, "capability-reference");
  await support.replace().click(); await expect(support.chooser()).toBeVisible();
  await expect(support.chooser().getByRole("option").first()).toBeFocused();
  await page.keyboard.press("Escape"); await expect(support.chooser()).toHaveCount(0); await expect(support.replace()).toBeFocused();
  expect(await support.stored(capable)).toEqual(memberBinding);
  support.observations.push(enhanced ? "typed object member capability enabled" : "typed object with capability false: legacy whole browser, no members, existing member binding preserved on Replace/Escape");

}
