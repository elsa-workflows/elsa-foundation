import { expect, type Page, type TestInfo } from "@playwright/test";
import type { ActivityDefinitionDraftView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityDefinitionTypes";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real owning APIs; only the named transient-outage control intercepts one validation response. */
export async function nativeBlockValidationJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(backend + path, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.json() as Promise<T>;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>("/design/activities/definitions?search=Elsa.Activities.Sequence.Activities.Sequence");
  const sequence = catalog.items.find(item => item.definition.activityTypeKey === "Elsa.Activities.Sequence.Activities.Sequence")!.definition.headVersionId;
  const rootContract = await api<{ contract: { outcomes: { name: string; referenceKey: string; isEmitted: boolean }[] } }>(`/design/activities/versions/${sequence}`);
  const done = rootContract.contract.outcomes.find(item => item.isEmitted && (item.name || item.referenceKey).toLowerCase() === "done")!;
  const member = { type: { alias: "Decimal", collectionKind: "Single", supportsDurability: true }, isRequired: false, isNullable: false, durability: "Required", storageDriverKey: "elsa.json" };
  // Missing required Set Output inputs is invalid even though current main supports the intrinsic.
  const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
    category: "Qualification", displayName: `Validation ${info.project.name} ${Date.now()}`, description: null,
    contract: { contractSchemaVersion: "1", inputs: [{ ...member, referenceKey: "amount", name: "Amount" }], outputs: [{ ...member, referenceKey: "total", name: "Total" }], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: {
      rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "set-output", activityVersionId: "elsa.intrinsic.set-output@1", inputs: [], outputs: [], intrinsic: { kind: "SetOutput", valueType: { alias: "Object", collectionKind: "Single" } } }], variables: [] } } },
      variables: [], outputMappings: [], outcomeMappings: [{ sourceOutcomeReferenceKey: done.referenceKey, boundaryOutcomeReferenceKey: "done" }]
    } }, layout: [], activityPresentation: []
  });
  const draftPath = `/design/activities/drafts/${created.draft.draftId}`;
  const route = `/workflows/activity-definitions?definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}`;
  await page.goto(route);
  await expect(page.getByRole("region", { name: "Provider implementation editor", exact: true })).toBeVisible();
  const beforeInvalid = await api<ActivityDefinitionDraftView>(draftPath);
  await page.getByRole("button", { name: "Create Flowchart block", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "Create Flowchart block", exact: true });
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Test");
  const invalidResponse = page.waitForResponse(response => response.url().endsWith("/candidate-validation"));
  await dialog.getByRole("button", { name: "Preview change", exact: true }).click();
  const invalid = await invalidResponse;
  expect(invalid.status()).toBe(200);
  expect((await invalid.json()).state).toBe("invalid");
  await expect(dialog.getByRole("alert")).toContainText("activity.graph.set-output-invalid");
  await expect(dialog.getByRole("button", { name: "Apply change", exact: true })).toBeDisabled();
  expect(await api(draftPath)).toEqual(beforeInvalid);
  await capture(page, info, "block-validation-invalid");
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();

  // Repair only this disposable fixture through the normal API; Set Output runtime behavior is unchanged.
  const provider = structuredClone(beforeInvalid.provider);
  const payload = provider.payload as { rootActivity: { structure: { payload: { activities: unknown[] } } } };
  payload.rootActivity.structure.payload.activities = [];
  await api(draftPath, "PUT", { expectedRevision: beforeInvalid.revision, contract: beforeInvalid.contract, provider, layout: beforeInvalid.layout, activityPresentation: beforeInvalid.activityPresentation });
  await page.reload();
  await page.getByRole("button", { name: "Create Flowchart block", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "Create Flowchart block", exact: true });
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Retry block");
  const beforeOutage = await api<ActivityDefinitionDraftView>(draftPath);
  let attempts = 0;
  await page.route("**/candidate-validation", async route => {
    attempts++;
    if (attempts === 1) await route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "Validation service unavailable", detail: "The validation service is restarting. Try again.", errorCode: "qualification.transient-unavailable", traceId: "owned-transient-control", credentials: "must-not-render" }) });
    else await route.continue();
  });
  await dialog.getByRole("button", { name: "Preview change", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("The validation service is restarting.");
  await expect(dialog.getByRole("alert")).toContainText("HTTP 503");
  await expect(dialog.getByRole("alert")).toContainText("owned-transient-control");
  await expect(dialog).not.toContainText("must-not-render");
  const retry = dialog.getByRole("button", { name: "Retry validation", exact: true });
  await expect(retry).toBeFocused();
  await expect(dialog.getByRole("button", { name: "Apply change", exact: true })).toBeDisabled();
  expect(await api(draftPath)).toEqual(beforeOutage);
  await capture(page, info, "block-validation-outage-control");
  await retry.press("Enter");
  await expect(dialog.getByRole("heading", { name: "Proposed change", exact: true })).toBeVisible();
  expect(attempts).toBe(2);
  await expect(dialog.getByRole("alert")).toHaveCount(0);
  await capture(page, info, "block-validation-retried");
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Current block");
  await expect(dialog.getByRole("button", { name: "Apply change", exact: true })).toBeDisabled();
  await dialog.getByRole("button", { name: "Preview change", exact: true }).click();
  await expect(dialog.getByRole("heading", { name: "Proposed change", exact: true })).toBeVisible();
  let saves = 0;
  page.on("request", request => { if (request.method() === "PUT" && request.url().endsWith(draftPath)) saves++; });
  await dialog.getByRole("button", { name: "Apply change", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect.poll(async () => (await api<ActivityDefinitionDraftView>(draftPath)).activityPresentation?.map(item => item.displayName)).toContain("Current block");
  expect(saves).toBe(1);
  await page.reload();
  await expect(page.getByRole("region", { name: "Provider implementation editor", exact: true })).toBeVisible();
  await expect(page.locator("[data-graph-node-id]").filter({ hasText: "Current block" })).toHaveCount(1);
  await capture(page, info, "block-validation-persisted");
  await info.attach("validation-control.json", { body: JSON.stringify({ invalidStatus: invalid.status(), unchangedInvalid: true, transientOutage: "one simulated503 response; retry and subsequent validations use real backend", unchangedOutage: true, attempts, saves }), contentType: "application/json" });
}
