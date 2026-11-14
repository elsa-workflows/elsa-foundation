import { expect, type Page, type Request, type TestInfo } from "@playwright/test";
import { activityDataSupport, attachActivityDataJson, type Binding, type Capture, type Node } from "./activityDataJourneySupport";

/** Typed Customer → String input with no variable, capture, expression, or optional expression module. */
export async function activityDataMembersJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const s = await activityDataSupport(page, info, registerCleanup);
  const producerVersion = await s.version("Elsa.Workbench.ActivityDataSelectionProof.GetCustomer");
  const catalog = await s.api<{ contract: { outputs: { referenceKey: string; type: { alias: string } }[] } }>(`/design/activities/versions/${producerVersion}`);
  expect(catalog.contract.outputs).toHaveLength(1);
  expect(catalog.contract.outputs[0]).toMatchObject({ referenceKey: "Customer", type: { alias: "Elsa.Workbench.ActivityDataSelectionProof.Customer" } });
  await attachActivityDataJson(info, "customer-catalog.json", catalog);
  const expected: Binding = { expressionType: "ActivityResultMember", value: { producerNodeId: "producer", projectionKey: "Customer", isOptional: false, memberPath: ["customerId"] } };
  const customer = () => s.chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: /› Customer$/ }) });
  const whole = () => s.chooser().getByRole("option").filter({ has: page.locator(".wf-data-chooser-option-label", { hasText: /› Whole output$/ }) });
  const member = () => s.chooser().getByRole("option", { name: /^customerId/ });
  for (const kind of ["sequence", "flowchart"]) {
    const producer: Node = { nodeId: "producer", activityVersionId: producerVersion, inputs: [], outputs: [] };
    const created = await s.seed(`Customer ${kind}`, s.root([producer, s.line()], kind));
    await s.open(created); await s.trigger().press("Enter");
    await expect(customer()).toHaveCount(1);
    if (kind === "sequence") {
      const label = await customer().locator(".wf-data-chooser-option-label").boundingBox();
      expect(label!.width, "Readable source label").toBeGreaterThan(90);
      await capture(page, info, "customer-sources");
    }
    await customer().press("Enter"); await expect(member()).toBeVisible();
    await expect(s.chooser()).toContainText("Input type: String");
    if (kind === "sequence") await capture(page, info, "customer-members");
    await s.commit(created, expected, () => member().press("Enter"));
    await expect(s.reference()).toContainText("Get Customer"); await expect(s.reference()).toContainText("customerId");
    await expect(s.replace()).toBeFocused();
    const draft = await s.saved(created);
    expect(draft.state.variables).toEqual([]);
    expect(draft.state.rootActivity.structure!.payload.activities[0].outputs).toEqual([]);
    if (kind === "sequence") {
      await capture(page, info, "customer-reference");
      await s.history(created, null, expected);
      await page.getByRole("button", { name: "Clear data for Text", exact: true }).press("Enter");
      await expect(s.trigger()).toBeFocused(); await expect.poll(() => s.stored(created)).toBeNull();
      await s.history(created, expected, null);
      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await expect.poll(() => s.stored(created)).toEqual(expected); await s.select();

      // Actual UI transport injection only: delay/abort requests, never replace server analysis or catalog responses.
      const pattern = "**/design/workflows/activity-data/context";
      let release: (() => void) | undefined;
      let held = false, heldRequest: Request | undefined, cancelledRequest: Request | undefined;
      const observeCancellation = (request: Request) => { if (request === heldRequest) cancelledRequest = request; };
      page.on("requestfailed", observeCancellation);
      try {
        await page.route(pattern, async route => {
          if (!held && !route.request().postDataJSON().query?.source) {
            held = true; heldRequest = route.request(); await new Promise<void>(resolve => { release = resolve; });
          }
          await route.continue();
        });
        const writesBefore = s.writes();
        await s.replace().click(); await expect.poll(() => !!release).toBe(true);
        await expect(s.chooser().getByLabel("Loading outputs", { exact: true })).toBeVisible();
        await expect(s.chooser()).toBeFocused();
        await capture(page, info, "customer-loading");
        await page.keyboard.press("Escape"); await expect(s.replace()).toBeFocused();
        release!(); await page.unrouteAll({ behavior: "wait" });
        await expect.poll(() => cancelledRequest?.failure()?.errorText).toMatch(/ERR_ABORTED|cancelled|canceled|aborted/i);
        s.transportControls.push({ kind: "cancelled", request: heldRequest!.postDataJSON(), failure: cancelledRequest!.failure()!.errorText });
        await expect(s.chooser()).toHaveCount(0); expect(await s.stored(created)).toEqual(expected);
        expect(s.writes(), "Opening, holding and cancelling never saves").toBe(writesBefore);
        await capture(page, info, "customer-held");
        let aborted = false;
        await page.route(pattern, async route => {
          if (!aborted && route.request().postDataJSON().query?.source) { aborted = true; await route.abort("failed"); }
          else await route.continue();
        });
        await s.replace().click(); await whole().press("Enter");
        await expect(s.chooser().getByRole("alert")).toContainText("couldn't load");
        await capture(page, info, "customer-retry");
        await s.chooser().getByRole("button", { name: "Retry", exact: true }).click();
        await expect(s.chooser().getByRole("option", { name: /^customer\b/ })).toBeVisible();
        await page.unrouteAll({ behavior: "wait" }); await s.chooser().getByRole("option", { name: /^customer\b/ }).press("Enter");
        await expect(s.path()).toContainText("customer");
        await s.path().getByRole("button", { name: "Whole value", exact: true }).click();
        await expect(s.path().getByRole("button")).toHaveCount(1);
        await expect(s.chooser().locator(".wf-data-members")).not.toHaveAttribute("aria-busy", "true");
        await expect(s.chooser().getByRole("option", { name: /^customer\b/ })).toBeFocused();
        await s.chooser().getByRole("button", { name: "Back", exact: true }).click(); await expect(whole()).toBeFocused();
        await page.keyboard.press("Escape"); await expect(s.replace()).toBeFocused();
        expect(await s.stored(created)).toEqual(expected);
        s.observations.push("controlled real-request delay/abort: loading, actual browser-cancelled held request, retry, breadcrumb/Back/Escape, no hidden save");
      } finally {
        // Drain the released handler before disabling interception; never swallow an unexpected route error.
        release?.(); await page.unrouteAll({ behavior: "wait" }); page.off("requestfailed", observeCancellation);
      }
      await s.codeApply(created); await expect(s.reference()).toContainText("customerId");
      await capture(page, info, "customer-code-reopened");
      await s.assertComposition();
      await s.publishRun(created, "cust-1042", {}, true);

      // Current unsaved snapshot must refuse the deleted producer without discarding the existing authored value.
      await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
      await page.getByRole("menuitemcheckbox", { name: /^Autosave/ }).click(); await page.keyboard.press("Escape");
      const before = await s.saved(created); await s.select("producer"); await s.canvas();
      await page.getByRole("button", { name: "Delete Get Customer", exact: true }).click(); await s.select();
      await expect(page.locator(".wf-data-reference-problem")).toContainText("Replace it or clear it");
      expect(await s.saved(created)).toEqual(before);
      await page.getByRole("button", { name: "Undo", exact: true }).click(); await s.select();
      await expect(page.locator(".wf-data-reference-problem")).toHaveCount(0);
      await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
      await page.getByRole("menuitemcheckbox", { name: /^Autosave/ }).click(); await page.keyboard.press("Escape");
      s.observations.push("autosave-off producer deletion uses current snapshot, preserves saved document and reference, Undo repairs");
      const viewport = page.viewportSize()!;
      await page.setViewportSize({ width: 320, height: 900 }); await s.open(created); await s.replace().click();
      await customer().press("Enter"); await expect(member()).toBeVisible(); await expect(member()).toBeFocused(); await capture(page, info, "customer-320");
      await page.keyboard.press("Escape"); await expect(s.replace()).toBeFocused(); expect(await s.stored(created)).toEqual(expected);
      await page.setViewportSize(viewport);
    } else {
      await s.open(created); await expect(s.reference()).toContainText("customerId");
      await capture(page, info, "customer-flowchart"); await s.publishRun(created, "cust-1042", {}, true);
    }
  }
}
