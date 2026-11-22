import { expect, type Page, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, type Capture } from "./activityDataJourneySupport";

export async function numericBindingJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const type = { alias: "Double", collectionKind: "Single" };
  const member = (name: string) => ({
    referenceKey: name.toLowerCase(), name, type, storageDriverKey: "elsa.json", isRequired: true, isNullable: false
  });
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });

  // A public graph output uses the normal activity publication flow and an exact version in the caller.
  const activity = await s.api<{ draft: { draftId: string; revision: number } }>("/design/activities/definitions", "POST", {
    displayName: `Calculate total ${info.project.name} ${Date.now()}`, category: "Qualification",
    contract: {
      contractSchemaVersion: "1", inputs: [member("A"), member("B")], outputs: [member("Sum")],
      outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }]
    },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", payload: {
      rootActivity: s.root([]), variables: [],
      outputMappings: [{ outputReferenceKey: "sum", source: { syntax: "JavaScript", value: "args.A + args.B" } }]
    } }, layout: []
  });
  const draftRoute = `/design/activities/drafts/${activity.draft.draftId}`;
  const review = await s.api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; diagnostics: unknown[] }>(
    `${draftRoute}/publication-preflight`, "POST", { expectedDraftRevision: activity.draft.revision, expectedDefinitionHeadVersionId: null });
  expect(review.isPublishable, JSON.stringify(review.diagnostics)).toBe(true);
  const sum = await s.api<{ outcome: { definitionVersionId: string } }>(`${draftRoute}/publish`, "POST", {
    expectedDraftRevision: activity.draft.revision, expectedDefinitionHeadVersionId: null,
    version: review.minimumVersion, reviewToken: review.reviewToken, idempotencyKey: crypto.randomUUID()
  });
  const binding = { expressionType: "ActivityResult", value: { producerNodeId: "producer", projectionKey: "sum", isOptional: false } };
  const callerOutput = {
    nodeId: "caller-output", activityVersionId: "elsa.intrinsic.set-output@1", intrinsic: { kind: "SetOutput", valueType: type },
    inputs: [literal("name", "CallerSum"), { referenceKey: "value", value: binding }], outputs: []
  };
  const caller = await s.seed("numeric binding", s.root([
    { nodeId: "producer", activityVersionId: sum.outcome.definitionVersionId, inputs: [literal("a", 10), literal("b", 9.25)], outputs: [] },
    s.line(), callerOutput
  ]), [], [], [{ referenceKey: "caller-sum", name: "CallerSum", type, isNullable: false }]);

  await s.open(caller);
  await s.trigger().press("Enter");
  await s.chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: /(?:^| › )Sum$/ }) }).press("Enter");
  await expect.poll(() => s.stored(caller)).toEqual(binding);
  const reference = page.locator(".wf-data-reference");
  const automatic = reference.getByText(/Double\s*→\s*String\s*·\s*automatic/i);
  await expect(automatic).toBeVisible();
  await capture(page, info, "automatic-conversion");

  const toggle = page.getByRole("button", { name: /^Text conversion:/ });
  const mode = page.getByRole("button", { name: "Text conversion mode", exact: true });
  async function chooseMode(name: "None" | "Auto") {
    if (!(await mode.isVisible())) await toggle.click();
    await mode.press("Enter");
    await page.getByRole("listbox", { name: "Text conversion mode", exact: true }).getByRole("option", { name, exact: true }).press("Enter");
  }
  async function storedMode() {
    const draft = await s.saved(caller);
    const consumer = draft.state.rootActivity.structure!.payload.activities.find(node => node.nodeId === "consumer")!;
    const text = consumer.inputs.find(input => input.referenceKey === "text");
    return (text as { conversion?: { mode: string } })?.conversion?.mode?.toLowerCase() ?? "auto";
  }
  await chooseMode("None");
  const reason = reference.getByText(/requires an exact source and target contract match/i);
  await expect(reason).toBeVisible();
  const validation = page.locator(".wf-validation");
  await expect(validation).toContainText(/exact source and target contract match/i);
  await reason.scrollIntoViewIfNeeded();
  await capture(page, info, "incompatible-conversion");
  await expect.poll(storedMode).toBe("none");
  const draft = await s.saved(caller);
  const rejected = await page.request.post(`${process.env.ELSA_WORKBENCH_URL}/publishing/workflows/preflight`, {
    data: { definitionId: caller.definition.id, state: draft.state, layout: draft.layout }
  });
  expect(rejected.status()).toBe(400);
  const failure = await rejected.text();
  expect(failure).toContain("VF-COER-001");
  expect(failure).toContain("None");

  await chooseMode("Auto");
  await expect(reason).toHaveCount(0);
  await expect(automatic).toBeVisible();
  await expect.poll(storedMode).toBe("auto");
  await s.open(caller);
  await expect(automatic).toBeVisible();
  await expect(validation).toContainText("No validation errors");
  await capture(page, info, "recovered-conversion");
  const { instance, consumerInput, publication, run } = await s.publishRun(caller, "19.25");
  const completed = await s.api<{ outputs: { CallerSum: { value: { kind: string; value: unknown } } } }>(instance);
  expect(completed.outputs.CallerSum.value).toMatchObject({ kind: "number", value: 19.25 });
  expect(consumerInput).toMatchObject({ kind: "string", preview: "19.25" });
  await attachActivityDataJson(info, "numeric-binding-proof.json", {
    definitionId: caller.definition.id, activityVersionId: sum.outcome.definitionVersionId, binding, rejection: failure,
    publication, run, writeLineText: consumerInput, callerSum: completed.outputs.CallerSum
  });
}
