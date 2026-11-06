import { randomUUID } from "node:crypto";
import { expect } from "@playwright/test";
import { TriggerJourney, configured, input, source, type Authored, type DispatchBoundary, type WorkflowState } from "./standaloneTriggersSupport";
import type { ChangePreview, DeliveryView, TriggerView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

type EchoTarget = Awaited<ReturnType<TriggerJourney["echo"]>>;
type ScheduleActions = { pause(id: string, capture?: string): Promise<void>; replace(authored: Authored, state: WorkflowState): Promise<unknown> };
type BusinessOutput = { value: unknown; isRedacted: boolean };

function scalarOutput(output: BusinessOutput): unknown {
  expect(output, "The business workflow durably assigned its named output").toBeTruthy();
  expect(output.isRedacted).toBe(false);
  const value = output.value;
  if (value && typeof value === "object") {
    const snapshot = value as { kind?: string; preview?: unknown; value?: unknown };
    if (snapshot.kind === "null") return null;
    if (Object.hasOwn(snapshot, "preview")) return snapshot.preview;
    if (Object.hasOwn(snapshot, "value")) return snapshot.value;
  }
  return value;
}

async function businessOutputs(j: TriggerJourney, executionId: string, expected: Record<string, unknown>) {
  let result: { instance: { status: string; artifactId: string; definitionVersionId: string }; outputs: Record<string, BusinessOutput> } | undefined;
  await expect.poll(async () => { result = await j.api(`/runtime/workflows/instances/${executionId}`); return result!.instance.status; }, { timeout: 45_000 }).toBe("Completed");
  for (const [name, value] of Object.entries(expected)) expect(scalarOutput(result!.outputs[name]), `Actual business output ${name}`).toEqual(value);
  return result!;
}

async function editSubscription(j: TriggerJourney, triggerId: string, definitionId: string) {
  await j.open(triggerId);
  await j.page.getByRole("row").filter({ hasText: definitionId }).getByRole("button", { name: "Edit", exact: true }).click();
  await expect(j.page.getByRole("heading", { name: "Declared inputs", exact: true })).toBeVisible();
}

async function saveSubscription(j: TriggerJourney) {
  await j.page.getByRole("checkbox", { name: "Authorize this shared trigger to invoke the workflow", exact: true }).check();
  await j.page.getByRole("button", { name: "Review subscription", exact: true }).click(); await j.review();
}

export async function verifyOptionalInputSemantics(j: TriggerJourney) {
  const declarations = [{ ...input("message", "String", false), defaultValue: "Declared workflow default", defaultSyntax: "Literal" }];
  const target = await j.echo("Optional default and explicit null business consumer", declarations);
  const before = await j.api<{ state: WorkflowState }>(`/design/workflows/drafts/${target.authored.draftId}`);
  const trigger = await j.create("event", "Optional input semantics");
  const subscription = await j.add(trigger.id, target.authored.definition.id, []);
  expect(subscription.mappings).toEqual([]);
  await j.enable(trigger.id); await j.event(String(trigger.configuration.eventName), {});
  const omitted = await j.started(await j.waitDelivery(trigger.id));
  await businessOutputs(j, omitted.recipients[0].executionReference!.id, { message: "Declared workflow default" });
  await editSubscription(j, trigger.id, target.authored.definition.id);
  await j.page.getByLabel("Value source for message", { exact: true }).selectOption("Null");
  await saveSubscription(j);
  expect((await j.subscriptions(trigger.id))[0].mappings).toEqual([configured("message", null)]);
  await j.event(String(trigger.configuration.eventName), {});
  const explicitNull = await j.started(await j.waitDelivery(trigger.id, new Set([omitted.id])));
  await businessOutputs(j, explicitNull.recipients[0].executionReference!.id, { message: null });

  await editSubscription(j, trigger.id, target.authored.definition.id);
  await j.page.getByLabel("Value source for message", { exact: true }).selectOption("SourceField");
  await j.page.getByLabel("Source field for message", { exact: true }).selectOption("custom");
  const path = j.page.getByLabel("Source path for message", { exact: true });
  await path.fill("payload/customer.name/id~1value/~0key");
  await j.page.getByRole("button", { name: "Review subscription", exact: true }).click();
  await expect(path).toHaveAttribute("aria-invalid", "true");
  await expect(path).toHaveValue("payload/customer.name/id~1value/~0key");
  await expect(j.page.getByRole("button", { name: "Apply reviewed change", exact: true })).toHaveCount(0);
  await path.fill("/payload/customer.name/id~1value/~0key"); await j.capture("subscription-mapping");
  await saveSubscription(j);
  expect((await j.subscriptions(trigger.id))[0].mappings).toEqual([source("message", ["payload", "customer.name", "id/value", "~key"])]);
  await j.page.reload(); await editSubscription(j, trigger.id, target.authored.definition.id);
  await expect(j.page.getByLabel("Source field for message", { exact: true })).toHaveValue("custom");
  await expect(j.page.getByLabel("Source path for message", { exact: true })).toHaveValue("/payload/customer.name/id~1value/~0key");
  await j.page.getByRole("button", { name: "Cancel", exact: true }).click();
  await j.event(String(trigger.configuration.eventName), { "customer.name": { "id/value": { "~key": "Nested source consumed" } } });
  const nested = await j.started(await j.waitDelivery(trigger.id, new Set([omitted.id, explicitNull.id])));
  await businessOutputs(j, nested.recipients[0].executionReference!.id, { message: "Nested source consumed" });
  expect((await j.api<{ state: WorkflowState }>(`/design/workflows/drafts/${target.authored.draftId}`)).state).toEqual(before.state);
  j.observed.push("An omitted optional input consumed its declared workflow default; explicit null consumed null through an actual Event and SetOutput", "A custom nested payload path with dots, slash and tilde was authored, corrected, saved and reopened through UI, then consumed by the actual business workflow", "All three admissions retained the same trigger-free business graph");
}

async function editSchedule(j: TriggerJourney, id: string, fields: Record<string, string>, capture?: string) {
  await j.open(id); await j.page.getByRole("button", { name: "Edit configuration", exact: true }).click();
  for (const [label, value] of Object.entries(fields)) await j.page.getByLabel(label, { exact: true }).fill(value);
  await j.page.getByRole("button", { name: "Review changes", exact: true }).click(); await j.review(capture);
}

export async function verifyScheduledRetention(j: TriggerJourney, targets: EchoTarget[], actions: ScheduleActions) {
  const timer = await j.create("timer", "Captured due occurrence survives schedule edit", { interval: "PT15S" });
  for (const target of targets) await j.add(timer.id, target.authored.definition.id, [source("message", ["metadata", "dueAt"]), configured("count", 9)]);
  try {
    // Arm only while paused, so this exact first due occurrence reaches the owned physical seal.
    await j.host("arm-dispatch-boundary", { triggerId: timer.id });
    await j.enable(timer.id);
    const armed = await j.read(timer.id); expect(armed.schedule?.nextDueUtc).toBeTruthy();
    await j.page.reload(); await expect(j.page.getByRole("region", { name: "Schedule", exact: true })).toContainText("Next firing:"); await j.capture("schedule-next-firing");
    const boundary = (await j.host("await-dispatch-boundary")).boundary as DispatchBoundary;
    const held = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${boundary.deliveryId}`);
    expect(held).toMatchObject({ triggerId: timer.id, acceptedCount: 2, refusedCount: 0 });
    expect(held.recipients.find(item => item.id === boundary.recipientId)).toMatchObject({ dispatch: "DispatchCommitted" });
    expect(held.recipients.find(item => item.id !== boundary.recipientId)).toMatchObject({ dispatch: "NotDispatched" });
    expect(held.recipients.every(item => !item.executionReference)).toBe(true);
    await j.inspectDelivery(held, "schedule-delivery");
    await actions.pause(timer.id, "trigger-paused");
    await editSchedule(j, timer.id, { Interval: "PT30S" }, "shared-change-review");
    for (const target of targets) {
      const replacement = structuredClone(target.state);
      (replacement.rootActivity.structure!.payload.activities as { inputs: unknown[] }[])[0].inputs = [{ referenceKey: "name", value: { expressionType: "Literal", value: "message" } }, { referenceKey: "value", value: { expressionType: "Literal", value: "New serving version must not replace the accepted due input" } }];
      await actions.replace(target.authored, replacement);
    }
    const changed = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${held.id}`);
    for (const recipient of changed.recipients) {
      expect(recipient.capturedVersion).toBe(held.recipients.find(item => item.id === recipient.id)!.capturedVersion);
      expect(recipient.currentVersion).not.toBe(recipient.capturedVersion); expect(recipient.executionReference).toBeFalsy();
    }
    const recovery = await j.host("recover-on-peer");
    expect(recovery.recovery).toMatchObject({ stopSignal: "SIGKILL", naturalLeaseWaitSeconds: 61, leaseMutated: false, clockChanged: false });
    const completed = await j.started(held);
    for (const recipient of completed.recipients) {
      const original = targets.find(item => item.authored.definition.id === recipient.target!.definitionId)!;
      const result = await businessOutputs(j, recipient.executionReference!.id, { count: 9 });
      expect(result.instance.artifactId).toBe(original.published.artifactId);
      expect(result.instance.definitionVersionId).toBe(original.authored.version.id);
      expect(Date.parse(String(scalarOutput(result.outputs.message)))).toBe(Date.parse(armed.schedule!.nextDueUtc!));
    }
    expect((await j.read(timer.id)).configuration.interval).toBe("PT30S"); expect((await j.read(timer.id)).desiredState).toBe("Paused");
    await j.page.reload(); await j.capture("subscriber-runs");
    j.requests.push({ kind: "actual-scheduled-accepted-capture-retained-after-configuration-and-serving-change", triggerId: timer.id, originalDueUtc: armed.schedule!.nextDueUtc, deliveryId: held.id, originalRecipients: held.recipients, completedRecipients: completed.recipients });
    j.observed.push("A real timer due occurrence accepted two roots; one was held after the physical dispatch seal and the other was still pending", "Pause preceded the reviewed interval edit and serving replacements; actual peer recovery completed both original captured versions and original due-time business outputs");
  } finally { await j.host("restore-features"); }
  // Subsequent ordinary-source recovery consumes the same business request inputs again.
  for (const target of targets) await actions.replace(target.authored, target.state);
}

