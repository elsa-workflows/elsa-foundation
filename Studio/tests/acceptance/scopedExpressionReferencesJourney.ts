import { expect, type Page, type TestInfo } from "@playwright/test";
import { activityDataSupport, type Capture } from "./activityDataJourneySupport";

export async function scopedExpressionReferencesJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const double = { alias: "Double", collectionKind: "Single" };
  const root = s.root([]);
  const sequence = await s.api<{ contract: { outcomes: { name: string; referenceKey: string }[] } }>(`/design/activities/versions/${root.activityVersionId}`);
  const done = sequence.contract.outcomes.find(outcome => outcome.name === "Done")!.referenceKey;
  const legacy = "args.Numbers.reduce((total, number) => total + number, 0)";
  const stable = "args.numbers.reduce((total, number) => total + number, 0)";
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
      payload: { rootActivity: root, variables: [], outputMappings: [{ outputReferenceKey: "sum", source: { syntax: "JavaScript", value: legacy } }],
        outcomeMappings: [{ sourceOutcomeReferenceKey: done, boundaryOutcomeReferenceKey: "done" }] }
    },
    layout: [], activityPresentation: []
  });
  const draft = `/design/activities/drafts/${created.draft.draftId}`;
  const read = () => s.api<{ revision: number; contract: { inputs: { name: string; referenceKey: string }[] }; provider: { payload: { outputMappings: { source: { value: string | { source: string; inputBinding: "referenceKey" } } }[] } } }>(draft);
  const savedValue = async () => (await read()).provider.payload.outputMappings[0].source.value;
  const savedSource = async () => {
    const value = await savedValue();
    return typeof value === "string" ? value : value.source;
  };
  const route = `/workflows/activity-definitions?backend=legacy&definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}`;
  const outputs = page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: /^Outputs/ });
  const source = page.getByRole("region", { name: "Value source for Sum" });
  const expression = source.getByRole("textbox", { name: "JavaScript expression" });
  await page.goto(route);
  await outputs.click();
  await expect.poll(savedValue).toBe(legacy);
  const switchKeys = source.getByRole("button", { name: "Replace with stable-key expression", exact: true });
  await switchKeys.scrollIntoViewIfNeeded();
  await switchKeys.press("Enter");
  const replacement = source.getByRole("textbox", { name: "Expression using stable input keys", exact: true });
  const currentExpression = source.getByRole("textbox", { name: "Current expression using input names", exact: true });
  await expect(replacement).toBeFocused();
  await expect(currentExpression).toHaveValue(legacy);
  await expect(currentExpression).not.toBeEditable();
  await expect(source.getByRole("button", { name: "Insert data", exact: true })).toHaveCount(0);
  await expect(source.getByRole("button", { name: "Apply replacement", exact: true })).toBeDisabled();
  await replacement.fill(stable);
  await capture(page, info, "sum-binding-mode");
  await source.getByRole("button", { name: "Cancel", exact: true }).press("Enter");
  await expect(switchKeys).toBeFocused();
  await expect(source.getByRole("button", { name: "Insert data", exact: true })).toBeEnabled();
  await expect.poll(savedValue).toBe(legacy);
  await switchKeys.press("Enter");
  await expect(replacement).toHaveValue("");
  await replacement.fill(stable);
  await source.getByRole("button", { name: "Apply replacement", exact: true }).press("Enter");
  await expect(source.locator("[data-output-source]")).toBeFocused();
  await expect.poll(savedValue).toEqual({ source: stable, inputBinding: "referenceKey" });
  await source.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  await expression.fill(legacy);
  await expect.poll(savedValue).toEqual({ source: legacy, inputBinding: "referenceKey" });
  const fixOutput = source.getByRole("button", { name: "Use args.numbers", exact: true });
  await expect(fixOutput).toBeVisible();
  await fixOutput.scrollIntoViewIfNeeded();
  await expect.poll(() => source.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  await capture(page, info, "sum-fix");
  await fixOutput.press("Enter");
  await expect(expression).toBeFocused();
  await expect.poll(savedValue).toEqual({ source: stable, inputBinding: "referenceKey" });
  await expression.press("ControlOrMeta+z");
  await expect.poll(savedValue).toEqual({ source: legacy, inputBinding: "referenceKey" });
  await expression.press("ControlOrMeta+Shift+z");
  await expect.poll(savedValue).toEqual({ source: stable, inputBinding: "referenceKey" });

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
  await expect.poll(savedSource).toBe("args.numbers");
  await expression.fill(stable);
  await expect.poll(savedSource).toBe(stable);

  await page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: /^Inputs/ }).click();
  const inputName = page.getByRole("textbox", { name: "Name", exact: true });
  await inputName.fill("Values");
  await inputName.press("Enter");
  await expect.poll(async () => (await read()).contract.inputs[0]).toMatchObject({ name: "Values", referenceKey: "numbers" });

  await page.reload();
  await outputs.click();
  await expect(source.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true })).toContainText(stable);
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
  await fixText.scrollIntoViewIfNeeded();
  await capture(page, info, "write-line-fix");
  await fixText.press("Enter");
  await expect.poll(() => s.stored(caller)).toEqual({ expressionType: "JavaScript", value: rightText });

  const text = page.getByRole("textbox", { name: "JavaScript expression", exact: true });
  await expect(text).toBeFocused();
  await s.publishRun(caller, "Sum: 10", {}, true, "expression");
  await capture(page, info, "calculator-direct-repair");
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  await text.fill("0");
  await text.press("ControlOrMeta+a");
  await page.getByRole("button", { name: "Insert data", exact: true }).press("Enter");
  const data = page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const boundOutput = data.getByRole("group", { name: "Activity outputs", exact: true }).getByRole("option").filter({
    has: page.locator(".wf-data-chooser-option-label", { hasText: /› Sum$/ })
  });
  await expect(boundOutput).toBeVisible();
  await capture(page, info, "write-line-picker");
  await boundOutput.press("Enter");
  const useWhole = data.getByRole("button", { name: "Use whole value", exact: true });
  // Scalar sources can commit immediately. Other shapes ask the author to use the whole value.
  await expect.poll(async () => !await data.isVisible() || (await useWhole.isVisible() && await useWhole.isEnabled())).toBe(true);
  if (await data.isVisible()) await useWhole.press("Enter");
  await expect.poll(() => s.stored(caller)).toMatchObject({
    expressionType: "JavaScript",
    value: { source: "args.data", parameters: { data: { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "sum" } } }
  });
  const beforeFix = (await s.stored(caller))!.value as Record<string, unknown>;
  await page.reload();
  await s.open(caller);
  const warning = page.getByText("This expression returns Double. This field expects String.", { exact: true });
  const fix = page.getByRole("button", { name: "Wrap in String(…)", exact: true });
  await expect(warning).toBeVisible();
  await fix.scrollIntoViewIfNeeded();
  await capture(page, info, "write-line-result-warning");
  await fix.press("Enter");
  await expect(text).toBeFocused();
  const fixed = { expressionType: "JavaScript", value: { ...beforeFix, source: "String(args.data)" } };
  await expect.poll(() => s.stored(caller)).toEqual(fixed);
  await expect(warning).toHaveCount(0);
  await text.press("ControlOrMeta+z");
  await expect.poll(() => s.stored(caller)).toEqual({ expressionType: "JavaScript", value: beforeFix });
  const mac = await page.evaluate(() => /Mac/.test(navigator.platform));
  await text.press(mac ? "Meta+Shift+z" : "Control+y");
  await expect.poll(() => s.stored(caller)).toEqual(fixed);
  await page.reload();
  await s.open(caller);
  await expect(page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true })).toContainText("String(args.data)");
  await expect(fix).toHaveCount(0);
  await capture(page, info, "write-line-result-fixed");
  await s.publishRun(caller, "10", {}, true, "expression");
  await capture(page, info, "calculator-completed");
}
