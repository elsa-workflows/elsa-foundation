import { expect, type Page, type TestInfo } from "@playwright/test";

/** Real HTTP and rendered Studio against the acceptance runner's disposable stores. */
export async function activityValidationReportingJourney(page: Page, info: TestInfo, capture: (page: Page, info: TestInfo, name: string) => Promise<void>) {
  await page.goto("/workflows/activity-definitions");
  await page.locator(".studio-page-header").getByRole("button", { name: "Create activity", exact: true }).click();
  await page.getByRole("textbox", { name: "Display name", exact: true }).fill(`Validation guidance ${info.project.name} ${Date.now()}`);
  const provider = page.getByRole("combobox", { name: "Implementation type", exact: true });
  if (await provider.count()) await provider.selectOption((await provider.locator('option[value^="elsa.activity-graph|"]').first().getAttribute("value"))!);
  await page.locator("label.wf-root-card").filter({ has: page.getByRole("radio", { name: "Sequence", exact: true }) }).click();
  const loaded = page.waitForResponse(response => response.request().method() === "GET" && /\/design\/activities\/drafts\/[^/]+$/.test(new URL(response.url()).pathname));
  await page.getByRole("button", { name: "Create definition", exact: true }).click();
  const response = await loaded;
  expect(response.ok()).toBe(true);
  let draft = await response.json();
  const draftUrl = response.url();
  const authorization = response.request().headers().authorization;
  const headers: Record<string, string> = authorization ? { Authorization: authorization } : {};
  await expect(page.locator("#activity-draft-title")).toContainText("Validation guidance");
  await expect(page.getByRole("region", { name: "Provider implementation editor", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Create Sequence block", exact: true })).toBeVisible();
  await capture(page, info, "reference");

  const validateSaved = async () => {
    await page.locator(".ad-authoring-advanced > summary").click();
    const validated = page.waitForResponse(item => item.request().method() === "POST" && item.url() === `${draftUrl}/validate`);
    await page.getByRole("button", { name: "Validate saved revision", exact: true }).click();
    expect((await validated).ok()).toBe(true);
    await page.locator(".ad-authoring-advanced > summary").click();
    await expect(page.getByRole("button", { name: "View 1 draft error", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "View 1 draft error", exact: true }).press("Enter");
    await expect(page.locator('.ad-diagnostics-panel [aria-label="Diagnostic severity counts"]')).toHaveText("1 error");
  };
  const seed = async (kind: "variable" | "output") => {
    const payload = structuredClone(draft.provider.payload);
    payload.variables = kind === "variable" ? [{ referenceKey: "total", name: "Total", type: { alias: "String", collectionKind: "Single" }, storageDriverKey: "elsa.json", initialValue: null }] : [];
    // Contract order differs from mapping order, so pointer index 0 must locate Second.
    const output = (key: string) => ({ referenceKey: key.toLowerCase(), name: key, type: { alias: "String", collectionKind: "Single" }, isRequired: false, isNullable: true, storageDriverKey: "elsa.json" });
    const contract = { ...draft.contract, outputs: kind === "output" ? [output("First"), output("Second")] : [] };
    payload.outputMappings = kind === "output" ? [{ outputReferenceKey: "second", source: null }] : [];
    const candidate = { expectedRevision: draft.revision, contract, provider: { ...draft.provider, payload }, layout: draft.layout, activityPresentation: draft.activityPresentation };
    const checked = await page.request.post(`${draftUrl}/candidate-validation`, { headers, data: candidate });
    expect(checked.status()).toBe(200);
    const body = await checked.json();
    expect(body.state).toBe("invalid");
    expect(body.validation.isValid).toBe(false);
    expect(body.validation.diagnostics).toHaveLength(1);
    expect(body.validation.diagnostics[0].severity).toBe("error");
    expect(body.validation.diagnostics[0].location.jsonPointer).toBe(kind === "variable" ? "/variables/0/initialValue" : "/outputMappings/0/source");
    const saved = await page.request.put(draftUrl, { headers, data: candidate });
    expect(saved.ok()).toBe(true);
    draft = await saved.json();
    await page.reload();
    await expect(page.locator("#activity-draft-title")).toContainText("Validation guidance");
    await validateSaved();
  };
  await seed("variable");
  await expect(page.getByText("The variable initial value is not a valid expression.", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Edit variable initial value", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "variable-blocker");
  await page.getByRole("button", { name: "Edit variable initial value", exact: true }).press("Enter");
  await expect(page.getByRole("tab", { name: "Variables", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("textbox", { name: "Variable default value", exact: true })).toBeFocused();
  await capture(page, info, "variable-focused");
  await page.getByRole("button", { name: "Return to diagnostic", exact: true }).click();
  await expect(page.getByRole("button", { name: "Edit variable initial value", exact: true })).toBeFocused();

  await seed("output");
  await expect(page.getByText("The output value source is missing or is not a valid expression.", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Edit output value source", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "output-blocker");
  await page.getByRole("button", { name: "Edit output value source", exact: true }).press("Enter");
  await expect(page.getByRole("tab", { name: "Outputs (2)", exact: true })).toHaveAttribute("aria-selected", "true");
  const source = page.locator('[data-output-reference-key="second"] [data-output-source]');
  await expect(source.getByRole("button", { name: "Set value", exact: true })).toBeFocused();
  await capture(page, info, "output-focused");

  let reviews = 0;
  page.on("request", request => { if (request.method() === "POST" && request.url().endsWith("/publication-preflight")) reviews++; });
  await page.locator(".ad-publication-toolbar-trigger").click();
  const dialog = page.getByRole("dialog", { name: "Review & publish", exact: true });
  await expect(dialog.getByRole("button", { name: "Edit output value source", exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: "Close publication review", exact: true }).click();
  const status = page.getByRole("button", { name: "Publication blocked — view issues", exact: true });
  await expect(status).toBeVisible();
  const existingReviews = reviews;
  await status.press("Enter");
  await expect(dialog.getByRole("button", { name: "Edit output value source", exact: true })).toBeVisible();
  expect(reviews).toBe(existingReviews);
  await capture(page, info, "review-retained");
}