export async function verifyCronAndOverdueRecovery(j: TriggerJourney, targets: EchoTarget[], actions: ScheduleActions) {
  const cron = await j.create("cron", "Timezone and future generation");
  await editSchedule(j, cron.id, { "Cron expression": "0 0 9 * * *", Timezone: "UTC" });
  await j.add(cron.id, targets[0].authored.definition.id, [configured("message", "Cron timezone qualification"), configured("count", 1)]); await j.enable(cron.id);
  const before = await j.read(cron.id); expect(before.schedule?.timezone).toBe("UTC");
  expect(before.schedule?.nextDueUtc).toBeTruthy(); expect(before.schedule?.offset).toBe("+00:00");
  await j.open(cron.id); await j.page.getByRole("button", { name: "Edit configuration", exact: true }).click();
  await j.page.getByLabel("Cron expression", { exact: true }).fill("0 0 10 * * *");
  await j.page.getByLabel("Timezone", { exact: true }).fill("America/New_York");
  const previewUrl = new URL(`${j.base}/runtime/standalone-triggers/${encodeURIComponent(cron.id)}/changes/preview`);
  const previewResponse = j.page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "POST" && url.origin === previewUrl.origin && url.pathname === previewUrl.pathname;
  });
  const previewRequestedAt = Date.now();
  await j.page.getByRole("button", { name: "Review changes", exact: true }).click();
  const response = await previewResponse;
  expect(response.ok(), "The installed schedule module prepares the actual UI-issued next-firing review").toBe(true);
  expect(response.request().postDataJSON()).toMatchObject({ expectedRevision: before.revision,
    command: { kind: "SaveConfiguration", configuration: { expression: "0 0 10 * * *", timezone: "America/New_York" } } });
  const preview = await response.json() as ChangePreview;
  expect(preview.canApply).toBe(true); expect(preview.observedRevision).toBe(before.revision);
  const nextFirings = preview.impact.filter(item => item.label === "Next firing"); expect(nextFirings).toHaveLength(1);
  const nextFiring = nextFirings[0];
  const cursor = (value: string | undefined, description: string) => {
    expect(value, description).toBeTruthy();
    const parts = value!.split(" | "); expect(parts, description).toHaveLength(3);
    expect(parts[0], "The source module projects an explicit UTC instant").toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z$/);
    expect(Number.isFinite(Date.parse(parts[0])), description).toBe(true);
    expect(parts[1], "The source module includes the schedule timezone").not.toBe("");
    expect(parts[2], "The source module includes the signed UTC offset").toMatch(/^[+-]\d{2}:\d{2}$/);
    return { utc: parts[0], timezone: parts[1], offset: parts[2] };
  };
  const current = cursor(nextFiring.before, "Preview exposes the actual current persisted cursor");
  const proposed = cursor(nextFiring.after, "Preview exposes the installed planner's proposed next firing");
  expect(Date.parse(current.utc)).toBe(Date.parse(before.schedule!.nextDueUtc!));
  expect(current.timezone).toBe(before.schedule!.timezone); expect(current.offset).toBe(before.schedule!.offset);
  expect(proposed.timezone).toBe("America/New_York");
  expect(Date.parse(proposed.utc)).toBeGreaterThan(previewRequestedAt);
  expect(Date.parse(proposed.utc)).not.toBe(Date.parse(current.utc));
  const proposedOffset = new Intl.DateTimeFormat("en-US", { timeZone: proposed.timezone, timeZoneName: "longOffset" })
    .formatToParts(new Date(proposed.utc)).find(part => part.type === "timeZoneName")!.value.replace(/^GMT/, "");
  expect(proposed.offset).toBe(proposedOffset);
  expect(nextFiring.description).toMatch(/^Action: Replace\./);
  const impactRow = j.page.getByRole("region", { name: "Change impact", exact: true }).getByRole("row").filter({ hasText: "Next firing" });
  await expect(impactRow).toHaveCount(1);
  await expect(impactRow.getByRole("cell").nth(0)).toHaveText(nextFiring.before!);
  await expect(impactRow.getByRole("cell").nth(1)).toHaveText(nextFiring.after!);
  await expect(impactRow).toContainText(nextFiring.description!);
  const stillCurrent = await j.read(cron.id);
  expect(stillCurrent.revision).toBe(before.revision); expect(stillCurrent.configuration).toEqual(before.configuration);
  expect(stillCurrent.schedule?.nextDueUtc).toBe(before.schedule?.nextDueUtc); expect(stillCurrent.schedule?.generation).toBe(before.schedule?.generation);
  j.requests.push({ kind: "actual-ui-cron-next-firing-preview", backendUrl: response.url(), status: response.status(), triggerId: cron.id,
    persistedSchedule: before.schedule, current, proposed, impact: nextFiring, observedRevision: preview.observedRevision, previewDidNotMutateSchedule: true });
  await j.review("cron-next-firing-preview");
  const changed = await j.read(cron.id);
  expect(changed.schedule?.timezone).toBe("America/New_York"); expect(changed.schedule!.generation).toBeGreaterThan(before.schedule!.generation!);
  expect(changed.schedule?.nextDueUtc).not.toBe(before.schedule?.nextDueUtc); expect(Date.parse(changed.schedule!.nextDueUtc!)).toBeGreaterThan(Date.now());
  await j.page.reload(); await expect(j.page.getByRole("region", { name: "Schedule", exact: true })).toContainText("America/New_York"); await actions.pause(cron.id);

  const timer = await j.create("timer", "Retained overdue zero-eligibility occurrence", { interval: "PT15S" });
  for (const target of targets) await j.add(timer.id, target.authored.definition.id, [source("message", ["metadata", "dueAt"]), configured("count", 23)]);
  await j.enable(timer.id); const armed = await j.read(timer.id);
  await Promise.all(targets.map(target => j.api(`/publishing/workflows/${target.authored.definition.id}/slots/default`, "DELETE")));
  let overdue: TriggerView | undefined;
  await expect.poll(async () => { overdue = await j.read(timer.id); return overdue.schedule?.isOverdue && (overdue.schedule?.failureCount ?? 0) > 0 && overdue.schedule?.failureReason === "NoEligibleSubscribers"; }, { timeout: 45_000 }).toBe(true);
  const waitingReason = overdue!.findings.find(finding => finding.code === "ScheduleNoEligibleSubscribers");
  const waitingMessage = "Waiting for eligible subscribers. The due occurrence is retained and will be retried.";
  expect(waitingReason).toMatchObject({ code: "ScheduleNoEligibleSubscribers", message: waitingMessage });
  expect(overdue!.findings.some(finding => finding.code === "ScheduleAdmissionUnavailable")).toBe(false);
  expect(overdue!.schedule?.nextDueUtc).toBe(armed.schedule?.nextDueUtc); expect(overdue!.schedule?.generation).toBe(armed.schedule?.generation);
  expect(overdue!.schedule?.nextRetryAt).toBeTruthy(); expect(Date.parse(overdue!.schedule!.nextRetryAt!)).toBeGreaterThan(Date.parse(armed.schedule!.nextDueUtc!));
  expect((await j.deliveries(timer.id)).filter(item => item.acceptedCount > 0)).toEqual([]);
  await j.page.reload();
  const waitingSchedule = j.page.getByRole("region", { name: "Schedule", exact: true });
  await expect(waitingSchedule).toContainText(/overdue|retained|retry/i);
  await expect(waitingSchedule).toContainText(waitingMessage);
  await expect(waitingSchedule).not.toContainText("Schedule admission is unavailable.");
  await j.capture("schedule-no-eligible-subscribers");
  j.requests.push({ kind: "actual-schedule-zero-eligibility-retains-one-overdue-cursor-with-backoff", triggerId: timer.id, armed: armed.schedule, observed: overdue!.schedule, finding: waitingReason, acceptedDeliveryCount: 0 });
  await j.api(`/publishing/workflows/${targets[0].authored.definition.id}/slots/default/restore`, "POST", {});
  const partial = await j.waitDelivery(timer.id);
  expect(partial).toMatchObject({ admission: "PartiallyAccepted", admissionPolicy: "Independent", acceptedCount: 1, refusedCount: 1 });
  const started = await j.started(partial); const recipient = started.recipients.find(item => item.admission === "Accepted")!;
  const result = await businessOutputs(j, recipient.executionReference!.id, { count: 23 });
  expect(Date.parse(String(scalarOutput(result.outputs.message)))).toBe(Date.parse(armed.schedule!.nextDueUtc!));
  await expect.poll(async () => Date.parse((await j.read(timer.id)).schedule!.nextDueUtc!) > Date.now(), { timeout: 45_000 }).toBe(true);
  const future = await j.read(timer.id); expect(future.schedule?.generation).toBe(armed.schedule?.generation);
  await actions.pause(timer.id);
  const refused = started.recipients.find(item => item.admission === "Refused")!; expect(refused.executionReference).toBeFalsy();
  await j.api(`/publishing/workflows/${targets[1].authored.definition.id}/slots/default/restore`, "POST", {});
  const repaired = await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${partial.id}`);
  expect(repaired.acceptedCount).toBe(1); expect(repaired.refusedCount).toBe(1);
  expect(repaired.recipients.find(item => item.id === refused.id)).toEqual(refused);
  expect(repaired.recipients.map(item => item.id)).toEqual(started.recipients.map(item => item.id));
  await j.open(timer.id); await j.page.reload(); await expect(j.page.getByRole("region", { name: "Schedule", exact: true })).toContainText("Paused");
  j.requests.push({ kind: "actual-old-due-consumed-then-one-future-cursor-and-frozen-independent-refusals", triggerId: timer.id, originalDueUtc: armed.schedule!.nextDueUtc, futureDueUtc: future.schedule!.nextDueUtc, original: started, afterTargetRepair: repaired });
  j.observed.push("Actual Cron UI preview matched the persisted current UTC cursor and showed the installed planner's proposed UTC instant, America/New_York timezone, signed offset and Replace action in the visible Next firing row before Apply; preview left resource and schedule unchanged", "Actual Cron UI changed timezone and expression into a persisted future schedule generation", "With both serving targets unpublished, a real due timer retained one overdue cursor with failed-attempt backoff and no accepted delivery; API and Schedule UI explicitly reported waiting for eligible subscribers rather than admission unavailability", "Restoring one target consumed the original due-time input and advanced to one future cursor; restoring the other after pause did not add a recipient to the already partially accepted Independent cohort");
}

export async function verifyBpmnInputRefusals(j: TriggerJourney, authored: Authored, validVersionId: string, validState: WorkflowState) {
  const slotPath = `/runtime/workflows/activation-slots/${authored.definition.id}/default`;
  const validVersionPath = `/design/workflows/versions/${validVersionId}`;
  const originalSlot = await j.api(slotPath), originalVersion = await j.api(validVersionPath);
  const cases = [
    { name: "undeclared destination", change: (inputs: Record<string, unknown>) => { inputs.undeclaredWorkerInput = { kind: "Literal", value: "not declared" }; } },
    { name: "incompatible declared process input type", change: (inputs: Record<string, unknown>) => { inputs.count = { kind: "Input", name: "processMessage" }; } }
  ];
  try {
    for (const scenario of cases) {
      const invalid = structuredClone(validState);
      const service = (invalid.rootActivity.structure!.payload.elements as { elementId: string; properties: Record<string, string> }[]).find(item => item.elementId === "service")!;
      const binding = JSON.parse(service.properties["elsa.direct.binding"]) as { inputs: Record<string, unknown> }; scenario.change(binding.inputs);
      service.properties["elsa.direct.binding"] = JSON.stringify(binding);
      await j.api(`/design/workflows/drafts/${authored.draftId}`, "PUT", { state: invalid, layout: [] });
      const version = await j.api<{ id: string }>(`/design/workflows/drafts/${authored.draftId}/promote`, "POST", { operationKey: randomUUID() });
      const path = `/interchange/bpmn/versions/${version.id}/publish-direct`;
      const request = { requestId: randomUUID() }; const response = await j.page.request.post(`${j.base}${path}`, { data: request });
      const problem = await response.json(); expect(response.status(), JSON.stringify(problem)).toBe(422); expect(JSON.stringify(problem)).toMatch(/input|destination/i);
      j.requests.push({ kind: "actual-crafted-authored-bpmn-publication-refusal", scenario: scenario.name, method: "POST", path, request, status: response.status(), result: problem, editorCanAuthorThisInvalidDestinationOrType: false });
      expect(await j.api(slotPath)).toEqual(originalSlot); expect(await j.api(validVersionPath)).toEqual(originalVersion);
      expect((await j.api<{ state: WorkflowState }>(`/design/workflows/drafts/${authored.draftId}`)).state).toEqual(invalid);
    }
  } finally {
    await j.api(`/design/workflows/drafts/${authored.draftId}`, "PUT", { state: validState, layout: [] });
  }
  expect((await j.api<{ state: WorkflowState }>(`/design/workflows/drafts/${authored.draftId}`)).state).toEqual(validState);
  j.observed.push("Separate crafted authored BPMN versions with an undeclared worker destination and an incompatible declared process input type were rejected by actual publish-direct with 422, preserving the prior serving slot and immutable valid graph; the editable draft was restored", "These publication-boundary refusals supplement the inspected UI choice restrictions and required-mapping feedback");
}
