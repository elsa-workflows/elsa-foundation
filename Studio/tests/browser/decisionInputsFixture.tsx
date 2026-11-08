import React, { useEffect, useMemo, useState } from "react";
import { ActivityPropertiesPanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { builtInPropertyEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { createObjectExpressionEditorContribution } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/objectExpressionEditor";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { collectEvaluateDecisionValidation } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionDraftValidation";
import { createDecisionInputsEditorContribution } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionInputsEditor";
import { DecisionVersionEditor } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionVersionEditor";
import { decisionsCapability } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionsApi";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, ValidationError, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { ValidationPanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorPanels";
import "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";

const approvalXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="approval_definitions" name="Approval" namespace="urn:elsa:decisions"><decision id="approval" name="Approval"><decisionTable id="approval_table" hitPolicy="UNIQUE"><input id="approval_amount"><inputExpression id="approval_amount_expression" typeRef="number"><text>amount</text></inputExpression></input><input id="approval_category"><inputExpression id="approval_category_expression" typeRef="string"><text>customerCategory</text></inputExpression></input><output id="approval_result" label="result" typeRef="string"/><rule id="approval_rule"><inputEntry id="approval_c1"><text>&lt;= 1000</text></inputEntry><inputEntry id="approval_c2"><text>"Standard"</text></inputEntry><outputEntry id="approval_o1"><text>"Automatic"</text></outputEntry></rule></decisionTable></decision></definitions>`;

const scoreXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="score_definitions" name="Score" namespace="urn:elsa:decisions"><decision id="score" name="Score"><decisionTable id="score_table" hitPolicy="UNIQUE"><input id="score_input"><inputExpression id="score_expression" typeRef="number"><text>score</text></inputExpression></input><output id="score_output" label="result" typeRef="string"/><rule id="score_rule"><inputEntry id="score_c1"><text>-</text></inputEntry><outputEntry id="score_o1"><text>"Ok"</text></outputEntry></rule></decisionTable></decision></definitions>`;
const amountTextXml = approvalXml.replace('typeRef="number"', 'typeRef="string"');

const published = {
  id: "v1",
  assetId: "approval",
  number: 1,
  name: "Approval",
  xml: approvalXml,
  sourceHash: "hash1",
  publishedAt: "2026-11-05T10:00:00Z"
};

const textPublished = {
  id: "v2",
  assetId: "approval",
  number: 2,
  name: "Approval",
  xml: amountTextXml,
  sourceHash: "hash2",
  publishedAt: "2026-11-05T10:00:00Z"
};

const scorePublished = {
  id: "v1",
  assetId: "scoring",
  number: 1,
  name: "Scoring",
  xml: scoreXml,
  sourceHash: "hash-score",
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

const scoring = {
  ...asset,
  id: "scoring",
  name: "Scoring",
  xml: scoreXml,
  sourceHash: "hash-score",
  publishedVersionId: "v1"
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

function createApi(unpublished: boolean, fetchError: boolean): ElsaStudioModuleApi {
  const publishedAsset = unpublished ? asset : { ...asset, publishedVersionId: "v1" };
  const getJson = async (path: string): Promise<unknown> => {
    if (fetchError && (path.endsWith("/versions") || path.includes("/versions/"))) throw new Error("versions unavailable");
    if (path.endsWith("/decisions")) return { items: [publishedAsset, scoring] };
    if (path.endsWith("/versions/v2")) return textPublished;
    if (path.includes("/scoring/versions/v1") || (path.endsWith("/versions/v1") && path.includes("/scoring/"))) return scorePublished;
    if (path.endsWith("/versions/v1")) return published;
    if (path.endsWith("/versions") && path.includes("/scoring/")) return { items: [scorePublished] };
    if (path.endsWith("/versions")) return { items: unpublished ? [] : [published, textPublished] };
    if (path.endsWith("/scoring")) return scoring;
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
  const fetchError = search.get("fetchError") === "1";
  const api = useMemo(() => createApi(unpublished, fetchError), [unpublished, fetchError]);
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
  const [localErrors, setLocalErrors] = useState<ValidationError[]>([]);
  const versionEditor: StudioActivityPropertyEditorContribution = {
    id: "decisions.version",
    requiresBackend: decisionsCapability,
    order: 50,
    supportsValidationReporting: true,
    collectDraftValidation: next => collectEvaluateDecisionValidation(api, next),
    supports: descriptor => descriptor.uiHint === "decision-version",
    component: props => <DecisionVersionEditor {...props} api={api} />
  };
  const editors = [...builtInPropertyEditors, versionEditor];
  const expressionEditors: StudioExpressionEditorContribution[] = [
    ...(before ? [] : [createDecisionInputsEditorContribution(api)]),
    createObjectExpressionEditorContribution(() => editors)
  ];

  useEffect(() => {
    let active = true;
    void collectEvaluateDecisionValidation(api, activity).then(issues => {
      if (active) setLocalErrors(issues.map(issue => ({ type: issue.type, message: issue.message, path: issue.path })));
    });
    return () => { active = false; };
  }, [activity, api]);

  const draft = { validationErrors: [] } as unknown as WorkflowDraft;

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
      <ValidationPanel draft={draft} errors={localErrors} onRepair={() => undefined} />
    </main>
  );
}
