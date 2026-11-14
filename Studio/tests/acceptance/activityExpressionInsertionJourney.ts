import { expect, type Page, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, wireIdentity, type Binding, type Capture, type Created, type State } from "./activityDataJourneySupport";

type Parameter = { kind: string; [key: string]: unknown };
type Definition = { language: string; source: string; resultType: { alias: string; collectionKind?: string }; parameters: Record<string, Parameter>;
  options: unknown; capabilityProfile: string; metadata?: unknown; [field: string]: unknown };
type Context = { request: { definitionId: string; nodeId: string; inputKey: string; documentRevision: string; expectedSnapshotHash?: string; snapshot: State;
  query?: { expressionType?: string; parameterSource?: Parameter; memberPath?: string[] } };
  response: { state: string; documentRevision: string; snapshotHash: string; analysis?: { expressionParametersEnabled?: boolean; expressionType?: string; memberReadsEnabled?: boolean;
    sources: { parameterSource?: Parameter; parameterPath?: string[] }[] } } };

/** Actual installed adapters → portable carrier → save/Code Apply → publish → committed consumer input. */
export async function activityExpressionInsertionJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void, syntax: "JavaScript" | "Liquid") {
  const s = await activityDataSupport(page, info, registerCleanup);
  const composition = process.env.UX_ACTIVITY_DATA_COMPOSITION ?? "normal";
  expect(["normal", "default-off"]).toContain(composition);
  if (composition === "default-off") expect(syntax).toBe("JavaScript");
  const contexts: Context[] = [], pending: Promise<void>[] = [], insertions: unknown[] = [];
  page.on("response", response => {
    if (response.ok() && response.url().includes("/design/workflows/activity-data/context"))
      pending.push((async () => { contexts.push({ request: response.request().postDataJSON(), response: await response.json() }); })());
  });
  registerCleanup(async () => {
    await Promise.all(pending);
    await attachActivityDataJson(info, "expression-data-proof.json", { sourceSha: process.env.UX_SOURCE_SHA, syntax, composition, contexts, insertions });
  });
  const producerVersion = await s.version("Elsa.Workbench.ActivityDataSelectionProof.GetCustomer");
  const declared = { referenceKey: "suffix-key", name: "suffix", displayName: "Suffix", category: null,
    type: { alias: "String", collectionKind: "Single" }, storageDriverType: null, isNullable: false, isRequired: true };
  const variable = { referenceKey: "escaped-payload", name: "Escaped payload", type: { alias: "Object", collectionKind: "Single" },
    storageDriverType: "elsa.json", default: { expressionType: "Object", value: JSON.stringify({ "a.b": { "": "escaped-id" } }) } };
  const consumer = s.line(); consumer.inputs = [{ referenceKey: "text", value: { expressionType: syntax, value: "" } }];
  const created = await s.seed(`expression ${syntax}`, s.root([{ nodeId: "producer", activityVersionId: producerVersion, inputs: [], outputs: [] }, consumer]), [declared], [variable]);
  const button = () => page.getByRole("button", { name: "Insert data", exact: true });
  const chooser = () => page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const content = () => page.locator(".cm-content");
  const value = async () => (await s.stored(created))?.value as Definition;
  const source = async () => { const stored = (await s.stored(created))?.value; return typeof stored === "string" ? stored : (stored as Definition).source; };
  async function settled(expected: string) {
    await expect.poll(source).toBe(expected);
    await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  }
  async function openChooser() {
    await button().press("Enter");
    await expect(chooser().getByRole("option").first()).toBeFocused();
  }
  async function insert(alias: string, path: string[], action: () => Promise<void>, expectedSource: string) {
    const before = await s.stored(created), start = contexts.length;
    await openChooser(); await action();
    await expect(chooser()).toHaveCount(0); await expect(content()).toBeFocused(); await settled(expectedSource); await Promise.all(pending);
    const after = await s.stored(created), definition = after!.value as Definition, bound = definition.parameters[alias];
    const admission = contexts.slice(start).find(item => item.request.definitionId === created.definition.id && item.request.nodeId === "consumer" && item.request.inputKey === "text" &&
      item.request.query?.expressionType === syntax && wireIdentity(item.request.query.memberPath ?? []) === wireIdentity(path) &&
      wireIdentity(item.request.query.parameterSource) === wireIdentity(bound) && ["supported", "empty"].includes(item.response.state) &&
      item.request.documentRevision === item.response.documentRevision && item.request.expectedSnapshotHash === item.response.snapshotHash &&
      item.response.analysis?.expressionParametersEnabled === true && item.response.analysis.expressionType === syntax &&
      item.response.analysis.sources.some(offer => wireIdentity(offer.parameterSource) === wireIdentity(bound) && wireIdentity(offer.parameterPath) === wireIdentity(path)));
    expect(admission, "Fresh exact canonical parameter/path offer for the pre-insertion snapshot").toBeTruthy();
    const beforeInput = admission!.request.snapshot.rootActivity.structure!.payload.activities.find(node => node.nodeId === "consumer")!.inputs.find(input => input.referenceKey === "text")!.value;
    expect(beforeInput).toEqual(before);
    expect(definition).toMatchObject({ language: syntax, resultType: { alias: "String" }, capabilityProfile: "binding-pure-v1" });
    expect(admission!.response.analysis!.memberReadsEnabled).toBe(composition !== "default-off");
    insertions.push({ definitionId: created.definition.id, alias, path, parameter: bound, before, after, admission });
  }
  await s.open(created); await expect(button()).toBeEnabled();
  const before = await s.stored(created), writes = s.writes();
  await openChooser(); await capture(page, info, "expression-sources"); await page.keyboard.press("Escape");
  await expect(chooser()).toHaveCount(0); await expect(button()).toBeFocused();
  expect(await s.stored(created)).toEqual(before); expect(s.writes()).toBe(writes);
  await capture(page, info, "expression-cancelled");
  let text = syntax === "JavaScript" ? 'args.data["customerId"]' : '{{ data["customerId"] }}';
  await insert("data", ["customerId"], async () => {
    await chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: /› Customer$/ }) }).press("Enter");
    await chooser().getByRole("option", { name: /^customerId/ }).press("Enter");
  }, text);
  expect((await value()).parameters.data).toMatchObject({ kind: "ActivityResult", producerNodeId: "producer", projectionKey: "Customer" });
  const selected = await s.stored(created);
  await content().press("ControlOrMeta+z"); await expect.poll(() => s.stored(created)).toEqual(before);
  const mac = await page.evaluate(() => /Mac/.test(navigator.platform));
  await content().press(mac ? "Meta+Shift+z" : "Control+y"); await expect.poll(() => s.stored(created)).toEqual(selected);
  await s.history(created, before, selected); await capture(page, info, "expression-history");
  // Expanding really unmounts the compact editor. Its parked session owns the cursor/history continuity.
  await page.getByRole("button", { name: "Open expanded Text editor", exact: true }).click();
  const expanded = page.getByRole("dialog", { name: "Text", exact: true });
  await expect(expanded.locator(".cm-content")).toBeFocused(); await capture(page, info, "expression-expanded");
  if (syntax === "JavaScript") { await page.keyboard.insertText('.toUpperCase() + "-" + '); text += '.toUpperCase() + "-" + '; }
  else { await page.keyboard.insertText(" | upcase"); text = '{{ data["customerId"] | upcase }}'; await content().press("ControlOrMeta+End"); await page.keyboard.insertText("-"); text += "-"; }
  await settled(text);
  text += syntax === "JavaScript" ? "args.data2" : "{{ data2 }}";
  await insert("data2", [], async () => {
    await chooser().getByRole("group", { name: "Workflow inputs", exact: true }).getByRole("option", { name: /Suffix/ }).press("Enter");
  }, text);
  expect((await value()).parameters.data2).toEqual({ kind: "WorkflowRequest", memberKey: "suffix", path: null });
  await expanded.getByRole("button", { name: "Close Text editor", exact: true }).click();
  await expect(expanded).toHaveCount(0); await expect(page.getByRole("button", { name: "Open expanded Text editor", exact: true })).toBeFocused();
  const preview = page.locator(".studio-code-editor-preview"); if (await preview.isVisible()) await preview.click();
  await expect(content()).toBeFocused(); await content().press("ControlOrMeta+End");
  await page.keyboard.insertText(syntax === "JavaScript" ? ' + "-" + ' : "-"); text += syntax === "JavaScript" ? ' + "-" + ' : "-"; await settled(text);
  text += syntax === "JavaScript" ? 'args.data3["a.b"][""]' : '{{ data3["a.b"][""] }}';
  await insert("data3", ["a.b", ""], async () => {
    await chooser().getByRole("group", { name: "Variables", exact: true }).getByRole("option", { name: /Escaped payload/ }).press("Enter");
    const member = chooser().getByRole("textbox", { name: "Member name", exact: true });
    await expect(member).toBeEnabled(); await member.fill("a.b"); await member.press("Enter");
    await chooser().getByRole("button", { name: "Open empty member", exact: true }).press("Enter");
    await expect(chooser().getByRole("navigation", { name: "Member path", exact: true })).toContainText('""');
    await capture(page, info, "expression-escaped-member");
    await chooser().getByRole("button", { name: "Use this value", exact: true }).press("Enter");
  }, text);
  expect((await value()).parameters.data3).toEqual({ kind: "Variable", declaringScopeNodeId: "workflow", variableKey: "escaped-payload" });
  expect(Object.keys((await value()).parameters)).toEqual(["data", "data2", "data3"]);
  await capture(page, info, "expression-transformed");
  const authored = await s.stored(created); await s.codeApply(created); await expect.poll(() => s.stored(created)).toEqual(authored);
  await capture(page, info, "expression-reopened");
  await s.publishRun(created, "CUST-1042-tail-escaped-id", { suffix: "tail" }, true, "expression");
  await capture(page, info, "expression-completed");
  const viewport = page.viewportSize()!; await page.setViewportSize({ width: 320, height: 900 }); await s.open(created);
  await openChooser(); await capture(page, info, "expression-320"); await page.keyboard.press("Escape"); await expect(button()).toBeFocused();
  expect(await s.stored(created)).toEqual(authored); await page.setViewportSize(viewport);
}

