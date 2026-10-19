import React, { useMemo, useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type {
  StudioActivityDefinitionImplementationEditorProps,
  StudioAiContributionApi,
  StudioEndpointContext,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import { ActivityGraphImplementationEditor } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityGraphImplementationEditor";
import { WorkflowEditor } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor";
import {
  flowchartActivity,
  flowchartNode,
  sequenceActivity,
  sequenceNode,
  forEachActivity,
  forEachNode,
  leafNode,
  writeLine
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/fixtures";
import { JavaScriptExpandedEditor, JavaScriptInlineEditor } from "../../../Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/module";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });

const writeLineCatalog: ActivityCatalogItem = {
  ...writeLine,
  inputs: [{
    referenceKey: "text",
    name: "Text",
    typeName: "System.String",
    displayName: new URLSearchParams(window.location.search).has("long-label")
      ? "Message to include when notifying the customer about their completed order"
      : "Text",
    description: "Text to write.",
    order: 0,
    category: "General",
    isBrowsable: true,
    uiHint: "singleline",
    isWrapped: true,
    defaultSyntax: "Literal"
  }],
  ports: [{ name: "Done", displayName: "Done", isBrowsable: true }],
  available: true
};

const notifyBuyer: ActivityNode = {
  ...leafNode("write-one"),
  inputs: [{
    referenceKey: "text",
    value: { value: "buyer.email", expressionType: "JavaScript" }
  }]
};

function editorDraft(): WorkflowDraft {
  const sequence = new URLSearchParams(window.location.search).get("composition") === "sequence";
  const root = sequence
    ? sequenceRootActivity()
    : flowchartNode("flow-root", [notifyBuyer, leafNode("write-two")]);
  if (!sequence) root.structure!.payload.connections = [{
    id: "flow-write-one-write-two",
    source: { nodeId: "write-one", port: "Done" },
    target: { nodeId: "write-two" }
  }];
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { variables: [], rootActivity: root, inputs: [], outputs: [] },
    layout: [
      { nodeId: "write-one", x: 420, y: 180 },
      { nodeId: "write-two", x: 720, y: 180 }
    ],
    activityPresentation: [{ nodeId: "write-one", displayName: "Notify buyer" }],
    validationErrors: []
  };
}

function sequenceRootActivity(): ActivityNode {
  return sequenceNode("sequence-root", [
    notifyBuyer,
    forEachNode("loop", sequenceNode("body", [leafNode("nested")])),
    leafNode("write-two")
  ]);
}

function activityGraphSequenceValue(): StudioActivityDefinitionImplementationEditorProps["value"] {
  return {
    payload: {
      rootActivity: sequenceRootActivity(),
      variables: [],
      outputMappings: [],
      outcomeMappings: []
    },
    layout: []
  };
}

const expressionEditors: StudioExpressionEditorContribution[] = [{
  id: "browser.javascript-expression-editor",
  supports: context => context.syntax === "JavaScript",
  surfaces: { inline: JavaScriptInlineEditor, expanded: JavaScriptExpandedEditor }
}];

export function ActivityDuplicateEditorFixture() {
  const context = useMemo(() => createEditorContext(), []);
  const activityHost = new URLSearchParams(window.location.search).get("host") === "activity";
  const ai = useMemo(() => ({
    promptActions: { list: () => [] },
    providerAvailability: { get: () => false, subscribe: () => () => undefined },
    dispatchPrompt: () => undefined
  } as unknown as StudioAiContributionApi), []);

  return (
    <div className="activity-duplicate-editor" data-testid="activity-duplicate-editor">
      <QueryClientProvider client={queryClient}>
        {activityHost
          ? <ControlledActivityGraphFixture context={context} />
          : <WorkflowEditor
              context={context}
              definitionId="definition-1"
              ai={ai}
              propertyEditors={[]}
              expressionEditors={expressionEditors}
              runInputEditors={[]}
              workflowDesignerPanels={[]}
              autosaveEnabledByDefault={false}
              onBack={() => undefined}
            />}
      </QueryClientProvider>
    </div>
  );
}

function ControlledActivityGraphFixture({ context }: { context: StudioEndpointContext }) {
  const [value, setValue] = useState(activityGraphSequenceValue);
  return <ActivityGraphImplementationEditor
    context={context}
    definitionId="definition-1"
    draftId="draft-1"
    revision={1}
    providerKey="elsa.activity-graph"
    providerSchemaVersion="2"
    manifestFingerprint="sha256:browser-activity-duplicate"
    contract={{ contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] }}
    propertyEditors={[]}
    expressionEditors={expressionEditors}
    graphAuthoringPanels={[]}
    historyResetKey="draft-1:activity-sequence"
    value={value}
    readOnly={false}
    onChange={setValue}
  />;
}

function createEditorContext(): StudioEndpointContext {
  const draft = editorDraft();
  return {
    baseUrl: "browser-activity-duplicate-editor",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") {
          return {
            capabilities: [
              {
                id: "elsa.api.workflow-design",
                contractVersion: "1",
                links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
              },
              {
                id: "elsa.api.activity-design",
                contractVersion: "1",
                links: [{ rel: "activity-catalog", href: "design/activities/catalog" }]
              },
              {
                id: "elsa.api.expressions",
                contractVersion: "1",
                links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }]
              }
            ]
          };
        }
        if (url === "/design/workflows/definitions/definition-1") {
          return {
            definition: {
              id: "definition-1",
              name: "Order confirmation",
              description: "Notifies the buyer.",
              createdAt: "2026-10-14T00:00:00Z",
              lastModifiedAt: "2026-10-14T00:00:00Z",
              deletedAt: null,
              draftId: "draft-1",
              latestVersionId: "version-1",
              latestVersion: "1.0.0",
              versionCount: 1
            },
            draft,
            versions: []
          };
        }
        if (url === "/design/activities/catalog") {
          return { activities: [writeLineCatalog, flowchartActivity, sequenceActivity, forEachActivity] };
        }
        if (url === "/expressions/descriptors") {
          return {
            items: [
              { type: "Literal", displayName: "Literal", editingMode: "literal" },
              { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
            ]
          };
        }
        throw new Error(`Unexpected editor fixture request: ${url}`);
      },
      postJson: async () => ({}),
      putJson: async () => draft,
      requestJson: async () => ({}),
      deleteJson: async () => undefined
    }
  } as unknown as StudioEndpointContext;
}
