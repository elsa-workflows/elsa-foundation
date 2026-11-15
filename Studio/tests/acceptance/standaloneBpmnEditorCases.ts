import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { TriggerJourney, configured, definitionPath, input, type Authored, type WorkflowState } from "./standaloneTriggersSupport";
import type { InputMapping } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";
import { businessOutputs } from "./standaloneTriggerOrdinaryCases";

async function process(j: TriggerJourney, name: string, inputs: WorkflowState["inputs"] = []): Promise<Authored> {
  return j.submit(name, { inputs, outputs: [], variables: [], rootActivity: {
    nodeId: "process", activityVersionId: await j.version("Elsa.Activities.Bpmn.Activities.BpmnProcess"), inputs: [], outputs: [],
    structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      activities: [], elements: [{ elementId: "start", elementType: "startEvent" },
        { elementId: "service", elementType: "task", name: "Consume saved mapping" }, { elementId: "end", elementType: "endEvent" }],
      sequenceFlows: [{ flowId: "a", sourceRef: "start", targetRef: "service" }, { flowId: "b", sourceRef: "service", targetRef: "end" }]
    } }
  } });
}
async function selectWorker(j: TriggerJourney, authored: Authored, name: string) {
  const page = j.page;
  await page.goto(definitionPath(authored.definition.id));
  await page.locator('.react-flow__node[data-id="service"]').press("Enter"); await j.openAuthoringInspector();
  await page.getByRole("button", { name: "Bind a published service workflow", exact: true }).press("Enter");
  const picker = page.getByRole("combobox", { name: /workflow/i }).first();
  await picker.fill(`${name} ${j.stamp}`);
  await page.getByRole("option").filter({ hasText: `${name} ${j.stamp}` }).first().click();
  await page.getByLabel("Service publication", { exact: true }).selectOption({ label: "default" });
  await page.getByRole("button", { name: "Use this service publication", exact: true }).press("Enter");
  await expect(page.getByRole("heading", { name: "Map service workflow inputs", exact: true })).toBeFocused();
}
async function savePublishAndConsume(j: TriggerJourney, authored: Authored, expected: Record<string, unknown>, mappings: InputMapping[] = []) {
  const save = j.page.getByRole("button", { name: "Save", exact: true });
  await expect(save).toBeEnabled();
  const completedSave = j.page.waitForResponse(response => response.request().method() === "PUT" &&
    new URL(response.url()).pathname.endsWith(`/design/workflows/drafts/${encodeURIComponent(authored.draftId)}`));
  await save.press("Enter"); const response = await completedSave;
  expect(response.ok(), await response.text()).toBe(true); await response.finished();
  let saved: { state: WorkflowState } | undefined;
  const bindingOf = (state: WorkflowState) =>
    (state.rootActivity.structure!.payload.elements as { elementId: string; properties?: Record<string, string> }[])
      .find(element => element.elementId === "service")?.properties?.["elsa.direct.binding"];
  // A PUT already in flight may finish before the newly applied mapping's autosave. Promote only the real committed binding.
  await expect.poll(async () => {
    saved = await j.api<{ state: WorkflowState }>(`/design/workflows/drafts/${authored.draftId}`);
    return bindingOf(saved.state);
  }, { timeout: 45_000, message: "The committed draft retains the applied worker binding before promotion" }).toBeTruthy();
  expect(JSON.parse(bindingOf(saved!.state)!).formatVersion).toBe(2);
  const version = await j.api<{ id: string }>(`/design/workflows/drafts/${authored.draftId}/promote`, "POST", { operationKey: randomUUID() });
  await j.api(`/interchange/bpmn/versions/${version.id}/publish-direct`, "POST", { requestId: randomUUID() });
  const trigger = await j.create("event", "Saved BPMN mapping consumer");
  await j.add(trigger.id, authored.definition.id, mappings); await j.enable(trigger.id); await j.event(String(trigger.configuration.eventName));
  const delivery = await j.started(await j.waitDelivery(trigger.id));
  const id = delivery.recipients[0].executionReference!.id;
  let run: { execution: { status: string }; attempts: { workerExecutionId: string }[] } | undefined;
  await expect.poll(async () => { run = await j.api(`/bpmn/executions/${id}/inspection`); return run!.attempts.length; }, { timeout: 45_000 }).toBe(1);
  await businessOutputs(j, run!.attempts[0].workerExecutionId, expected);
  await expect.poll(async () => (await j.api<{ execution: { status: string } }>(`/bpmn/executions/${id}/inspection`)).execution.status,
    { timeout: 45_000 }).toBe("TerminalSettled");
  return saved!.state;
}