/** Actual minimal backend AND editor absence. Authored carriers are inspected, never executed without their evaluator. */
export async function expressionModulePreservationJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  expect(process.env.UX_ACTIVITY_DATA_COMPOSITION).toBe("minimal");
  const s = await activityDataSupport(page, info, registerCleanup), preserved: unknown[] = [];
  registerCleanup(async () => attachActivityDataJson(info, "expression-preservation-proof.json", { sourceSha: process.env.UX_SOURCE_SHA, composition: "minimal", preserved }));
  for (const syntax of ["JavaScript", "Liquid"] as const) {
    const definition: Definition = { language: syntax, source: syntax === "JavaScript" ? 'args.manual + "-kept"' : '{{ manual | append: "-kept" }}',
      resultType: { alias: "String", collectionKind: "Single" }, parameters: { manual: { kind: "WorkflowRequest", memberKey: "suffix", path: null } },
      options: { preserve: true }, capabilityProfile: "binding-pure-v1", metadata: { original: "keep" }, futureCarrier: { opaque: ["keep", null, 7] } };
    const conversion = { mode: "profile", profile: { id: "partner.customer-json", version: "3" }, limits: { maxDepth: 8 }, options: { preserved: true } };
    const line = s.line(); line.inputs = [{ referenceKey: "text", value: { expressionType: syntax, value: definition }, conversion, autoEvaluate: false }];
    const created: Created = await s.seed(`missing ${syntax}`, s.root([line]), [{ referenceKey: "suffix-key", name: "suffix", type: { alias: "String", collectionKind: "Single" }, isNullable: false }]);
    const argument = async () => (await s.saved(created)).state.rootActivity.structure!.payload.activities.find(node => node.nodeId === "consumer")!.inputs.find(input => input.referenceKey === "text")!;
    const beforeArgument = await argument();
    expect(beforeArgument).toMatchObject({ referenceKey: "text", conversion, autoEvaluate: false, value: { expressionType: syntax, value: definition } });
    const before: Binding | null = await s.stored(created); expect(before).toEqual({ expressionType: syntax, value: definition });
    await s.open(created); const writes = s.writes();
    await expect(page.getByRole("button", { name: "Insert data", exact: true })).toHaveCount(0);
    await expect(page.locator(".cm-content")).toHaveCount(0);
    // Both the backend descriptor and editor are absent. Do not fabricate an expanded editing capability.
    await expect(page.getByRole("button", { name: "Open expanded Text editor", exact: true })).toHaveCount(0);
    await expect(page.locator(".wf-expression-editor-hint")).toContainText(`No editor is available for ${syntax}`);
    await capture(page, info, `missing-${syntax.toLowerCase()}`);
    await page.getByRole("button", { name: /^Text expression syntax/ }).click();
    await expect(page.getByRole("listbox")).toBeVisible(); await page.keyboard.press("Escape");
    await expect(page.getByRole("listbox")).toHaveCount(0);
    expect(await s.stored(created)).toEqual(before); expect(s.writes()).toBe(writes);
    await s.codeApply(created); expect(await s.stored(created)).toEqual(before); expect(await argument()).toEqual(beforeArgument);
    await capture(page, info, `missing-${syntax.toLowerCase()}-reopened`);
    preserved.push({ syntax, definitionId: created.definition.id, before, after: await s.stored(created), beforeArgument, afterArgument: await argument(), noWritesOpeningOrCancelling: true });
  }
  // Measure the same real source capability/menu/descriptors/graph as the existing basic native mapping control.
  const producer = { nodeId: "producer", activityVersionId: await s.version("Elsa.Workbench.ActivityDataSelectionProof.GetCustomer"), inputs: [], outputs: [] };
  const native = await s.seed("missing modules native control", s.root([producer, s.line()])); await s.open(native); await s.trigger().click();
  await expect(s.chooser().getByRole("option").first()).toBeFocused(); await s.assertComposition("Activity output…");
  await capture(page, info, "missing-native-control");
  const viewport = page.viewportSize()!; await page.setViewportSize({ width: 320, height: 900 }); await s.open(native); await s.trigger().click();
  await expect(s.chooser().getByRole("option").first()).toBeFocused(); await capture(page, info, "missing-320");
  await page.keyboard.press("Escape"); await expect(s.trigger()).toBeFocused(); await page.setViewportSize(viewport);
}
