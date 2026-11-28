import { expect, test } from "@playwright/test";
import { mockGraphAuthoring } from "./graphAuthoringMocks";

test.setTimeout(60_000);

for (const width of [390, 1440]) {
  for (const theme of ["light", "dark"] as const) {
    test(`Activity Definition graph workbench matches the shared designer at ${width}px in ${theme} mode`, async ({ page }) => {
      await page.setViewportSize({ width, height: 1000 });
      await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), theme);
      await mockGraphAuthoring(page);
      await page.goto(`/?mode=activity-definition-graph-authoring&theme=${theme}`);
      await expect(page.locator("html")).toHaveAttribute("data-theme-mode", theme);

      const toolbar = page.locator(".ad-draft-editor-toolbar > .wf-editor-toolbar");
      const canvas = page.locator(".ad-graph-canvas");
      await expect(page.getByRole("heading", { name: "Browser graph activity" })).toBeVisible();
      await expect(toolbar).toHaveClass(/wf-editor-toolbar/);
      await expect(toolbar.locator("nav[aria-label='Breadcrumb']")).toContainText("Activity Definitions");
      await expect(toolbar.locator(".wf-editor-title")).toContainText("Browser graph activity");
      await expect(toolbar.locator(".wf-canvas-tools")).toBeVisible();
      await expect(toolbar.getByRole("button", { name: "Undo Activity Graph edit" })).toBeVisible();
      await expect(toolbar.getByRole("button", { name: "Auto-layout Activity Graph" })).toBeVisible();
      await expect(toolbar.getByRole("button", { name: "Test Run" })).toBeVisible();
      await expect(toolbar.locator(".ad-authoring-history")).toBeVisible();
      await expect(toolbar.locator(".ad-authoring-advanced")).toBeVisible();
      await expect(page.getByRole("tablist", { name: "Activity Definition authoring views" })).toBeVisible();
      await expect(page.locator(".ad-graph-scope-label")).toHaveCount(0);
      await expect(canvas.locator(".wf-canvas-tools")).toHaveCount(0);
      await expect(canvas).toHaveAttribute("data-graph-root-location", "");
      await expect(page.locator(".ad-graph-palette [data-graph-root-location]")).toHaveCount(0);
      const expandActivities = page.getByRole("button", { name: "Expand activities panel" });
      if (await expandActivities.isVisible()) await expandActivities.click();
      await expect(page.locator(".wf-palette-category-toggle").first()).toHaveAttribute("aria-expanded", "true");
      await expect(page.locator(".wf-palette-activity").first()).toBeVisible();

      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await page.screenshot({ path: test.info().outputPath(`activity-graph-${theme}-${width}.png`), fullPage: true });
    });
  }
}

for (const graph of ["sequence", "flowchart"] as const) {
  test(`Activity Definition ${graph} canvas fills the center pane across authoring tabs`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await mockGraphAuthoring(page);
    await page.goto(`/?mode=activity-definition-graph-authoring&graph=${graph}&theme=dark`);

    const canvas = page.locator(graph === "sequence" ? ".ad-graph-designer-panel .wf-sequence-designer" : ".ad-graph-designer-panel .wf-canvas");
    const centerContent = page.locator(".ad-graph-center-content");
    const viewHost = page.locator(".ad-graph-authoring-view-host");
    await expect(canvas).toBeVisible();
    await expect(viewHost).toHaveAttribute("hidden", "");
    const assertCanvasFillsCenter = async () => {
      const canvasBounds = (await canvas.boundingBox())!;
      const centerBounds = (await centerContent.boundingBox())!;
      expect(Math.abs(centerBounds.y + centerBounds.height - canvasBounds.y - canvasBounds.height)).toBeLessThanOrEqual(1);
    };

    await assertCanvasFillsCenter();
    await page.screenshot({ path: testInfo.outputPath(`${graph}-canvas-fills-center.png`) });

    await page.getByRole("tab", { name: "Code", exact: true }).click();
    await expect(page.getByRole("tabpanel", { name: "Code", exact: true })).toBeVisible();
    await expect(viewHost).toBeVisible();
    await expect(viewHost).not.toHaveAttribute("hidden");

    await page.getByRole("tab", { name: "Designer", exact: true }).click();
    await expect(canvas).toBeVisible();
    await expect(viewHost).toHaveAttribute("hidden", "");
    await assertCanvasFillsCenter();
  });
}

