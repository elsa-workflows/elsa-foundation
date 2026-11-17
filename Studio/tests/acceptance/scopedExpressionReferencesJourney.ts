import { expect, type Page, type TestInfo } from "@playwright/test";
import { activityDataSupport, type Capture } from "./activityDataJourneySupport";

export async function scopedExpressionReferencesJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const double = { alias: "Double", collectionKind: "Single" };
  const root = s.root([]);
  const sequence = await s.api<{ contract: { outcomes: { name: string; referenceKey: string }[] } }>(`/design/activities/versions/${root.activityVersionId}`);
  const done = sequence.contract.outcomes.find(outcome => outcome.name === "Done")!.referenceKey;
  const wrong = "args.numbers.reduce((total, number) => total + number, 0)";
  const right = "args.Numbers.reduce((total, number) => total + number, 0)";
  const created = await s.api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
    displayName: `Sum ${info.project.name} ${Date.now()}`, category: "Qualification",
    contract: {
      contractSchemaVersion: "1",
      inputs: [{ referenceKey: "numbers", name: "Numbers", type: { ...double, collectionKind: "Array" }, storageDriverKey: "elsa.json", isRequired: true, isNullable: false }],
      outputs: [{ referenceKey: "sum", name: "Sum", type: double, storageDriverKey: "elsa.json", isRequired: true, isNullable: false }],
      outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }]
    },
    provider: {
      providerKey: "elsa.activity-graph", schemaVersion: "2",
      payload: { rootActivity: root, variables: [], outputMappings: [{ outputReferenceKey: "sum", source: { syntax: "JavaScript", value: wrong } }],
        outcomeMappings: [{ sourceOutcomeReferenceKey: done, boundaryOutcomeReferenceKey: "done" }] }
    },
    layout: [], activityPresentation: []
  });
  const draft = `/design/activities/drafts/${created.draft.draftId}`;
  const read = () => s.api<{ revision: number; provider: { payload: { outputMappings: { source: { value: string } }[] } } }>(draft);
  const savedSource = async () => (await read()).provider.payload.outputMappings[0].source.value;
  const route = `/workflows/activity-definitions?backend=legacy&definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}`;
  const outputs = page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: /^Outputs/ });
  const source = page.getByRole("region", { name: "Value source for Sum" });
  const expression = source.getByRole("textbox", { name: "JavaScript expression" });
  await page.goto(route);
  await outputs.click();
  const fixOutput = source.getByRole("button", { name: "Use args.Numbers" });
  await expect(fixOutput).toBeVisible();
  await capture(page, info, "sum-fix");
  await fixOutput.press("Enter");
  await expect(expression).toBeFocused();
  await expect.poll(savedSource).toBe(right);

  await expression.fill("0");
  await expression.press("ControlOrMeta+a");
  const insert = source.getByRole("button", { name: "Insert data", exact: true });
  await insert.press("Enter");
  const picker = page.getByRole("dialog", { name: "Insert data", exact: true });
  const numbers = picker.getByRole("button", { name: /Numbers.*Array/ });
  await expect(numbers).toBeFocused();
  await capture(page, info, "sum-picker");
  await page.keyboard.press("Escape");
  await expect(insert).toBeFocused();
  await expect.poll(savedSource).toBe("0");
  await insert.press("Enter");
  await numbers.press("Enter");
  await expect(expression).toBeFocused();
  await expect.poll(savedSource).toBe("args.Numbers");
  await expression.fill(right);
  await expect.poll(savedSource).toBe(right);

  await page.reload();
  await outputs.click();
  await expect(source.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true })).toContainText(right);
  const preflight = await s.api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; definitionHeadVersionId: string | null }>(
    `${draft}/publication-preflight`, "POST", { expectedDraftRevision: (await read()).revision, expectedDefinitionHeadVersionId: null });
  expect(preflight.isPublishable, JSON.stringify(preflight)).toBe(true);
  const publication = await s.api<{ outcome: { definitionVersionId: string } }>(`${draft}/publish`, "POST", {
    expectedDraftRevision: (await read()).revision, expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId,
    version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID()
  });

  const line = s.line();
  const wrongText = "`Sum: ${args.Variable1}`";
  const rightText = "`Sum: ${variables.Variable1}`";
  line.inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: wrongText } }];
  const caller = await s.seed("Calculator", s.root([{
    nodeId: "producer", activityVersionId: publication.outcome.definitionVersionId,
    inputs: [{ referenceKey: "numbers", value: { expressionType: "Literal", value: [1, 3, 4, 2] } }],
    outputs: [{ referenceKey: "sum", value: { expressionType: "Variable", value: { referenceKey: "variable-1", declaringScopeId: "workflow" } } }]
  }, line]), [], [{ referenceKey: "variable-1", name: "Variable1", type: double, storageDriverType: "elsa.json" }]);
  await s.open(caller);
  const fixText = page.getByRole("button", { name: "Use variables.Variable1" });
  await expect(fixText).toBeVisible();
  await capture(page, info, "write-line-fix");
  await fixText.press("Enter");
  await expect.poll(() => s.stored(caller)).toEqual({ expressionType: "JavaScript", value: rightText });

  const text = page.getByRole("textbox", { name: "JavaScript expression", exact: true });
  await expect(text).toBeFocused();
  await text.fill('"Sum: " + ');
  await text.press("ControlOrMeta+End");
  await page.getByRole("button", { name: "Insert data", exact: true }).press("Enter");
  const data = page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const variable = data.getByRole("group", { name: "Variables", exact: true }).getByRole("option", { name: /Variable1/ });
  await expect(variable).toBeVisible();
  await capture(page, info, "write-line-picker");
  await variable.press("Enter");
  const useWhole = data.getByRole("button", { name: "Use whole value", exact: true });
  // Scalar sources can commit immediately. Other shapes ask the author to use the whole value.
  await expect.poll(async () => !await data.isVisible() || (await useWhole.isVisible() && await useWhole.isEnabled())).toBe(true);
  if (await data.isVisible()) await useWhole.press("Enter");
  await expect.poll(() => s.stored(caller)).toMatchObject({
    expressionType: "JavaScript",
    value: { source: '"Sum: " + args.data', parameters: { data: { kind: "Variable", variableKey: "variable-1" } } }
  });
  await s.publishRun(caller, "Sum: 10", {}, true, "expression");
  await capture(page, info, "calculator-completed");
}
