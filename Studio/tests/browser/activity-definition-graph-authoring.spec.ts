import { expect, test, type Page } from "@playwright/test";

test.setTimeout(60_000);

test("Activity Definition graph authoring shares the designer without workflow lifecycle UI", async ({ page }) => {
  const requests = await mockGraphAuthoring(page);
  await page.goto("/?mode=activity-definition-graph-authoring&theme=black-glass");

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
  await expect(create.getByRole("radio", { name: /Flowchart/ })).toBeChecked();
  await create.getByRole("radio", { name: /Sequence/ }).check({ force: true });
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
  await expect(page.getByRole("tabpanel", { name: "Public Interface" })).toContainText("Several sources may converge on one public outcome");
  const source = page.getByRole("combobox", { name: "Root outcome reference key" });
  const target = page.getByRole("combobox", { name: "Boundary outcome reference key" });
  await source.selectOption("completed");
  await target.selectOption("accepted");
  await page.getByRole("button", { name: "Add mapping" }).click();
  await source.selectOption("faulted");
  await target.selectOption("accepted");
  await page.getByRole("button", { name: "Add mapping" }).click();
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
  await expect(page.getByText(/Schema 1 retains its historical single/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Add mapping" })).toHaveCount(0);
});

async function mockGraphAuthoring(page: Page) {
  const requests: unknown[] = [];
  await page.route("**/capabilities", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      capabilities: [
        {
          id: "elsa.api.activity-design",
          contractVersion: "1",
          links: [
            { rel: "activity-definitions", href: "design/activities/definitions" },
            { rel: "activity-authoring-capabilities", href: "design/activities/authoring-capabilities" },
            { rel: "activity-catalog", href: "design/activities/catalog" }
          ]
        },
        {
          id: "elsa.api.expressions",
          contractVersion: "1",
          links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }]
        }
      ]
    })
  }));
  await page.route("**/design/activities/authoring-capabilities", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      contractSchemaVersions: ["1"],
      activityTypeKeyRules: {
        serverGenerated: true,
        allowsPreCreationOverride: true,
        immutable: true,
        prefix: "elsa.user",
        pattern: "^elsa\\.user\\..+$",
        maximumLength: 160,
        collisionScope: "tenantId + activityTypeKey"
      },
      providers: [{
        providerKey: "elsa.activity-graph",
        displayName: "Activity Graph",
        manifestSchemas: [
          { schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions: ["1"] },
          { schemaVersion: "2", isAuthorable: true, migratableFromSchemaVersions: ["1", "2"] }
        ],
        requiredOutcomes: []
      }],
      types: [],
      storageDriverKeys: [],
      snapshotFingerprint: "sha256:browser"
    })
  }));
  await page.route("**/design/activities/catalog", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      activities: [
        catalogActivity("flowchart-v1", "Elsa.Flowchart", "Flowchart", "Composition", {
          kind: "Flowchart",
          schemaVersion: "1",
          payload: { activities: [], connections: [] }
        }, [
          { type: "outcome", referenceKey: "completed", displayName: "Completed" },
          { type: "outcome", referenceKey: "faulted", displayName: "Faulted" }
        ]),
        catalogActivity("sequence-v1", "Elsa.Sequence", "Sequence", "Composition", {
          kind: "Sequence",
          schemaVersion: "1",
          payload: { activities: [] }
        }),
        catalogActivity("bpmn-v1", "Elsa.Bpmn", "BPMN", "Composition", {
          kind: "Bpmn",
          schemaVersion: "1",
          payload: { activities: [], connections: [] }
        }),
        catalogActivity("write-line-v1", "Elsa.WriteLine", "Write line", "Primitives")
      ]
    })
  }));
  await page.route("**/expressions/descriptors", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ items: [] })
  }));
  await page.route("**/design/activities/definitions", async route => {
    requests.push(route.request().postDataJSON());
    await route.fulfill({
      status: 201,
      contentType: "application/json",
      body: JSON.stringify({
        definition: {
          definitionId: "created-definition",
          activityTypeKey: "elsa.user.payment-decision",
          category: "Financial controls",
          displayName: "Payment decision",
          contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design" }
        },
        draft: {
          draftId: "created-draft",
          definitionId: "created-definition",
          revision: 1,
          status: "active",
          providerKey: "elsa.activity-graph",
          providerSchemaVersion: "2",
          updatedAt: "2026-07-28T00:00:00Z"
        }
      })
    });
  });
  return requests;
}

function catalogActivity(
  activityVersionId: string,
  activityTypeKey: string,
  displayName: string,
  category: string,
  structure: Record<string, unknown> | null = null,
  ports: unknown[] = []
) {
  return {
    activityVersionId,
    activityTypeKey,
    version: "1.0.0",
    category,
    displayName,
    description: null,
    executionType: "sync",
    inputs: [],
    outputs: [],
    ports,
    designFacets: [],
    available: true,
    authoringTemplate: {
      nodeId: "template",
      activityVersionId,
      inputs: [],
      outputs: [],
      structure
    }
  };
}