test("Activity Definition graph authoring shares the designer without workflow lifecycle UI", async ({ page }) => {
  const requests = await mockGraphAuthoring(page);
  await page.goto("/?mode=activity-definition-graph-authoring&theme=dark");

  await expect(page.getByRole("heading", { name: "Browser graph activity" })).toBeVisible();
  await expect(page.getByLabel("Activity Graph designer")).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo Activity Graph edit" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Auto-layout Activity Graph" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Collapse activities panel" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Maximize activities panel" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Collapse inspector panel" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Maximize inspector panel" })).toBeVisible();
  await expect(page.getByRole("separator", { name: "Resize activities panel" })).toBeVisible();
  await expect(page.getByRole("separator", { name: "Resize inspector panel" })).toBeVisible();
  await expect(page.getByRole("tablist", { name: "Activity inspector sections" })).toContainText("Inputs");
  await expect(page.getByRole("tablist", { name: "Activity inspector sections" })).toContainText("Outputs");
  await expect(page.getByRole("tablist", { name: "Activity inspector sections" })).toContainText("Variables");
  await expect(page.getByRole("tablist", { name: "Activity inspector sections" })).toContainText("Details");
  await expect(page.getByRole("tablist", { name: "Activity inspector sections" })).toContainText("Version");
  await expect(page.getByRole("button", { name: /Publish|Deploy|Instances|Triggers|Schedule/i })).toHaveCount(0);

  await page.getByRole("button", { name: "New activity definition" }).click();
  const create = page.getByRole("dialog", { name: "New activity definition" });
  await expect(create.getByText("Implementation type")).toHaveCount(0);
  await create.getByRole("textbox", { name: "Display name" }).fill("Payment decision");
  const category = create.getByRole("combobox", { name: "Category" });
  await category.fill("Financial controls");
  const flowchartRadio = create.getByRole("radio", { name: "Flowchart" });
  const sequenceRadio = create.getByRole("radio", { name: "Sequence" });
  const rootCardFor = (radio: typeof flowchartRadio) => radio.locator("xpath=ancestor::label[contains(concat(' ', normalize-space(@class), ' '), ' wf-root-card ')]");
  const flowchartCard = rootCardFor(flowchartRadio);
  const sequenceCard = rootCardFor(sequenceRadio);
  const cardBackground = (card: typeof flowchartCard) => card.evaluate(async element => {
    await Promise.all(element.getAnimations().map(animation => animation.finished));
    return getComputedStyle(element).backgroundColor;
  });
  await expect(flowchartRadio).toBeEnabled();
  await expect(sequenceRadio).toBeEnabled();
  await expect(flowchartRadio).toBeChecked();
  expect(await cardBackground(flowchartCard)).not.toBe(await cardBackground(sequenceCard));
  await sequenceCard.click();
  await expect(sequenceRadio).toBeChecked();
  expect(await cardBackground(sequenceCard)).not.toBe(await cardBackground(flowchartCard));
  await create.getByRole("button", { name: "Create" }).click();
  await expect(page.getByText("Created Payment decision.")).toBeVisible();
  expect(requests).toHaveLength(1);
  expect(requests[0]).toMatchObject({
    category: "Financial controls",
    displayName: "Payment decision",
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "2",
      payload: {
        rootActivity: {
          nodeId: "root",
          activityVersionId: "sequence-v1"
        }
      }
    }
  });

  await page.getByRole("tab", { name: "Public Interface" }).click();
  await expect(page.getByRole("tabpanel", { name: "Public Interface" }).getByRole("heading", { name: "Completion paths", exact: true })).toBeVisible();
  const source = page.getByRole("combobox", { name: "Designer outcome" });
  const target = page.getByRole("combobox", { name: "Activity outcome" });
  await source.selectOption("completed");
  await target.selectOption("accepted");
  await page.getByRole("button", { name: "Connect", exact: true }).click();
  await source.selectOption("faulted");
  await target.selectOption("accepted");
  await page.getByRole("button", { name: "Connect", exact: true }).click();
  await expect(page.getByText("Completed → Accepted")).toBeVisible();
  await expect(page.getByText("Faulted → Accepted")).toBeVisible();

  await page.getByRole("tab", { name: "Code" }).click();
  const code = page.getByRole("textbox", { name: "Activity Definition JSON" });
  const projection = JSON.parse(await code.inputValue()) as Record<string, unknown>;
  projection.presentationLabel = "Reviewed in JSON";
  await code.fill(JSON.stringify(projection, null, 2));
  await page.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(code).toHaveValue(/Reviewed in JSON/);

  const diagnostics = page.locator(".ad-diagnostics-panel");
  await expect(diagnostics.getByText("Public contract")).toBeVisible();
  await expect(diagnostics.getByText("Boundary mappings")).toBeVisible();
  await expect(diagnostics.getByText("Graph", { exact: true })).toBeVisible();
  await expect(diagnostics.getByText("Provider", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Test Run" }).click();
  await expect(page.getByRole("dialog", { name: "Browser graph activity" })).toContainText("Revision 3 validated");
  await page.getByRole("button", { name: "Close Test Run" }).click();

  await page.getByRole("button", { name: "Preview legacy schema" }).click();
  await page.getByRole("tab", { name: "Public Interface" }).click();
  await page.getByText("Compatibility details", { exact: true }).click();
  await expect(page.getByText(/Schema 1 retains its historical single/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Connect", exact: true })).toHaveCount(0);
});

test("Activity Definition input bindings select a public input directly from Literal", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await mockGraphAuthoring(page);
  await page.goto("/?mode=activity-definition-graph-authoring&theme=dark&bindings=empty&no-javascript=true");

  await page.locator("[data-graph-node-id='write-line-1']").click();
  const syntax = page.getByRole("button", { name: "Message expression syntax: Literal", exact: true });
  await expect(syntax).toBeVisible();
  const bindingTool = page.locator(".wf-input-bindings-toggle");
  await expect(bindingTool).toBeVisible();
  await expect(bindingTool).toHaveAttribute("aria-expanded", "false");

  await bindingTool.click();
  const bindings = page.locator(".wf-js-input-bindings");
  await expect(bindings).toBeVisible();
  const picker = bindings.getByLabel("Use input directly");
  await expect(picker).toBeVisible();
  await expect(syntax).toBeVisible();
  await expect(bindingTool).toHaveAttribute("aria-expanded", "true");
  await picker.selectOption("customer-id");
  await expect(page.getByRole("button", { name: "Message expression syntax: Request input", exact: true })).toBeVisible();
  await expect(picker).toHaveValue("customer-id");
  await expect(bindingTool).toHaveAttribute("aria-label", "Activity input bindings (1)");

  await bindingTool.click();
  await expect(bindings).toBeHidden();
  await bindingTool.click();
  await expect(bindings).toBeVisible();
  await expect(picker).toHaveValue("customer-id");
  await picker.selectOption("order-id");
  await expect(picker).toHaveValue("order-id");

  await page.setViewportSize({ width: 390, height: 900 });
  const headerTools = page.locator(".wf-property-row-header .studio-field-caption-actions");
  await expect(headerTools.locator(".wf-conversion-toggle")).toBeVisible();
  await expect(headerTools.locator(".wf-input-bindings-toggle")).toBeVisible();
  await expect(headerTools).toHaveCSS("gap", "4px");
  await expect(await page.locator(".wf-property-row").evaluate(element => element.scrollWidth === element.clientWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath("activity-input-bindings-narrow.png"), fullPage: true });
});

test("Activity Definition empty JavaScript inputs bind directly and restore the selected key after reopening", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await mockGraphAuthoring(page);
  await page.goto("/?mode=activity-definition-graph-authoring&theme=dark&bindings=empty-javascript");

  const node = page.locator("[data-graph-node-id='write-line-1']");
  await node.click();
  const syntax = page.getByRole("button", { name: "Message expression syntax: JavaScript", exact: true });
  await expect(syntax).toBeVisible();
  const bindingTool = page.locator(".wf-input-bindings-toggle");
  await bindingTool.click();

  const bindings = page.locator(".wf-js-input-bindings");
  const picker = bindings.getByLabel("Use input directly");
  await expect(bindings.getByLabel("Bind as JavaScript argument")).toBeVisible();
  await picker.selectOption("customer-id");
  await expect(page.getByRole("button", { name: "Message expression syntax: Request input", exact: true })).toBeVisible();
  await expect(picker).toHaveValue("customer-id");

  await page.getByRole("tab", { name: "Code" }).click();
  const code = page.getByRole("textbox", { name: "Activity Definition JSON" });
  const draft = JSON.parse(await code.inputValue()) as {
    implementation: {
      payload: {
        rootActivity: {
          structure: {
            payload: {
              activities: Array<{ inputs?: Array<{ referenceKey: string; value?: unknown }> }>;
            };
          };
        };
      };
    };
  };
  expect(draft.implementation.payload.rootActivity.structure.payload.activities[0]?.inputs?.find(input => input.referenceKey === "message")?.value).toMatchObject({
    expressionType: "WorkflowRequest",
    value: { memberKey: "customer-id" }
  });

  await page.getByRole("tab", { name: "Designer" }).click();
  await page.locator(".react-flow__pane").click({ position: { x: 10, y: 10 } });
  await expect(page.locator(".wf-input-bindings-toggle")).toHaveCount(0);
  await node.click();
  await expect(page.getByRole("button", { name: "Message expression syntax: Request input", exact: true })).toBeVisible();
  await page.locator(".wf-input-bindings-toggle").click();
  await expect(page.locator(".wf-js-input-bindings").getByLabel("Use input directly")).toHaveValue("customer-id");
});

