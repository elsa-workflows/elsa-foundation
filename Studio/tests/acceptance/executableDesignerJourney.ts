import { expect, type Page, type TestInfo } from "@playwright/test";
import { hasExactUrlMultimap } from "./exactUrl.mjs";

type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
type Submitted = { definition: { id: string }; draftId: string; version: { id: string } };
type Published = { artifactId: string; sourceReferenceId: string };
type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real catalog versions, immutable publication and editable source draft in the disposable host. */
export async function executableDesignerJourney(page: Page, info: TestInfo, capture: Capture, ownDefinition: (id: string) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "The owned runner supplies its private Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(type: string) {
    const list = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const item = list.items.find(item => item.definition.activityTypeKey === type);
    expect(item, `Registered ${type}`).toBeTruthy();
    return item!.definition.headVersionId;
  }
  const [sequenceVersion, lineVersion, flowVersion, compensationVersion, bpmnVersion] = await Promise.all([
    version("Elsa.Activities.Sequence.Activities.Sequence"), version("Elsa.Activities.Primitives.Activities.WriteLine"),
    version("Elsa.Activities.Flowchart.Activities.Flowchart"), version("Elsa.Activities.Compensation.Activities.CompensationScope"),
    version("Elsa.Activities.Bpmn.Activities.BpmnProcess")
  ]);
  const line = (id: string, text: string): Node => ({ nodeId: id, activityVersionId: lineVersion, inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: text } }], outputs: [] });
  const structure = (id: string, activityVersionId: string, kind: string, payload: Record<string, unknown>): Node => ({ nodeId: id, activityVersionId, inputs: [], outputs: [], structure: { kind, schemaVersion: "1.0.0", payload } });
  const seq = (id: string, activities: Node[]) => structure(id, sequenceVersion, "elsa.sequence.structure", { activities });
  const stamp = `${info.project.name}-${Date.now()}`;
  const state = (rootActivity: Node) => ({ rootActivity, variables: [], inputs: [], outputs: [] });
  async function publish(root: Node, name: string) {
    const submitted = await api<Submitted>("/design/workflows/definitions/submit", "POST", { name: `${name} ${stamp}`, state: state(root) });
    ownDefinition(submitted.definition.id);
    const published = await api<Published>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    return { submitted, published };
  }
  async function inspect(published: Published) {
    await page.goto(`/workflows/executables/${published.artifactId}?backend=legacy&ref=${published.sourceReferenceId}`);
    await expect(page.getByRole("region", { name: "Executable canvas", exact: true })).toBeVisible();
  }
  async function collapseEditorPanels() {
    const active = page.locator('.studio-workspace-panel:not([hidden]) > .wf-editor');
    for (const [name, state] of [["inspector", "inspector"], ["activities", "palette"]] as const) {
      const panel = active.locator(state === "inspector" ? ".wf-inspector" : ".wf-palette");
      const collapse = panel.getByRole("button", { name: `Collapse ${name} panel`, exact: true });
      const expand = panel.getByRole("button", { name: `Expand ${name} panel`, exact: true });
      await expect(panel).toBeVisible();
      await expect(collapse.or(expand)).toBeVisible();
      if (await collapse.isVisible()) await collapse.click();
      await expect(expand).toBeVisible();
      await expect(collapse).toBeHidden();
      await expect(active.locator(".wf-editor-body")).toHaveClass(new RegExp(`\\b${state}-collapsed\\b`));
    }
  }
  // A real long draft makes internal canvas scrolling observable at every viewport.
  const frameDraft = await api<Submitted>("/design/workflows/definitions/submit", "POST", {
    name: `Navigation frame ${stamp}`, state: state(seq("frame-root", Array.from({ length: 16 }, (_, index) => line(`frame-${index}`, `Frame step ${index}`))))
  });
  ownDefinition(frameDraft.definition.id);
  const frameUrl = `/workflows/definitions?backend=legacy&definition=${frameDraft.definition.id}&version=${frameDraft.version.id}`;
  const expectedFrameUrl = new URL(frameUrl, page.url());
  await page.goto(frameUrl);
  const editor = page.locator('.studio-workspace-panel:not([hidden]) > .wf-editor');
  await expect(editor.locator(".wf-sequence-step")).toHaveCount(16);
  const narrowFrame = (info.project.use.viewport?.width ?? 1440) < 800;
  if (narrowFrame) {
    await collapseEditorPanels();
  }
  const collapseDock = page.getByRole("button", { name: "Collapse bottom panel", exact: true });
  if (await collapseDock.isVisible()) await collapseDock.click();
  const assertFrame = async (name: string) => {
    await expect(page.getByRole("navigation", { name: "Workflows views", exact: true })).toBeVisible();
    const geometry = await editor.evaluate(node => {
      const content = node.closest(".content")!;
      const bounds = content.getBoundingClientRect();
      const view = content.querySelector('.studio-workspace-panel:not([hidden]) .studio-view-navigation')!.getBoundingClientRect();
      return {
        viewport: { width: innerWidth, height: innerHeight },
        content: { top: bounds.top, bottom: bounds.bottom, scrollTop: content.scrollTop }, viewBottom: view.bottom,
        frames: [node, ...node.querySelectorAll(".wf-editor-body, .wf-canvas-shell, .wf-sequence-designer, .wf-inspector")]
          .map(part => { const box = part.getBoundingClientRect(); return { name: part.className, top: box.top, bottom: box.bottom, height: box.height }; })
      };
    });
    expect(geometry.frames[0].top).toBeGreaterThanOrEqual(geometry.viewBottom);
    expect(Math.abs(geometry.frames[0].bottom - geometry.content.bottom), "Editor fills the remaining shell cell").toBeLessThanOrEqual(1);
    for (const part of geometry.frames) {
      expect(part.height, `${part.name}: positive remaining height`).toBeGreaterThan(0);
      expect(part.top, `${part.name}: top inside content`).toBeGreaterThanOrEqual(geometry.content.top - 1);
      expect(part.bottom, `${part.name}: lower edge inside content`).toBeLessThanOrEqual(geometry.content.bottom + 1);
      expect(part.bottom, `${part.name}: lower edge inside viewport`).toBeLessThanOrEqual(geometry.viewport.height);
    }
    const scroller = editor.locator(".wf-sequence-designer");
    expect(await scroller.evaluate(node => node.scrollHeight > node.clientHeight), "The real long draft must require internal scrolling").toBe(true);
    const captionStates = [];
    for (const fraction of [0, 0.5, 1]) {
      await scroller.evaluate((node, value) => { node.scrollTop = (node.scrollHeight - node.clientHeight) * value; }, fraction);
      const observation = await scroller.evaluate(node => {
        const caption = node.querySelector(".wf-sequence-heading")!.getBoundingClientRect();
        const canvas = node.getBoundingClientRect();
        const collisions = [...node.querySelectorAll(".wf-sequence-item")].flatMap(card => {
          const box = card.getBoundingClientRect();
          const visible = box.bottom > canvas.top && box.top < canvas.bottom;
          return visible && caption.bottom > canvas.top && caption.top < canvas.bottom &&
            box.left < caption.right && box.right > caption.left && box.top < caption.bottom && box.bottom > caption.top
            ? [card.getAttribute("data-id") ?? card.className] : [];
        });
        return { scrollTop: node.scrollTop, caption: { top: caption.top, bottom: caption.bottom }, collisions };
      });
      captionStates.push({ fraction, ...observation });
      expect(observation.collisions, "Sequence caption and activity cards have separate visible regions").toEqual([]);
      if (fraction < 1) await capture(page, info, `${name}-scroll-${fraction === 0 ? "top" : "middle"}`);
    }
    await info.attach(`${name}-caption-scroll`, { body: JSON.stringify(captionStates), contentType: "application/json" });
    await scroller.evaluate(node => { node.scrollTop = 0; });
    const last = editor.locator('.wf-sequence-step[data-id="frame-15"]');
    await last.scrollIntoViewIfNeeded();
    expect(await scroller.evaluate(node => node.scrollTop), "The inner canvas actually scrolled").toBeGreaterThan(0);
    await expect(last).toBeInViewport({ ratio: 1 });
    expect(await last.evaluate(node => {
      const rect = node.getBoundingClientRect();
      const canvas = node.closest(".wf-sequence-designer")!.getBoundingClientRect();
      return rect.top >= canvas.top && rect.bottom <= canvas.bottom &&
        node.contains(document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2));
    }), "The final real activity remains inside the scrollable canvas and unobscured").toBe(true);
    expect(await page.locator(".content").evaluate(node => node.scrollTop)).toBe(geometry.content.scrollTop);
    await info.attach(`${name}-geometry`, { body: JSON.stringify({ ...geometry, canvasScrollTop: await scroller.evaluate(node => node.scrollTop) }), contentType: "application/json" });
    await capture(page, info, name);
  };
  await assertFrame("definition-frame");
  await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
  await expect(collapseDock).toBeVisible();
  await assertFrame("definition-frame-dock");
  // Keep the same editor DOM alive through a real Alt workspace tab and return.
  const screens = page.getByRole("tablist", { name: "Open screens", exact: true });
  const editorTabId = await screens.getByRole("tab", { selected: true }).getAttribute("id");
  const screenCount = await screens.getByRole("tab").count();
  const epoch = await page.evaluate(() => performance.timeOrigin);
  await editor.evaluate(node => { (node as HTMLElement).dataset.frameRetention = "same-editor"; });
  await page.locator(".sidebar").getByRole("link", { name: "Home", exact: true }).click({ modifiers: ["Alt"] });
  await expect(screens.getByRole("tab")).toHaveCount(screenCount + 1);
  await expect(editor).toHaveCount(0);
  await page.locator(`[id="${editorTabId}"]`).click();
  await expect(page).toHaveURL(actual => hasExactUrlMultimap(actual, expectedFrameUrl));
  await expect(editor).toHaveAttribute("data-frame-retention", "same-editor");
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(epoch);
  await assertFrame("definition-frame-retained");
  await collapseDock.click();
  const root = seq("root", [line("first", "Pinned hello"), seq("nested", [line("inner", "Nested hello")]), line("last", "Pinned goodbye")]);
  const { submitted, published } = await publish(root, "Sequence parity");
  await page.goto(`/workflows/definitions?backend=legacy&definition=${submitted.definition.id}&version=${submitted.version.id}`);
  await expect(page.locator(".wf-sequence-step")).toHaveCount(3);
  const narrow = (info.project.use.viewport?.width ?? 1440) < 800;
  if (narrow) {
    await expect(editor.locator(".wf-editor-body")).toHaveClass(/inspector-collapsed/);
    await expect(editor.locator(".wf-editor-body")).toHaveClass(/palette-collapsed/);
    await collapseEditorPanels();
  }
  await expect(page.locator('.wf-sequence-step[data-id="first"]')).toBeVisible();
  await expect.poll(async () => (await page.locator('.wf-sequence-step[data-id="first"]').boundingBox())?.width ?? 0).toBeGreaterThan(180);
  await capture(page, info, "definition-sequence");
  if (narrow) {
    await page.getByRole("button", { name: "Expand activities panel", exact: true }).click();
    await page.getByRole("button", { name: "Expand inspector panel", exact: true }).click();
  }
  // Change the current source after publication. Inspection must retain the pinned texts and topology.
  await inspect(published); // Unmount the editor before changing its draft outside the UI.
  await api(`/design/workflows/drafts/${submitted.draftId}`, "PUT", { state: state(seq("root", [line("changed", "CURRENT DRAFT ONLY")])), layout: [] });
  await page.reload();
  const changed = await api<{ state: { rootActivity: Node } }>(`/design/workflows/drafts/${submitted.draftId}`);
  expect(changed.state.rootActivity.structure?.payload.activities).toEqual([expect.objectContaining({ nodeId: "changed" })]);
  expect(JSON.stringify(changed.state.rootActivity)).toContain("CURRENT DRAFT ONLY");
  const canvas = page.getByRole("region", { name: "Executable canvas", exact: true });
  await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
  await expect(canvas).toContainText("Pinned hello");
  await expect(canvas).not.toContainText("CURRENT DRAFT ONLY");
  const first = canvas.locator('[data-id="first"]');
  await first.press("Enter"); await expect(first).toHaveAttribute("aria-pressed", "true");
  await first.press("Delete"); await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
  await expect(canvas.locator(".wf-sequence-insert, .wf-sequence-actions")).toHaveCount(0);
  await expect(first).toHaveAttribute("draggable", "false");
  expect((await first.boundingBox())!.width).toBeGreaterThan(180);
  await capture(page, info, "executable-sequence");
  await canvas.getByRole("button", { name: /^Open Activities/ }).press("Enter");
  await expect(canvas.locator('[data-id="inner"]')).toBeVisible();
  await capture(page, info, "nested-sequence");
  await canvas.getByRole("button", { name: "Root", exact: true }).click();
  await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
  await page.reload(); await expect(canvas).toContainText("Pinned goodbye");
  await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
  await expect(page.getByRole("button", { name: "Collapse executable details panel", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Collapse executable details panel", exact: true }).click();
  await expect(first).toBeVisible();
  await page.getByRole("button", { name: "Expand executable details panel", exact: true }).click();

  const draftRun = await api<{ artifactId: string; status: string }>("/publishing/workflows/drafts/test-runs", "POST", {
    definitionId: submitted.definition.id, snapshotId: `designer-${stamp}`, state: state(seq("draft-root", [line("draft-step", "Draft snapshot hello")])), layout: [], acknowledgeUnavailableExpressionValidation: true
  });
  expect(draftRun.artifactId, JSON.stringify(draftRun)).toBeTruthy();
  await page.goto(`/workflows/executables/${draftRun.artifactId}?backend=legacy`);
  await expect(canvas.locator(".wf-sequence-step")).toHaveCount(1);
  await expect(canvas).toContainText("Draft snapshot hello");
  await capture(page, info, "draft-sequence");

  const empty = await publish(seq("empty", []), "Empty sequence parity");
  await inspect(empty.published); await expect(canvas).toContainText("This sequence has no steps.");
  await expect(canvas.locator(".wf-sequence-insert")).toHaveCount(0);
  await capture(page, info, "empty-sequence");

  const compensation = await publish(structure("scope", compensationVersion, "elsa.compensation-scope.structure", { steps: [{ name: "Reserve", activity: line("reserve", "Reserve inventory"), compensation: line("release", "Release inventory") }] }), "Compensation parity");
  await inspect(compensation.published);
  await expect(canvas.locator(".wf-compensation-designer")).toBeVisible();
  await expect(canvas.getByRole("listitem", { name: "Step 1, Reserve, compensated by Write Line", exact: true })).toBeVisible();
  await expect(canvas.locator('[data-graph-node-id="reserve"]')).toBeVisible();
  const handler = canvas.locator('[data-graph-node-id="release"]');
  await handler.press("Enter"); await expect(handler).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("complementary", { name: "Executable details panel", exact: true })).toContainText("release");
  await expect(canvas).toContainText("Undoes: Reserve");
  await expect(canvas.getByRole("textbox")).toHaveCount(0);
  await capture(page, info, "compensation");

  const flow = await publish(structure("flow", flowVersion, "elsa.flowchart.structure", {
    activities: [line("flow-a", "Flow one"), line("flow-b", "Flow two")], startNodeId: "flow-a",
    connections: [{ id: "edge", source: { nodeId: "flow-a", port: "Done" }, target: { nodeId: "flow-b" } }], nodeMetadata: {}, connectionMetadata: {}
  }), "Flowchart parity");
  await inspect(flow.published);
  await expect(canvas.locator(".react-flow__node")).toHaveCount(2);
  await expect(canvas.locator(".react-flow__edge")).toHaveCount(1);
  await expect(canvas.locator(".wf-sequence-designer")).toHaveCount(0);
  await capture(page, info, "flowchart");

  const bpmn = await publish(structure("process", bpmnVersion, "elsa.bpmn.structure", {
    activities: [line("bound", "BPMN hello")], elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "task", elementType: "task", childNodeId: "bound" }, { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: [{ flowId: "s-t", sourceRef: "start", targetRef: "task" }, { flowId: "t-e", sourceRef: "task", targetRef: "end" }]
  }), "BPMN parity");
  await inspect(bpmn.published);
  await expect(canvas.locator(".wf-bpmn-node")).toHaveCount(3);
  await expect(canvas.locator(".react-flow__edge")).toHaveCount(2);
  const boundTask = canvas.locator('.react-flow__node[data-id="task"]');
  await boundTask.press("Enter"); await expect(boundTask).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("complementary", { name: "Executable details panel", exact: true })).toContainText("Write Line");
  await boundTask.press("Delete"); await expect(canvas.locator(".wf-bpmn-node")).toHaveCount(3);
  await capture(page, info, "bpmn");

}