export async function verifyDefaultAndNullWorkerEditor(j: TriggerJourney) {
  // Keep this producer distinct from earlier appearances while deliberately deduplicating its logical publication below.
  const defaultMessage = `worker default ${j.stamp}`;
  const defaults = [{ ...input("message"), defaultValue: defaultMessage, defaultSyntax: "Literal" },
    { ...input("count", "Int32"), defaultValue: 7, defaultSyntax: "Literal" }];
  const producer = await j.echo("Original default worker producer", defaults);
  const logicalName = "Logical default worker publication";
  const logical = await j.submit(logicalName, producer.state);
  const publication = await j.publish(logical);
  expect(publication.artifactId).toBe(producer.published.artifactId);
  const details = await j.api<{ producerIdentity: { definitionId: string; definitionVersionId: string }; chosenReference: { sourceReferenceId: string }; references: { sourceReferenceId: string; definitionId: string }[] }>(
    `/runtime/workflows/executables/${encodeURIComponent(publication.artifactId)}?ref=${encodeURIComponent(publication.sourceReferenceId)}`);
  expect(details.producerIdentity.definitionId).toBe(producer.authored.definition.id);
  expect(details.chosenReference.sourceReferenceId).toBe(publication.sourceReferenceId);
  const logicalReference = details.references.find(reference => reference.sourceReferenceId === publication.sourceReferenceId);
  expect(logicalReference).toBeTruthy();
  expect(logicalReference!.definitionId).toBe(logical.definition.id);
  expect(details.producerIdentity.definitionId).not.toBe(logicalReference!.definitionId);

  const defaultProcess = await process(j, "Default-only BPMN worker mappings");
  await selectWorker(j, defaultProcess, logicalName);
  await expect(j.page.getByLabel("Service input message source", { exact: true })).toHaveValue("Omit");
  await expect(j.page.getByLabel("Service input count source", { exact: true })).toHaveValue("Omit");
  await j.page.getByRole("button", { name: "Apply service input mapping", exact: true }).press("Enter");
  const state = await savePublishAndConsume(j, defaultProcess, { message: defaultMessage, count: 7 });
  const element = (state.rootActivity.structure!.payload.elements as { elementId: string; properties: Record<string, string> }[]).find(item => item.elementId === "service")!;
  const mapping = JSON.parse(element.properties["elsa.direct.binding"]);
  expect(mapping.formatVersion).toBe(2); expect(mapping.inputs).toEqual({});
  expect(mapping.sourceReferenceId).toBe(publication.sourceReferenceId);
  expect(mapping.executable.definitionId).toBe(details.producerIdentity.definitionId);
  await j.page.goto(definitionPath(defaultProcess.definition.id)); await j.page.locator('.react-flow__node[data-id="service"]').press("Enter"); await j.openAuthoringInspector();
  await expect(j.page.getByRole("combobox", { name: /workflow/i }).first()).toHaveValue(`${logicalName} ${j.stamp}`);
  await expect(j.page.getByRole("region", { name: "Published service workflow", exact: true })).toContainText("Retained service version");
  await expect(j.page.getByRole("region", { name: "Saved service input mappings", exact: true })).toBeVisible();

  const nullProcess = await process(j, "Explicit-null BPMN worker value");
  await selectWorker(j, nullProcess, logicalName);
  await j.page.getByLabel("Service input message source", { exact: true }).selectOption("Literal");
  await j.page.getByLabel("JSON value for service message", { exact: true }).fill("null");
  await j.page.getByRole("button", { name: "Apply service input mapping", exact: true }).press("Enter");
  await savePublishAndConsume(j, nullProcess, { message: null, count: 7 });
  for (let index = j.unverified.length - 1; index >= 0; index--) {
    if (["worker default-only empty v2 mapping", "logical worker publication versus producer pin"].includes(j.unverified[index].state)) j.unverified.splice(index, 1);
  }
  j.observed.push("All-omitted nonempty worker contract retained v2 inputs:{} and actual worker defaults",
    "Deduplicated logical service publication retained its different immutable producer pin through save/reopen/publish/execution",
    "Literal null worker value survived the editor and genuine BPMN/native-worker execution without becoming an omitted default");
}


export async function verifyExactWorkerCollectionMapping(j: TriggerJourney) {
  const list = { ...input("values"), type: { alias: "String", collectionKind: "List" } };
  const workerName = "Exact List worker consumer";
  await j.echo(workerName, [list]);
  const authored = await process(j, "Exact collection model inputs", [
    { ...input("listValues"), type: { alias: "String", collectionKind: "List" } },
    { ...input("arrayValues"), type: { alias: "String", collectionKind: "Array" } }
  ]);
  await selectWorker(j, authored, workerName);
  await j.page.getByLabel("Service input values source", { exact: true }).selectOption("Input");
  const options = j.page.getByLabel("Process input for service values", { exact: true });
  await expect(options.locator('option[value="arrayValues"]')).toBeDisabled();
  await expect(options.locator('option[value="listValues"]')).toBeEnabled();
  await options.selectOption("listValues");
  await j.page.getByRole("button", { name: "Apply service input mapping", exact: true }).press("Enter");
  await savePublishAndConsume(j, authored, { values: ["actual List consumed"] }, [
    configured("listValues", ["actual List consumed"]), configured("arrayValues", ["different Array remains separate"])
  ]);
  for (let index = j.unverified.length - 1; index >= 0; index--) {
    if (j.unverified[index].state === "exact worker collection compatibility") j.unverified.splice(index, 1);
  }
  j.observed.push("Same-alias Array input was visibly unavailable for a List worker; exact List mapping saved, published and consumed its actual collection");
}