test("Activity Definition empty JavaScript inputs can bind as an argument before entering the expression", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await mockGraphAuthoring(page);
  await page.goto("/?mode=activity-definition-graph-authoring&theme=dark&bindings=empty-javascript");

  const node = page.locator("[data-graph-node-id='write-line-1']");
  await node.click();
  const bindingTool = page.locator(".wf-input-bindings-toggle");
  await bindingTool.click();

  const bindings = page.locator(".wf-js-input-bindings");
  const argumentPicker = bindings.getByLabel("Bind as JavaScript argument");
  await expect(bindings.getByLabel("Use input directly")).toBeVisible();
  await argumentPicker.selectOption("CustomerId");
  await expect(page.getByRole("button", { name: "Message expression syntax: JavaScript", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  const source = page.locator(".cm-content[contenteditable='true']");
  await expect(source).toBeVisible();
  await expect(source).toHaveText("");
  await source.fill("args.CustomerId");

  await page.getByRole("tab", { name: "Code" }).click();
  const code = page.getByRole("textbox", { name: "Activity Definition JSON" });
  const draft = JSON.parse(await code.inputValue()) as {
    implementation: {
      payload: {
        rootActivity: {
          structure: {
            payload: {
              activities: Array<{ inputs?: Array<{ referenceKey: string; value?: unknown }> }>;
            };
          };
        };
      };
    };
  };
  expect(draft.implementation.payload.rootActivity.structure.payload.activities[0]?.inputs?.find(input => input.referenceKey === "message")?.value).toMatchObject({
    expressionType: "JavaScript",
    value: {
      language: "JavaScript",
      source: "args.CustomerId",
      parameters: { CustomerId: { kind: "WorkflowRequest", memberKey: "customer-id" } }
    }
  });

  await page.getByRole("tab", { name: "Designer" }).click();
  await page.locator(".react-flow__pane").click({ position: { x: 10, y: 10 } });
  await node.click();
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  const reopenedSource = page.locator(".cm-content[contenteditable='true']");
  await expect(reopenedSource).toHaveText("args.CustomerId");
  await page.locator(".wf-input-bindings-toggle").click();
  await expect(page.locator(".wf-js-input-binding-list")).toContainText("CustomerId → args.CustomerId");
});

test("Activity Definition input bindings use a compact inspector tool beside conversion", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await mockGraphAuthoring(page);
  await page.goto("/?mode=activity-definition-graph-authoring&theme=dark&bindings=true");

  await page.locator("[data-graph-node-id='write-line-1']").click();
  const bindingTool = page.getByRole("button", { name: "Activity input bindings (1)" });
  await expect(bindingTool).toBeVisible();
  await expect(bindingTool).toHaveAttribute("aria-expanded", "false");
  const fieldTools = page.locator(".studio-field-caption-actions");
  await expect(fieldTools.locator(".wf-input-bindings-toggle")).toBeVisible();
  await expect(fieldTools.locator(".wf-conversion-toggle")).toBeVisible();
  const bindings = page.locator(".wf-js-input-bindings");
  await expect(bindings).toBeHidden();

  await bindingTool.click();
  await expect(bindingTool).toHaveAttribute("aria-expanded", "true");
  await expect(bindings).toBeVisible();
  await expect(bindings.locator(".wf-js-input-binding-list")).toContainText("CustomerId → args.customer");
  await bindings.getByLabel("Bind as JavaScript argument").focus();
  await page.keyboard.press("Escape");
  await expect(bindingTool).toHaveAttribute("aria-expanded", "false");
  await expect(bindingTool).toBeFocused();
});
