import { expect } from "@playwright/test";
import { TriggerJourney, configured } from "./standaloneTriggersSupport";
import type { DeliveryView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

type Capabilities = { capabilities: { id: string; links: { rel: string }[] }[] };

export async function verifyHostComposition(j: TriggerJourney) {
  const target = await j.echo("Published before removing Design composition");
  const resource = await j.create("http", "Runtime-only shared endpoint");
  await j.add(resource.id, target.authored.definition.id, [configured("message", "Runtime consumes retained publication without Design"), configured("count", 7)]);
  await j.enable(resource.id);
  const source = await j.read(resource.id);
  const authoringJourney = new TriggerJourney(j.page, j.info);
  try {
    const combined = await j.host("combined-probe");
    expect(combined.setup!.composition).toMatchObject({ shape: "combined", runtimeProcessor: true, ingressFactory: true, deliveryStore: true, designStore: true });
    await j.page.reload(); await expect(j.page.getByRole("heading", { name: resource.name, exact: true })).toBeVisible();
    await j.capture("trigger-combined-composition");

    const authoring = await j.host("design-only");
    expect(authoring.setup!.composition).toMatchObject({ shape: "design-only", runtimeProcessor: false, ingressFactory: false, deliveryStore: false, designStore: true });
    const designDetails = await j.api<{ definition: { id: string } }>(`/design/workflows/definitions/${target.authored.definition.id}`);
    expect(designDetails.definition.id).toBe(target.authored.definition.id);
    const designCapabilities = await j.api<Capabilities>("/capabilities");
    const triggerCapability = designCapabilities.capabilities.find(item => item.id === "elsa.api.standalone-triggers");
    expect(triggerCapability).toBeTruthy(); expect(triggerCapability!.links.some(item => item.rel === "deliveries")).toBe(false);
    const absentIntake = await j.page.request.post(`${j.base}/workflows/http/${String(source.configuration.path).replace(/^\/+/, "")}`, { data: { actual: "no Runtime listener" } });
    expect(absentIntake.status()).toBe(404);
    const paused = await authoringJourney.create("event", "Authoring-only paused metadata");
    await j.page.getByRole("button", { name: "Add workflow", exact: true }).click(); await j.chooseTarget(target.authored.definition.id);
    await expect(j.page.getByRole("checkbox", { name: "Enable this subscription", exact: true })).not.toBeChecked();
    await j.page.getByRole("button", { name: "Review subscription", exact: true }).click(); await j.review();
    const disabled = (await authoringJourney.subscriptions(paused.id))[0];
    expect(disabled.enabled).toBe(false); expect(disabled.serving).toBeFalsy(); expect(disabled.target.definitionId).toBe(target.authored.definition.id);
    await j.page.reload(); await expect(j.page.getByRole("button", { name: "Enable trigger", exact: true })).toBeDisabled();
    const disabledRow = j.page.getByRole("row").filter({ hasText: target.authored.definition.id });
    await expect(disabledRow).toContainText("Paused");
    await expect(disabledRow).toContainText("Not serving");
    await j.capture("trigger-design-only-composition");
    const forceEnable = await j.page.request.post(`${j.base}/runtime/standalone-triggers/${paused.id}/changes/preview`,
      { data: { expectedRevision: (await authoringJourney.read(paused.id)).revision, command: { kind: "EnableTrigger" } } });
    // Review may return an explicit unavailable finding rather than an HTTP refusal; neither permits apply.
    const refused = await forceEnable.json() as { canApply?: boolean; code?: string };
    expect(forceEnable.ok() ? refused.canApply === false : [400, 409, 503].includes(forceEnable.status())).toBe(true);
    expect((await authoringJourney.read(paused.id)).desiredState).toBe("Paused");
    await j.page.getByRole("button", { name: "Delete trigger", exact: true }).click(); await j.review();

    const runtime = await j.host("runtime-only");
    expect(runtime.setup!.composition).toMatchObject({ shape: "runtime-only", runtimeProcessor: true, ingressFactory: true, deliveryStore: true, designStore: false });
    const runtimeCapabilities = await j.api<Capabilities>("/capabilities");
    expect(runtimeCapabilities.capabilities.some(item => item.id === "elsa.api.workflow-design")).toBe(false);
    const absentDesign = await j.page.request.get(`${j.base}/design/workflows/definitions/${target.authored.definition.id}`);
    expect(absentDesign.status()).toBe(404);
    await j.open(resource.id);
    await j.page.getByRole("button", { name: "Add workflow", exact: true }).click(); await j.chooseTarget(target.authored.definition.id, true);
    await expect(j.page.getByRole("heading", { name: "Declared inputs", exact: true })).toBeVisible();
    await j.capture("trigger-runtime-only-composition"); await j.page.getByRole("button", { name: "Cancel", exact: true }).click();
    const accepted = await j.sourceRequest(await j.read(resource.id), { actual: "Runtime only" }, 202);
    const delivery = await j.started(await j.api<DeliveryView>(`/runtime/standalone-trigger-deliveries/${accepted.body.deliveryId}`));
    expect(delivery.acceptedCount).toBe(1);
    const execution = delivery.recipients[0].executionReference!;
    await j.outputs(execution.id, { message: "Runtime consumes retained publication without Design", count: 7 });
    j.requests.push({ kind: "actual-host-composition-matrix", combined: combined.setup!.composition, designOnly: authoring.setup!.composition,
      runtimeOnly: runtime.setup!.composition, designOnlyIntakeStatus: absentIntake.status(), forcedEnableStatus: forceEnable.status(), forcedEnableResult: refused,
      designOnlyStore: "explicit process-local Core replacement; no durability or serving-input claim", runtimeOnlyDesignStatus: absentDesign.status(),
      runtimeOnlyAdmissionStatus: accepted.response.status(), completedExecutionId: execution.id });
    j.observed.push("Normal owned CShell feature composition produced actual combined, Design-only and Runtime-only hosts. A source-bound service inventory asserts processor/ingress/delivery absence in Design-only and Design-store absence in Runtime-only. Design-only used explicit process-local paused metadata replacements and no invented serving contract; force-enable refused and source HTTP404. Runtime-only browsed actual retained Runtime slots, accepted real HTTP202 and completed its previously published business workflow without Design services.");
  } finally {
    j.requests.push(...authoringJourney.requests);
    await j.host("restore-features"); await j.page.reload();
  }
}
