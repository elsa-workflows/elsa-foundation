import { expect, type Page, type TestInfo } from "@playwright/test";
import { activityDataSupport, type Binding, type Capture, type Created } from "./activityDataJourneySupport";

/** Literal keys and explicit scope remain identities, with server offers as the sole selection authority. */
export async function activityDataSourcesJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const variable = (referenceKey: string, sentinel: string) => ({ referenceKey, name: "Payload", type: { alias: "Object", collectionKind: "Single" },
    storageDriverType: "elsa.json", default: { expressionType: "Object", value: JSON.stringify({ "a.b": { "": sentinel } }) } });
  const input = { referenceKey: "request-payload", name: "payload", displayName: "Request payload", category: null,
    type: { alias: "Object", collectionKind: "Single" }, storageDriverType: null, isNullable: false, isRequired: false };
  const created = await s.seed("scoped and declared", s.root([s.line()], "sequence", [variable("inner-payload", "inner-id")]), [input], [variable("outer-payload", "outer-id")]);
  const inner: Binding = { expressionType: "VariableMember", value: { referenceKey: "inner-payload", declaringScopeId: "root", memberPath: ["a.b", ""] } };
  const outer: Binding = { expressionType: "VariableMember", value: { referenceKey: "outer-payload", declaringScopeId: "workflow", memberPath: ["a.b", ""] } };
  const declared: Binding = { expressionType: "WorkflowRequestMember", value: { memberKey: "payload", memberPath: ["a.b", ""] } };
  async function literal(captures = false) {
    await expect(s.memberName()).toBeEnabled(); await s.memberName().fill("a.b");
    await expect(s.chooser()).toContainText("case-sensitive");
    if (captures) await capture(page, info, "variable-literal");
    await s.memberName().press("Enter"); await expect(s.path()).toContainText('"a.b"');
    await expect(s.chooser().getByRole("button", { name: "Open empty member", exact: true })).toBeEnabled();
    if (captures) await capture(page, info, "variable-empty");
    await s.chooser().getByRole("button", { name: "Open empty member", exact: true }).press("Enter");
    await expect(s.path()).toContainText('""');
    await expect(s.chooser().getByRole("button", { name: "Use this value", exact: true })).toBeEnabled();
  }
  async function chooseCurrent(created: Created, binding: Binding) {
    await s.commit(created, binding, () => s.chooser().getByRole("button", { name: "Use this value", exact: true }).press("Enter"));
    await expect(page.locator(".wf-data-reference")).toContainText("checked when the workflow runs");
  }
  await s.open(created); await s.trigger().press("Enter");
  const variables = () => s.chooser().getByRole("group", { name: "Variables", exact: true });
  await expect(variables().getByRole("option")).toHaveCount(2);
  const labels = await variables().getByRole("option").allTextContents();
  expect(labels[0]).toContain("Sequence"); expect(labels[1]).toContain("Workflow"); expect(labels[1]).toContain("outer scope");
  await expect(s.chooser().getByRole("group", { name: "Workflow inputs", exact: true })).toBeVisible();
  await expect(s.chooser().getByRole("textbox", { name: "Request member", exact: true })).toHaveCount(0);
  await capture(page, info, "source-groups");
  const before = await s.saved(created), writesBefore = s.writes();
  await variables().getByRole("option").filter({ hasText: "Sequence" }).press("Enter"); await literal(true);
  await s.chooser().getByRole("button", { name: "Back", exact: true }).click();
  await expect(s.path()).not.toContainText('""');
  await s.path().getByRole("button", { name: "Whole value", exact: true }).click();
  // The old path can still expose an enabled input for one render before its replacement request starts.
  // Wait for the requested root breadcrumb/packet, then send Escape from the actual focused enabled input.
  await expect(s.path().getByRole("button")).toHaveCount(1);
  await expect(s.chooser().locator(".wf-data-members")).not.toHaveAttribute("aria-busy", "true");
  await expect(s.memberName()).toBeEnabled(); await s.memberName().focus(); await expect(s.memberName()).toBeFocused();
  await page.keyboard.press("Escape"); await expect(s.chooser()).toHaveCount(0);
  await expect(s.trigger()).toBeFocused(); expect(await s.saved(created)).toEqual(before); expect(s.writes()).toBe(writesBefore);
  s.observations.push("literal dot/empty keys; Back/breadcrumb/Escape focus; no hidden mutation or save on navigation/cancel");

  await s.trigger().click(); await variables().getByRole("option").filter({ hasText: "Sequence" }).press("Enter"); await literal(); await chooseCurrent(created, inner);
  await s.history(created, null, inner); await s.codeApply(created);
  await expect(s.reference()).toContainText("Sequence"); await expect(s.reference()).toContainText('"a.b"'); await expect(s.reference()).toContainText('""');
  await capture(page, info, "variable-reference"); await s.assertComposition("Variable member"); await s.publishRun(created, "inner-id");
  await s.replace().click(); await variables().getByRole("option").filter({ hasText: "Workflow" }).press("Enter"); await literal(); await chooseCurrent(created, outer);
  await s.open(created); await expect(s.reference()).toContainText("Workflow"); await capture(page, info, "outer-variable-reference");
  await s.publishRun(created, "outer-id");
  await s.replace().click(); await s.chooser().getByRole("group", { name: "Workflow inputs", exact: true }).getByRole("option", { name: /Request payload/ }).press("Enter");
  await literal(); await chooseCurrent(created, declared); await s.history(created, outer, declared); await s.codeApply(created);
  await expect(s.reference()).toContainText("Request payload"); await capture(page, info, "declared-reference");
  await s.publishRun(created, "declared-ui-id", { payload: { "a.b": { "": "declared-ui-id" } } });

  // Unsupported saved values remain byte-for-byte authored objects until explicit repair, including future fields.
  const unsupported = await s.seed("unsupported request", s.root([s.line()]), [input]);
  const legacy: Binding = { expressionType: "WorkflowRequestMember", value: { memberKey: "OldPayload", memberPath: ["a.b", ""], futureField: { preserved: true } } };
  const draft = await s.saved(unsupported); draft.state.rootActivity.structure!.payload.activities[0].inputs = [{ referenceKey: "text", value: legacy }];
  await s.api(`/design/workflows/drafts/${unsupported.draftId}`, "PUT", draft); await s.open(unsupported);
  await expect(page.locator(".wf-data-reference-problem")).toContainText("Declare it");
  await expect(s.reference()).toContainText("OldPayload"); expect(await s.stored(unsupported)).toEqual(legacy);
  await capture(page, info, "unsupported-request");
  const unchanged = s.writes(); await s.replace().press("Enter");
  await expect(s.chooser().getByRole("group", { name: "Workflow inputs", exact: true })).toBeVisible();
  await expect(s.chooser().getByRole("option").first()).toBeFocused();
  await page.keyboard.press("Escape"); await expect(s.chooser()).toHaveCount(0); await expect(s.replace()).toBeFocused(); expect(await s.stored(unsupported)).toEqual(legacy); expect(s.writes()).toBe(unchanged);
  await page.getByRole("button", { name: "Clear data for Text", exact: true }).click();
  await expect.poll(() => s.stored(unsupported)).toBeNull(); await s.history(unsupported, legacy, null);
  await page.getByRole("button", { name: "Undo", exact: true }).click(); await expect.poll(() => s.stored(unsupported)).toEqual(legacy); await s.select();
  await s.replace().click(); await s.chooser().getByRole("group", { name: "Workflow inputs", exact: true }).getByRole("option", { name: /Request payload/ }).press("Enter");
  await literal(); await chooseCurrent(unsupported, declared); await s.history(unsupported, legacy, declared); await s.codeApply(unsupported);
  await expect(page.locator(".wf-data-reference-problem")).toHaveCount(0); await capture(page, info, "request-repaired");
  s.observations.push("unknown input preserved with actionable declaration problem; explicit Clear/history and Replace/history/Code Apply repair");
  const viewport = page.viewportSize()!;
  await page.setViewportSize({ width: 320, height: 900 }); await s.open(created); await s.replace().click();
  await variables().getByRole("option").filter({ hasText: "Workflow" }).press("Enter"); await expect(s.memberName()).toBeEnabled(); await s.memberName().fill("a.b");
  await capture(page, info, "source-320"); await s.memberName().press("Escape"); await expect(s.replace()).toBeFocused();
  expect(await s.stored(created)).toEqual(declared); await page.setViewportSize(viewport);
  const empty = await s.seed("no inputs", s.root([s.line()])); await s.open(empty); await s.trigger().click();
  await expect(s.chooser().locator(".wf-data-members")).toContainText("No earlier data is available");
  await expect(s.chooser().getByRole("textbox", { name: "Request member", exact: true })).toHaveCount(0);
  await expect(s.chooser().getByRole("button", { name: "Open request member", exact: true })).toHaveCount(0);
  await expect(s.chooser()).toBeFocused();
  await capture(page, info, "no-request-proposal"); await page.keyboard.press("Escape"); await expect(s.trigger()).toBeFocused(); expect(await s.stored(empty)).toBeNull();
  expect(s.contexts.flatMap(item => item.response.analysis?.sources ?? []).some(source => source.shape?.state === "unknown"), "Real deferred shape facts, not runtime-key inference").toBe(true);
}
