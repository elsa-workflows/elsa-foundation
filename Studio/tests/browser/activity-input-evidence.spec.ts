import { expect, test, type Page } from "@playwright/test";
import axe from "axe-core";

async function mockInputEvidence(page: Page, longText = "", includeOutputEvidence = false) {
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
  const outputSnapshots = includeOutputEvidence ? [
    snapshot("total", "Total", {
      subject: "ActivityOutput", type: { typeName: "System.Decimal" },
      snapshot: { kind: "number", value: 42 }
    }),
    snapshot("response", "Response", {
      subject: "ActivityOutput", type: { typeName: "System.Object" },
      snapshot: { kind: "object", typeName: "Response", properties: [
        { name: "status", value: { kind: "string", preview: "Accepted" } },
        { name: "reference", value: { kind: "string", preview: "INV-1042" } }
      ] }
    })
  ] : [];
  await page.route("**/evidence-inspection/**", route => route.fulfill({ json: {
    activityExecutionId: "renewal-execution", workflowExecutionId: "renewal-run",
    outcomeNames: includeOutputEvidence ? ["Succeeded", "NeedsReview"] : [],
    valueSnapshots: [
      snapshot("policy", "PolicyReference", { accessState: "resolutionAvailable", snapshot: null }),
      snapshot("premium", "Premium", { snapshot: { kind: "number", value: 1250.75 } }),
      snapshot("customer", "Customer", { snapshot: { kind: "object", typeName: "Customer", properties: [
        { name: "name", value: { kind: "string", preview: "Alex Morgan" } },
        { name: "email", value: { kind: "string", preview: "alex@example.com" } }
      ] } }),
      snapshot("notes", "Notes", { snapshot: { kind: "string", preview: longText, length: longText.length } }),
      snapshot("token", "Token", { snapshot: { kind: "string", preview: "HIDDEN_RUNTIME_VALUE" } }),
      snapshot("discount", "Discount", { captureState: "captureFailed", failure: { code: "ExpressionFailed", message: "The discount rate is unavailable.", incidentId: "incident-discount" } }),
      ...outputSnapshots
    ]
  } }));
  await page.route("**/evidence-payload/policy", route => route.fulfill({ json: {
    evidenceId: "policy", captureMode: "DiagnosticSnapshot", payload: { kind: "string", preview: "POL-1042", length: 8, truncated: false }
  } }));
}

for (const theme of ["light", "dark"]) {
  test(`outputs and outcomes stay compact and keyboard-disclosable in ${theme} theme`, async ({ page }) => {
    await mockInputEvidence(page, "", true);

    for (const width of [1440, 360]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.goto(`/?mode=input-evidence&theme=${theme}`);
      if (width < 1000) await page.getByRole("button", { name: "Select activity" }).click();

      const inspector = page.getByRole("complementary", { name: "Run details" });
      await expect(inspector).toBeVisible();
      const outputs = page.locator(".wf-instance-section").filter({ has: page.getByRole("heading", { name: /Outputs/ }) });
      const outputRows = outputs.locator(".wf-runtime-input");
      const total = outputRows.filter({ hasText: "Total" });
      const response = outputRows.filter({ hasText: "Response" });

      await expect(total.locator(".wf-runtime-input-value")).toHaveText("42");
      await expect(total.locator("header strong")).toHaveCSS("text-transform", "none");
      await expect(total.locator("header span small")).toHaveCSS("text-transform", "none");
      await expect(outputs.locator(".wf-runtime-evidence-heading .wf-runtime-capture-mode")).toHaveText("Diagnostic Snapshot");
      await expect(outputs.locator(".wf-runtime-capture-mode")).toHaveCount(1);

      const responseTree = response.locator(".wf-runtime-snapshot-node").first();
      await expect(responseTree).not.toHaveAttribute("open", "");
      await expect(response.getByText("Accepted", { exact: true })).toBeHidden();
      await responseTree.locator("summary").focus();
      await page.keyboard.press("Enter");
      await expect(response.getByText("Accepted", { exact: true })).toBeVisible();

      const outcomes = page.locator(".wf-instance-section").filter({ has: page.getByRole("heading", { name: /Outcomes/ }) });
      await expect(outcomes.getByText("Succeeded", { exact: true })).toBeVisible();
      await expect(outcomes.getByText("NeedsReview", { exact: true })).toBeVisible();
      await expect(outcomes.getByRole("listitem")).toHaveCount(2);
      await expect(outcomes.getByText("Outcome", { exact: true })).toHaveCount(0);

      expect(await inspector.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);

      if (width === 1440) {
        const workbench = page.getByTestId("run-workbench");
        await expect(workbench).toHaveAttribute("data-layout-mode", "desktop");
        await page.getByRole("button", { name: "Maximize run details panel" }).click();
        await expect(page.getByRole("button", { name: "Restore run details panel" })).toBeVisible();
        await expect(workbench).toHaveClass(/inspector-maximized/);
        await expect(total.locator(".wf-runtime-input-value")).toHaveText("42");
        expect(await inspector.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
        expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);

        await page.addScriptTag({ content: axe.source });
        const violations = await page.evaluate(async () => {
          const root = document.querySelector('[aria-label="Run details"]');
          if (!root) return [{ id: "missing-run-details", impact: "critical", help: "Run details inspector is missing." }];
          const result = await (window as Window & { axe: typeof axe }).axe.run(root, { resultTypes: ["violations"] });
          return result.violations
            .filter(violation => violation.impact === "critical" || violation.impact === "serious")
            .map(({ id, impact, help }) => ({ id, impact, help }));
        });
        await test.info().attach("activity-evidence-axe-critical-serious.json", {
          body: JSON.stringify(violations, null, 2),
          contentType: "application/json"
        });
        expect(violations).toEqual([]);
        await page.getByRole("button", { name: "Restore run details panel" }).click();
        await expect(workbench).not.toHaveClass(/inspector-maximized/);
      }
    }
  });
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
