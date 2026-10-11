import { expect, test, type Page } from "@playwright/test";

async function mockInputEvidence(page: Page, longText = "") {
  await page.route("**/capabilities", route => route.fulfill({ json: { capabilities: [{
    id: "elsa.api.runtime", contractVersion: "1", links: [
      { rel: "activity-execution", href: "/evidence-inspection/{workflowExecutionId}/{activityExecutionId}" },
      { rel: "activity-execution-value-payload", href: "/evidence-payload/{evidenceId}" }
    ]
  }] } }));
  const snapshot = (inputKey: string, name: string, extra: Record<string, unknown>) => ({
    inputKey, name, subject: "ActivityInput", evidenceId: inputKey,
    captureMode: "DiagnosticSnapshot", captureState: "diagnosticSnapshotCaptured", accessState: "visible",
    phase: "invoke", sequence: 1, evaluationId: `${inputKey}-1`,
    capturedAt: "2026-10-04T10:00:00Z", isSensitive: false, metadata: {}, ...extra
  });
  await page.route("**/evidence-inspection/**", route => route.fulfill({ json: {
    activityExecutionId: "renewal-execution", workflowExecutionId: "renewal-run",
    valueSnapshots: [
      snapshot("policy", "PolicyReference", { accessState: "resolutionAvailable", snapshot: null }),
      snapshot("premium", "Premium", { snapshot: { kind: "number", value: 1250.75 } }),
      snapshot("customer", "Customer", { snapshot: { kind: "object", typeName: "Customer", properties: [
        { name: "name", value: { kind: "string", preview: "Alex Morgan" } },
        { name: "email", value: { kind: "string", preview: "alex@example.com" } }
      ] } }),
      snapshot("notes", "Notes", { snapshot: { kind: "string", preview: longText, length: longText.length } }),
      snapshot("token", "Token", { snapshot: { kind: "string", preview: "HIDDEN_RUNTIME_VALUE" } }),
      snapshot("discount", "Discount", { captureState: "captureFailed", failure: { code: "ExpressionFailed", message: "The discount rate is unavailable.", incidentId: "incident-discount" } })
    ]
  } }));
  await page.route("**/evidence-payload/policy", route => route.fulfill({ json: {
    evidenceId: "policy", captureMode: "DiagnosticSnapshot", payload: { kind: "string", preview: "POL-1042", length: 8, truncated: false }
  } }));
}

for (const theme of ["light", "dark"]) {
  test(`input values lead and source details disclose with the keyboard in ${theme} mode`, async ({ page }) => {
    await mockInputEvidence(page);
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=input-evidence&theme=${theme}`);
    const policy = page.locator(".wf-input-inspection-row").filter({ hasText: "Policy reference" });
    await expect(policy).toHaveCSS("display", "block");
    await expect(policy.locator(".wf-input-inspection-value")).toHaveText("POL-1042");
    await expect(policy.getByRole("heading", { name: "Authored source" })).toBeHidden();
    await expect(page.getByText("Paired evidence")).toHaveCount(0);
    await expect(page.getByText("HIDDEN_RUNTIME_VALUE")).toHaveCount(0);
    await expect(page.getByText("HIDDEN_SOURCE_VALUE")).toHaveCount(0);
    await expect(page.getByText("The discount rate is unavailable.", { exact: false })).toBeVisible();
    await expect(page.locator(".wf-input-inspection-value").filter({ hasText: '""' })).toBeVisible();
    await expect(page.getByText("Alex Morgan", { exact: true })).toBeHidden();
    await page.locator(".wf-input-inspection-value .wf-runtime-snapshot-node > summary").click();
    await expect(page.getByText("Alex Morgan", { exact: true })).toBeVisible();
    await page.locator(".wf-input-inspection-value .wf-runtime-snapshot-node > summary").click();
    const disclosure = policy.locator(".wf-input-inspection-summary");
    await disclosure.focus();
    await page.keyboard.press("Enter");
    await expect(policy.getByRole("heading", { name: "Authored source" })).toBeVisible();
    await expect(policy.getByText("Diagnostic Snapshot", { exact: true })).toBeVisible();
    await expect(policy.locator(".wf-input-inspection-value")).toHaveText("POL-1042");
    await page.keyboard.press("Space");
    await expect(policy.getByRole("heading", { name: "Authored source" })).toBeHidden();
  });
}

test("input evidence wraps within narrow inspectors without horizontal scrolling", async ({ page }) => {
  await mockInputEvidence(page);
  for (const width of [360, 700, 1200]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/?mode=input-evidence");
    if (width < 1000) await page.getByRole("button", { name: "Select activity" }).click();
    const inspector = page.getByRole("complementary", { name: "Run details" });
    await expect(inspector).toBeVisible();
    await expect(page.locator(".wf-input-inspection-value").filter({ hasText: "POL-1042" })).toBeVisible();
    const source = page.locator(".wf-input-inspection-row").filter({ hasText: "Premium" }).locator(".wf-input-inspection-summary");
    await source.click();
    await expect(page.getByText("variables.annualPremium * 1.05", { exact: true })).toBeVisible();
    expect(await inspector.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
  }
});

test("long captured text expands separately from source diagnostics", async ({ page }) => {
  const text = "Renewal terms\n" + "Confirm coverage before processing. ".repeat(20);
  await mockInputEvidence(page, text);
  await page.setViewportSize({ width: 1200, height: 1000 });
  await page.goto("/?mode=input-evidence");
  const notes = page.locator(".wf-input-inspection-row").filter({ hasText: "Notes" });
  await expect(notes.locator(".wf-runtime-input-value-details pre")).toBeHidden();
  await notes.locator(".wf-runtime-input-value-details > summary").click();
  await expect(notes.locator(".wf-runtime-input-value-details pre")).toHaveText(text);
  await expect(notes.getByRole("heading", { name: "Authored source" })).toBeHidden();
  expect(await notes.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
});
