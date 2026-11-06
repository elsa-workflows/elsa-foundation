import type { Page } from "@playwright/test";

/** Backend routes the Activity Definition graph-authoring fixture needs (capabilities, catalog, create). */
export async function mockGraphAuthoring(page: Page, resultBindings = false) {
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
        ...(resultBindings ? [{ id: "activity-expressions.tooling.v1", contractVersion: "1", links: ["context", "validate"].map(operation => ({
          rel: `expression-tooling-${operation}`, href: `design/activities/expression-tooling/${operation}`
        })) }] : []),
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
        { ...catalogActivity("write-line-v1", "Elsa.WriteLine", "Write line", "Primitives"),
          inputs: resultBindings ? [{ referenceKey: "message", name: "Message", displayName: "Message", typeName: "System.String", type: "System.String", isWrapped: true, defaultSyntax: "JavaScript" }] : [],
          outputs: resultBindings ? [{ referenceKey: "result", name: "Result", typeName: "System.String", type: "System.String" }] : [] }
      ]
    })
  }));
  await page.route("**/expressions/descriptors", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ items: resultBindings ? [{ type: "JavaScript", displayName: "JavaScript", editingMode: "text" }] : [] })
  }));
  if (resultBindings) await page.route("**/design/activities/expression-tooling/*", async route => {
    const request = route.request().postDataJSON() as { documentRevision: string };
    const validate = route.request().url().endsWith("/validate");
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ result: {
      state: validate ? "SupportedEmpty" : "Success", contractVersion: { major: 1, minor: 0 }, documentRevision: request.documentRevision,
      contextRevision: "activity-revision-3", payload: validate ? { diagnostics: [] } : {
        document: { workflowDraftId: "", owner: { kind: "activity-draft", id: "activity-graph-authoring-draft" }, nodeId: "later", propertyKey: "message", expressionType: "JavaScript", documentRevision: request.documentRevision },
        contextRevision: "activity-revision-3", symbolCatalogRevision: "activity-revision-3", capabilities: {}, rootSymbols: [{
          symbolId: "opaque:earlier", name: "first.Result", kind: "ActivityResult",
          parameterBinding: { kind: "ActivityResult", producerNodeId: "first", projectionKey: "result", isOptional: false }
        }]
      }
    } }) });
  });
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
