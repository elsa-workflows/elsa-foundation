import { execFileSync } from "node:child_process";
import { resolve } from "node:path";
import { expect, type Page, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, wireIdentity, type Binding, type Capture, type Created, type Node, resolvedFeatureClosure, optionalBackendFeatures, type FeatureFact } from "./activityDataJourneySupport";

type Execution = { authoredActivityId: string; activityExecutionId: string; status: string };
type CopyReceipt = { definitionId: string; operation: string; before: unknown; after: unknown; producerId?: string; consumerId: string };

/** Copy operations use the product's native actions. Setup is disposable; API reads are independent identity/runtime oracles. */
export async function activityDataCopyJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void, mode: "pairs" | "scopes" | "external") {
  info.setTimeout(300_000);
  const s = await activityDataSupport(page, info, registerCleanup);
  const receipts: CopyReceipt[] = [], runtime: unknown[] = [], boundaries: unknown[] = [];
  const servedAssets: { url: string; sha256: string }[] = [], assetReads: Promise<void>[] = [];
  const builtAssets = JSON.parse(process.env.UX_WORKFLOW_ASSET_SHA256S ?? "{}") as Record<string, string>;
  page.on("response", response => {
    if (response.request().resourceType() === "script" && response.ok()) assetReads.push((async () => {
      const hash = await crypto.subtle.digest("SHA-256", new Uint8Array(await response.body()));
      servedAssets.push({ url: response.url(), sha256: [...new Uint8Array(hash)].map(value => value.toString(16).padStart(2, "0")).join("") });
    })());
  });
  registerCleanup(async () => {
    await Promise.all(assetReads);
    const expected = Object.entries(builtAssets).filter(([path]) => path.endsWith(".js")).map(([, hash]) => hash);
    const matched = servedAssets.filter(asset => expected.includes(asset.sha256));
    await attachActivityDataJson(info, "activity-data-copy-proof.json", {
      sourceSha: process.env.UX_SOURCE_SHA, studioSha: process.env.UX_STUDIO_SOURCE_SHA,
      composition: process.env.UX_ACTIVITY_DATA_COMPOSITION ?? "normal", mode, receipts, runtime, boundaries,
      builtAssets, servedAssets, matchingSourceAssets: matched
    });
    expect(expected.length, "Freshly built Workflows module asset inventory").toBeGreaterThan(0);
    expect(matched.length, "Browser fetched exact bytes from the freshly built source Workflows module").toBeGreaterThan(0);
  });
  const timerVersion = await s.version("Elsa.Activities.Scheduling.Activities.Timer");
  const customerVersion = await s.version("Elsa.Workbench.ActivityDataSelectionProof.GetCustomer");
  const timer = (): Node => ({ nodeId: "producer", activityVersionId: timerVersion, inputs: [{ referenceKey: "Interval", value: { expressionType: "Literal", value: "00:00:01" } }], outputs: [] });
  const member: Binding = { expressionType: "ActivityResultMember", value: { producerNodeId: "producer", projectionKey: "Customer", isOptional: false, memberPath: ["customerId"] } };
  const whole: Binding = { expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "Result", isOptional: false } };
  const nodes = (node: Node): Node[] => [node, ...(node.structure?.payload.activities ?? []).flatMap(nodes),
    ...((node.structure?.payload.cases ?? []) as { activity: Node | null }[]).flatMap(item => item.activity ? nodes(item.activity) : [])];
  const savedNodes = async (created: Created) => nodes((await s.saved(created)).state.rootActivity);
  const input = (node: Node) => node.inputs.find(item => item.referenceKey === "text")?.value;
  const node = (id: string) => page.locator(`.wf-sequence-step[data-id="${id}"],.wf-canvas .react-flow__node[data-id="${id}"]`).first();
  async function waitCount(created: Created, expected: number) {
    await expect.poll(async () => (await savedNodes(created)).length, { timeout: 30_000 }).toBe(expected);
  }
  async function codeReopen(created: Created, consumerId: string, visibleNodeId = "producer") {
    const before = (await s.saved(created)).state;
    await s.canvas(); await page.getByRole("tab", { name: "Code", exact: true }).click();
    const code = page.getByRole("textbox", { name: "Workflow JSON", exact: true });
    await expect(code).toBeVisible(); await code.press("ControlOrMeta+End"); await code.press("Enter");
    await page.locator(".wf-code-view").getByRole("button", { name: "Apply", exact: true }).click();
    await page.getByRole("tab", { name: "Designer", exact: true }).click();
    await expect.poll(async () => wireIdentity((await s.saved(created)).state)).toBe(wireIdentity(before));
    await page.reload(); await expect(node(visibleNodeId)).toBeAttached();
    await s.select(consumerId);
    expect((await s.saved(created)).state).toEqual(before);
  }
  async function publishRun(created: Created, consumerId: string, producerId: string | undefined, expected: string, originalNeverRuns = false) {
    const before = (await s.saved(created)).state;
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
    await review.getByRole("button", { name: "Publish", exact: true }).click();
    const published = page.getByRole("dialog", { name: "Published", exact: true });
    await expect(published).toBeVisible({ timeout: 60_000 }); await published.getByRole("button", { name: "Close", exact: true }).click();
    const slots = await s.api<{ items: { slotName: string; activeActivationId: string }[] }>(`/runtime/workflows/activation-slots/${created.definition.id}`);
    const activation = slots.items.find(slot => slot.slotName === "default")!.activeActivationId;
    const publication = await s.api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/publications/${activation}`);
    const run = await s.api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${publication.artifactId}/execute`, "POST", { inputs: {}, sourceReferenceId: publication.sourceReferenceId });
    const instance = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    await expect.poll(async () => (await s.api<{ instance: { status: string } }>(instance)).instance.status, { timeout: 60_000 }).toBe("Completed");
    const executions = (await s.api<{ items: { execution: Execution }[] }>(`${instance}/activity-executions`)).items.map(item => item.execution);
    const consumer = executions.find(item => item.authoredActivityId === consumerId)!;
    expect(consumer?.status).toBe("Completed");
    if (producerId) expect(executions.filter(item => item.authoredActivityId === producerId)).toEqual([expect.objectContaining({ status: "Completed" })]);
    if (originalNeverRuns) expect(executions.filter(item => item.authoredActivityId === "producer")).toEqual([]);
    const detail = await s.api<{ valueSnapshots: { inputKey: string; evidenceId: string }[] }>(`${instance}/activity-executions/${consumer.activityExecutionId}`);
    const evidence = detail.valueSnapshots.find(item => item.inputKey === "text")!;
    const payload = await s.api<{ payload: { preview: string } }>(`${instance}/activity-executions/${consumer.activityExecutionId}/value-evidence/${evidence.evidenceId}/payload`);
    expect(payload.payload.preview).toBe(expected);
    runtime.push({ definitionId: created.definition.id, workflowExecutionId: run.workflowExecutionId, consumerId, producerId, originalNeverRuns, expected, committed: payload.payload.preview, executions });
    expect((await s.saved(created)).state).toEqual(before);
  }
  async function duplicateSelected() {
    // Right click preserves the real Flowchart multi-selection and opens the existing native action.
    await node("producer").click({ button: "right" });
    const menu = page.getByRole("menu", { name: "Activity actions", exact: true });
    await expect(menu).toBeVisible(); await menu.getByRole("menuitem", { name: /Duplicate/ }).click();
  }
  async function selectPair() {
    await s.canvas(); await node("producer").click(); await node("consumer").click({ modifiers: ["ControlOrMeta"] });
    await expect(page.locator(".wf-canvas .react-flow__node.selected")).toHaveCount(2);
  }

  if (mode === "pairs") {
    for (const role of ["whole", "member"] as const) {
      const producer: Node = role === "whole" ? timer() : { nodeId: "producer", activityVersionId: customerVersion, inputs: [], outputs: [] };
      const created = await s.seed(`copy flowchart ${role}`, s.root([producer, s.line()], "flowchart"));
      await s.open(created); await s.trigger().click();
      const projection = role === "whole" ? "Interval" : "Customer";
      await s.chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: new RegExp(`› ${projection}$`) }) }).click();
      if (role === "member") await s.commit(created, member, () => s.chooser().getByRole("option", { name: /^customerId/ }).click());
      else {
        await expect(s.chooser()).toHaveCount(0);
        await expect.poll(() => s.stored(created)).toEqual(whole);
      }
      await capture(page, info, `${role}-original`);
      const before = await s.saved(created);
      await selectPair(); await duplicateSelected(); await waitCount(created, 5);
      const duplicated = await savedNodes(created);
      const duplicatedConsumer = duplicated.slice(3).find(item => item.activityVersionId === s.line().activityVersionId)!;
      const duplicatedProducer = duplicated.slice(3).find(item => item.activityVersionId === producer.activityVersionId)!;
      const expected = { ...(role === "whole" ? whole : member), value: { ...((role === "whole" ? whole : member).value as object), producerNodeId: duplicatedProducer.nodeId } };
      expect(input(duplicatedConsumer)).toEqual(expected);
      receipts.push({ definitionId: created.definition.id, operation: `duplicate-${role}`, before: before.state, after: (await s.saved(created)).state,
        producerId: duplicatedProducer.nodeId, consumerId: duplicatedConsumer.nodeId });
      await s.select(duplicatedConsumer.nodeId); await expect(s.reference()).toContainText("(copy)");
      await capture(page, info, `${role}-duplicated`);
      await page.getByRole("button", { name: "Undo", exact: true }).click(); await waitCount(created, 3);
      expect((await s.saved(created)).state).toEqual(before.state);
      await page.getByRole("button", { name: "Redo", exact: true }).click(); await waitCount(created, 5);
      expect((await savedNodes(created)).find(item => item.nodeId === duplicatedConsumer.nodeId)).toEqual(duplicatedConsumer);
      await selectPair(); await node("consumer").press("ControlOrMeta+c"); await node("consumer").press("ControlOrMeta+v");
      await waitCount(created, 7);
      const pasted = (await savedNodes(created)).slice(5);
      const pastedProducer = pasted.find(item => item.activityVersionId === producer.activityVersionId)!;
      const pastedConsumer = pasted.find(item => item.activityVersionId === s.line().activityVersionId)!;
      expect(input(pastedConsumer)).toEqual({ ...expected, value: { ...(expected.value as object), producerNodeId: pastedProducer.nodeId } });
      receipts.push({ definitionId: created.definition.id, operation: `paste-${role}`, before: duplicated, after: (await s.saved(created)).state,
        producerId: pastedProducer.nodeId, consumerId: pastedConsumer.nodeId });
      await s.select(pastedConsumer.nodeId); await capture(page, info, `${role}-pasted`);
      await page.getByRole("button", { name: "Go to", exact: true }).click();
      await expect(page.locator(".wf-inspector-context")).toContainText(pastedProducer.nodeId);
      await page.getByRole("button", { name: "Set as start node", exact: true }).click();
      await expect.poll(async () => (await s.saved(created)).state.rootActivity.structure!.payload.startNodeId).toBe(pastedProducer.nodeId);
      await capture(page, info, `${role}-copied-start`);
      await codeReopen(created, pastedConsumer.nodeId); await capture(page, info, `${role}-reopened`);
      if (role === "member") {
        const viewport = page.viewportSize()!; await page.setViewportSize({ width: 320, height: 900 }); await s.select(pastedConsumer.nodeId);
        await capture(page, info, "copy-reference-320"); await page.setViewportSize(viewport);
      }
      await publishRun(created, pastedConsumer.nodeId, pastedProducer.nodeId, role === "whole" ? "00:00:01" : "cust-1042", true);
      await s.select(pastedConsumer.nodeId); await capture(page, info, `${role}-runtime`);
    }
    return;
  }

  if (mode === "scopes") {
    const variable = { referenceKey: "payload", name: "Payload", type: { alias: "Object", collectionKind: "Single" }, storageDriverType: "elsa.json",
      default: { expressionType: "Object", value: JSON.stringify({ "a.b": { "": "scope-copy-id" }, enabled: true }) } };
    const consumer = s.line(); consumer.inputs = [{ referenceKey: "text", value: { expressionType: "VariableMember", value: { referenceKey: "payload", declaringScopeId: "group", memberPath: ["a.b", ""] } } }];
    const portable = s.line(); portable.nodeId = "portable";
    const portableValue = { language: "JavaScript", source: 'customer.customerId', resultType: { alias: "String", collectionKind: "Single" },
      parameters: { customer: { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "Customer", isOptional: false } }, options: {}, capabilityProfile: "binding-pure-v1" };
    portable.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: portableValue } }];
    const switchVersion = await s.version("Elsa.Activities.Switch.Activities.Switch");
    const caseActivity = s.line(); caseActivity.nodeId = "case-consumer";
    caseActivity.inputs = [{ referenceKey: "text", value: { expressionType: "Literal", value: "switch-copy-value" } }];
    const switchNode = { nodeId: "switch", activityVersionId: switchVersion, inputs: [], outputs: [], structure: {
      kind: "elsa.switch.structure", schemaVersion: "2.0.0", payload: { cases: [{ caseId: "copy-case", match: "Approved", activity: caseActivity,
        condition: { referenceKey: "Case:copy-case:Condition", value: { expressionType: "VariableMember", value: {
          referenceKey: "payload", declaringScopeId: "group", memberPath: ["enabled"] } } } }], default: null } } } as unknown as Node;
    const group = s.root([{ nodeId: "producer", activityVersionId: customerVersion, inputs: [], outputs: [] }, consumer, portable, switchNode], "sequence", [variable]); group.nodeId = "group";
    const outer = s.root([group], "sequence", [{ ...variable, default: { expressionType: "Object", value: '{"a.b":{"":"outer-shadow"}}' } }]);
    const created = await s.seed("copy nested scope and portable", outer);
    await page.goto(`/workflows/definitions?definition=${created.definition.id}&backend=legacy`); await expect(node("group")).toBeAttached();
    await s.select("group"); await page.getByRole("button", { name: "Duplicate Sequence", exact: true }).click();
    await waitCount(created, 13);
    const copiedGroup = (await s.saved(created)).state.rootActivity.structure!.payload.activities[1];
    const [copiedProducer, copiedConsumer, copiedPortable, copiedSwitch] = copiedGroup.structure!.payload.activities;
    const copiedCase = (copiedSwitch.structure!.payload.cases as { activity: Node; condition: { value: Binding } }[])[0];
    expect(copiedCase.activity.nodeId).not.toBe("case-consumer");
    expect(copiedCase.condition.value).toEqual({ expressionType: "VariableMember", value: { referenceKey: "payload", declaringScopeId: copiedGroup.nodeId, memberPath: ["enabled"] } });
    expect(input(copiedConsumer)).toEqual({ expressionType: "VariableMember", value: { referenceKey: "payload", declaringScopeId: copiedGroup.nodeId, memberPath: ["a.b", ""] } });
    expect(input(copiedPortable)).toEqual({ expressionType: "JavaScript", value: { ...portableValue, parameters: { customer: { ...portableValue.parameters.customer, producerNodeId: copiedProducer.nodeId } } } });
    receipts.push({ definitionId: created.definition.id, operation: "duplicate-sequence-scope-portable", before: outer, after: (await s.saved(created)).state,
      producerId: copiedProducer.nodeId, consumerId: copiedConsumer.nodeId });
    await codeReopen(created, copiedGroup.nodeId, "group");
    await s.canvas(); await node(copiedGroup.nodeId).getByRole("button", { name: /^Open Activities/ }).click();
    await s.select(copiedConsumer.nodeId); await expect(s.reference()).toContainText('"a.b"'); await expect(s.reference()).toContainText('""');
    await capture(page, info, "scope-copied-reference");
    await s.select(copiedSwitch.nodeId); await capture(page, info, "switch-copied-condition");
    await s.select(copiedPortable.nodeId); await capture(page, info, "portable-copied-preserved");
    const minimal = process.env.UX_ACTIVITY_DATA_COMPOSITION === "minimal";
    if (minimal) {
      const descriptors = await s.api<{ items: { type: string }[] }>("/expressions/descriptors");
      const features = (await s.api<{ features: FeatureFact[] }>("/modularity/features")).features;
      const mandatoryClosure = resolvedFeatureClosure(features);
      expect(mandatoryClosure.filter(id => optionalBackendFeatures.includes(id))).toEqual([]);
      expect(descriptors.items.some(item => /JavaScript|Liquid/i.test(item.type))).toBe(false);
      boundaries.push({ kind: "missing-language", bodyPreservedWithRemappedIdentity: true, descriptors, features, mandatoryClosure,
        execution: "unverified: JavaScript runtime is deliberately absent; imported body remains canonical" });
    } else {
      await publishRun(created, copiedConsumer.nodeId, copiedProducer.nodeId, "scope-copy-id");
      // The same real run must also execute the copied portable consumer with its remapped producer.
      const run = runtime.at(-1) as { executions: Execution[] };
      expect(run.executions.find(item => item.authoredActivityId === copiedPortable.nodeId)?.status).toBe("Completed");
      expect(run.executions.find(item => item.authoredActivityId === copiedCase.activity.nodeId)?.status).toBe("Completed");
    }
    return;
  }

  // Consumer-only references retain valid or invalid external identity. This does not enable cross-resource transfer.
  for (const valid of [true, false]) {
    const consumer = s.line(); const selected = valid ? whole : { ...whole, value: { ...(whole.value as object), producerNodeId: "missing-producer" } };
    consumer.inputs = [{ referenceKey: "text", value: selected }];
    const root = s.root(valid ? [timer(), consumer] : [consumer]); root.nodeId = `external-${info.project.name}-${Date.now()}`;
    const created = await s.seed(`consumer-only ${valid}`, root);
    await s.open(created); await capture(page, info, valid ? "external-valid-original" : "external-invalid-original");
    await page.getByRole("button", { name: "Duplicate Write Line", exact: true }).click();
    await waitCount(created, valid ? 4 : 3);
    const copiedConsumer = (await s.saved(created)).state.rootActivity.structure!.payload.activities.at(-1)!;
    expect(input(copiedConsumer)).toEqual(selected); await s.select(copiedConsumer.nodeId);
    receipts.push({ definitionId: created.definition.id, operation: valid ? "consumer-only-valid" : "consumer-only-invalid", before: root,
      after: (await s.saved(created)).state, consumerId: copiedConsumer.nodeId });
    await capture(page, info, valid ? "external-valid-copy" : "external-invalid-copy");
    if (valid) {
      await publishRun(created, copiedConsumer.nodeId, "producer", "00:00:01");
      await s.select("consumer"); await s.canvas(); await node("consumer").press("ControlOrMeta+c");
      const destination = await s.seed("resource-local same IDs", s.root([timer(), s.line()]));
      await s.open(destination); const before = (await s.saved(destination)).state;
      await s.canvas(); await node("consumer").press("ControlOrMeta+v");
      expect((await s.saved(destination)).state).toEqual(before);
      await expect(page.locator(".wf-sequence-step")).toHaveCount(2);
      boundaries.push({ kind: "resource-local", source: created.definition.id, destination: destination.definition.id,
        sameIds: ["producer", "consumer"], clipboardCleared: true, crossResourceTransfer: "future T034/T035 obligation" });
      await capture(page, info, "foreign-resource-clipboard-cleared");
    } else {
      await expect(page.locator(".wf-data-reference-problem")).toContainText("Replace it or clear it");
      const snapshot = () => JSON.parse(execFileSync("python3", [resolve("../e2e-tests/activity-data-selection/publication_snapshot.py"),
        process.env.UX_ACCEPTANCE_OWNED_ROOT!, created.definition.id, root.nodeId], { encoding: "utf8" }));
      const before = snapshot();
      await page.getByRole("button", { name: "Review & publish", exact: true }).click();
      const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
      await expect(review.getByRole("alert").filter({ hasText: /Fix these issues before publishing|Publication blocked/ })).toContainText(/producer|source/i);
      await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeDisabled();
      const current = await s.saved(created);
      const refused = await page.request.post(process.env.ELSA_WORKBENCH_URL + "/publishing/workflows/preflight", {
        data: { definitionId: created.definition.id, state: current.state, layout: current.layout, slotName: "default", action: "replace" }
      });
      const refusal = { status: refused.status(), body: await refused.text() };
      expect(refused.ok(), refusal.body).toBe(false); expect(refusal.body).toMatch(/producer|ValueFlow/i);
      const after = snapshot(); expect(after).toEqual(before);
      boundaries.push({ kind: "invalid-publication-no-write", definitionId: created.definition.id, before, after,
        reference: input(copiedConsumer), refusal, gate: "actual publication review refuses before promotion/publication" });
      await capture(page, info, "invalid-publication-refused");
      await review.getByRole("button", { name: "Cancel", exact: true }).click();
      const viewport = page.viewportSize()!; await page.setViewportSize({ width: 320, height: 900 }); await s.select(copiedConsumer.nodeId);
      await capture(page, info, "invalid-reference-320"); await page.setViewportSize(viewport);
    }
  }
}
