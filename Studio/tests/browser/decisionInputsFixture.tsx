import React, { useMemo, useState } from "react";
import { ActivityPropertiesPanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { builtInPropertyEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { createObjectExpressionEditorContribution } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/objectExpressionEditor";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { createDecisionInputsEditorContribution } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionInputsEditor";
import { DecisionVersionEditor } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionVersionEditor";
import { decisionsCapability } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionsApi";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";

const approvalXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="approval_definitions" name="Approval" namespace="urn:elsa:decisions"><decision id="approval" name="Approval"><decisionTable id="approval_table" hitPolicy="UNIQUE"><input id="approval_amount"><inputExpression id="approval_amount_expression" typeRef="number"><text>amount</text></inputExpression></input><input id="approval_category"><inputExpression id="approval_category_expression" typeRef="string"><text>customerCategory</text></inputExpression></input><output id="approval_result" label="result" typeRef="string"/><rule id="approval_rule"><inputEntry id="approval_c1"><text>&lt;= 1000</text></inputEntry><inputEntry id="approval_c2"><text>"Standard"</text></inputEntry><outputEntry id="approval_o1"><text>"Automatic"</text></outputEntry></rule></decisionTable></decision></definitions>`;

const published = {
  id: "v1",
  assetId: "approval",
  number: 1,
  name: "Approval",
  xml: approvalXml,
  sourceHash: "hash1",
  publishedAt: "2026-11-05T10:00:00Z"
};

const asset = {
  id: "approval",
  name: "Approval",
  description: "Purchase approval",
  revision: "r1",
  sourceHash: "hash1",
  xml: approvalXml,
  isSensitive: false,
  createdAt: "2026-11-05T10:00:00Z",
  updatedAt: "2026-11-05T10:00:00Z"
};

const descriptor: StudioActivityDescriptor = {
  typeName: "Elsa.Decisions.Workflows.Runtime.Activities.EvaluateDecision",
  displayName: "Evaluate Decision",
  inputs: [
    {
      name: "Decision",
      referenceKey: "Decision",
      displayName: "Decision",
      typeName: "System.String",
      isWrapped: true,
      isRequired: true,
      uiHint: "decision-version",
      defaultSyntax: "Literal",
      tier: "essential"
    },
    {
      name: "Inputs",
      referenceKey: "Inputs",
      displayName: "Inputs",
      typeName: "Elsa.Decisions.Core.DecisionValue",
      isWrapped: true,
      collectionKind: "Dictionary",
      defaultSyntax: "Object",
      defaultValue: "{}",
      tier: "essential"
    },
    {
      name: "OperationBudget",
      referenceKey: "OperationBudget",
      displayName: "Operation Budget",
      typeName: "System.Int32",
      isWrapped: true,
      defaultSyntax: "Literal",
      defaultValue: "1000000",
      tier: "optional"
    }
  ],
  outputs: [],
  ports: []
};

const expressionDescriptors = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" as const },
  { type: "Object", displayName: "Object", editingMode: "structured" as const },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" as const },
  { type: "Variable", displayName: "Variable", editingMode: "reference" as const }
];

function createApi(unpublished: boolean): ElsaStudioModuleApi {
  const publishedAsset = unpublished ? asset : { ...asset, publishedVersionId: "v1" };
  const getJson = async (path: string): Promise<unknown> => {
    if (path.endsWith("/decisions")) return { items: [publishedAsset] };
    if (path.endsWith("/versions/v1")) return published;
    if (path.endsWith("/versions")) return { items: unpublished ? [] : [published] };
    if (path.endsWith("/approval")) return publishedAsset;
    return publishedAsset;
  };
  return {
    backend: {
      baseUrl: window.location.origin,
      http: { getJson, postJson: async () => ({}), putJson: async () => ({}), deleteJson: async () => undefined }
    },
    backendSession: {
      capabilities: {
        capabilities: [{
          id: "elsa.api.decisions",
          contractVersion: "1",
          links: [{ rel: "decisions", href: "/elsa/api/decisions" }]
        }]
      },
      identity: {
        status: "authenticated",
        subject: "user",
        tenantId: "tenant",
        roles: [],
        permissions: ["decisions:read", "decisions:manage", "decisions:publish"]
      }
    },
    runtime: { identity: { subject: "user", tenantId: "tenant" } }
  } as unknown as ElsaStudioModuleApi;
}

export function DecisionInputsFixture() {
  const search = new URLSearchParams(window.location.search);
  const before = search.get("before") === "1";
  const unpublished = search.get("unpublished") === "1";
  const api = useMemo(() => createApi(unpublished), [unpublished]);
  const [activity, setActivity] = useState<ActivityNode>({
    nodeId: "evaluatedecision-browser-node",
    activityVersionId: "evaluatedecision-browser-v1",
    inputs: [],
    outputs: [],
    Decision: {
      typeName: "System.String",
      expression: { type: "Literal", value: unpublished ? "approval@latest" : "approval@v1" }
    },
    Inputs: {
      typeName: "Elsa.Decisions.Core.DecisionValue",
      expression: { type: "Object", value: {} }
    },
    OperationBudget: {
      typeName: "System.Int32",
      expression: { type: "Literal", value: "1000000" }
    }
  });
  const versionEditor: StudioActivityPropertyEditorContribution = {
    id: "decisions.version",
    requiresBackend: decisionsCapability,
    order: 50,
    supportsValidationReporting: true,
    supports: descriptor => descriptor.uiHint === "decision-version",
    component: props => <DecisionVersionEditor {...props} api={api} />
  };
  const editors = [...builtInPropertyEditors, versionEditor];
  const expressionEditors: StudioExpressionEditorContribution[] = [
    ...(before ? [] : [createDecisionInputsEditorContribution(api)]),
    createObjectExpressionEditorContribution(() => editors)
  ];

  return (
    <main style={{ minHeight: "100vh", padding: 16, background: "var(--studio-app-bg)", color: "var(--studio-text)" }}>
      <aside
        className="wf-inspector"
        aria-label="Activity inspector"
        style={{
          width: 520,
          maxWidth: "100%",
          boxSizing: "border-box",
          height: "calc(100vh - 32px)",
          overflowY: "auto",
          marginLeft: "auto",
          border: "1px solid var(--studio-border)",
          borderRadius: 8,
          background: "var(--studio-surface)",
          color: "var(--studio-text)",
          padding: 16
        }}
      >
        <h1 style={{ marginTop: 0, fontSize: 18 }}>Evaluate Decision</h1>
        <ActivityPropertiesPanel
          activity={activity}
          descriptor={descriptor}
          editors={editors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          visibleVariables={[]}
          scopeStatus="ready"
          onChange={setActivity}
        />
        <output data-testid="authored-state" style={{ display: "none" }}>{JSON.stringify(activity)}</output>
      </aside>
    </main>
  );
}
