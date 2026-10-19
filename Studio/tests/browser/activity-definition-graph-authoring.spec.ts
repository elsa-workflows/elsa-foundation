import { expect, test } from "@playwright/test";
import { mockGraphAuthoring } from "./graphAuthoringMocks";

test.setTimeout(60_000);

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

  await page.getByRole("button", { name: "Create Activity Definition" }).click();
  const create = page.getByRole("dialog", { name: "Create Activity Definition" });
  await expect(create.getByText("Implementation type")).toHaveCount(0);
  await create.getByRole("textbox", { name: "Display name" }).fill("Payment decision");
  const category = create.getByRole("combobox", { name: "Category" });
  await category.fill("Financial controls");
  const flowchartRadio = create.getByRole("radio", { name: "Flowchart" });
  const sequenceRadio = create.getByRole("radio", { name: "Sequence" });
  const rootCardFor = (radio: typeof flowchartRadio) => radio.locator("xpath=ancestor::label[contains(concat(' ', normalize-space(@class), ' '), ' wf-root-card ')]");
  const flowchartCard = rootCardFor(flowchartRadio);
  const sequenceCard = rootCardFor(sequenceRadio);
  const cardBackground = (card: typeof flowchartCard) => card.evaluate(element => getComputedStyle(element).backgroundColor);
  await expect(flowchartRadio).toBeEnabled();
  await expect(sequenceRadio).toBeEnabled();
  await expect(flowchartRadio).toBeChecked();
  expect(await cardBackground(flowchartCard)).not.toBe(await cardBackground(sequenceCard));
  await sequenceCard.click();
  await expect(sequenceRadio).toBeChecked();
  expect(await cardBackground(sequenceCard)).not.toBe(await cardBackground(flowchartCard));
  await create.getByRole("button", { name: "Create definition" }).click();
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
  await expect(page.getByRole("tabpanel", { name: "Public Interface" })).toContainText("Choose the activity outcome for each way the designer can finish.");
  const source = page.getByRole("combobox", { name: "Designer outcome" });
  const target = page.getByRole("combobox", { name: "Activity outcome" });
  await source.selectOption("completed");
  await target.selectOption("accepted");
  await page.getByRole("button", { name: "Connect" }).click();
  await source.selectOption("faulted");
  await target.selectOption("accepted");
  await page.getByRole("button", { name: "Connect" }).click();
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
  await expect(page.getByRole("button", { name: "Connect" })).toHaveCount(0);
});
