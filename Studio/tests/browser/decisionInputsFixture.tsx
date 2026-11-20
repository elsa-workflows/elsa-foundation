import React, { useCallback, useMemo, useRef, useState } from "react";
import { ActivityPropertiesPanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import { builtInPropertyEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { createObjectExpressionEditorContribution } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/objectExpressionEditor";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { collectEvaluateDecisionValidation } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionDraftValidation";
import { createDecisionInputsEditorContribution } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionInputsEditor";
import { DecisionVersionEditor } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionVersionEditor";
import { decisionsCapability } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionsApi";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { collectContributionDraftValidation, contributionDraftValidationCollectors } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/contributionDraftValidation";
import { useContributionDraftValidation } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/useContributionDraftValidation";
import { withLocalValidationErrors } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/publicationReview";
import { ValidationPanel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorPanels";
import "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";

const approvalXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="approval_definitions" name="Approval" namespace="urn:elsa:decisions"><decision id="approval" name="Approval"><decisionTable id="approval_table" hitPolicy="UNIQUE"><input id="approval_amount"><inputExpression id="approval_amount_expression" typeRef="number"><text>amount</text></inputExpression></input><input id="approval_category"><inputExpression id="approval_category_expression" typeRef="string"><text>customerCategory</text></inputExpression></input><output id="approval_result" label="result" typeRef="string"/><rule id="approval_rule"><inputEntry id="approval_c1"><text>&lt;= 1000</text></inputEntry><inputEntry id="approval_c2"><text>"Standard"</text></inputEntry><outputEntry id="approval_o1"><text>"Automatic"</text></outputEntry></rule></decisionTable></decision></definitions>`;

const scoreXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="score_definitions" name="Score" namespace="urn:elsa:decisions"><decision id="score" name="Score"><decisionTable id="score_table" hitPolicy="UNIQUE"><input id="score_input"><inputExpression id="score_expression" typeRef="number"><text>score</text></inputExpression></input><output id="score_output" label="result" typeRef="string"/><rule id="score_rule"><inputEntry id="score_c1"><text>-</text></inputEntry><outputEntry id="score_o1"><text>"Ok"</text></outputEntry></rule></decisionTable></decision></definitions>`;
const amountTextXml = approvalXml.replace('typeRef="number"', 'typeRef="string"');
const regionXml = approvalXml
  .replace(
    '<output id="approval_result"',
    '<input id="approval_region"><inputExpression id="approval_region_expression" typeRef="string"><text>region</text></inputExpression></input><output id="approval_result"'
  )
  .replace(
    '<outputEntry id="approval_o1"',
    '<inputEntry id="approval_c3"><text>-</text></inputEntry><outputEntry id="approval_o1"'
  );

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

type BackendState = {
  unpublished: boolean;
  fetchError: boolean;
  latestAddsRegion: boolean;
  failOnce: boolean;
};

function createApi(state: BackendState): ElsaStudioModuleApi {
  const getJson = async (path: string): Promise<unknown> => {
    if (state.failOnce && /\/versions\/[^/]+$/.test(path)) {
      throw new Error("temporary lookup failed");
    }
    const regionPublished = {
      id: "v3",
      assetId: "approval",
      number: 3,
      name: "Approval",
      xml: regionXml,
      sourceHash: "hash-region",
      publishedAt: "2026-11-05T10:00:00Z"
    };
    const publishedVersions = state.unpublished
      ? []
      : [published, textPublished, ...(state.latestAddsRegion ? [regionPublished] : [])];
    const publishedAsset = state.unpublished
      ? asset
      : {
        ...asset,
        publishedVersionId: state.latestAddsRegion ? "v3" : "v1",
        xml: state.latestAddsRegion ? regionXml : asset.xml
      };
    if (state.fetchError && (path.endsWith("/versions") || path.includes("/versions/"))) throw new Error("versions unavailable");
    if (path.endsWith("/decisions")) return { items: [publishedAsset, scoring] };
    if (path.endsWith("/versions/v3")) return regionPublished;
    if (path.endsWith("/versions/v2")) return textPublished;
    if (path.includes("/scoring/versions/v1") || (path.endsWith("/versions/v1") && path.includes("/scoring/"))) return scorePublished;
    if (path.endsWith("/versions/v1")) return published;
    if (path.endsWith("/versions") && path.includes("/scoring/")) return { items: [scorePublished] };
    if (path.endsWith("/versions")) return { items: publishedVersions };
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

const notifyDescriptor: StudioActivityDescriptor = {
  typeName: "Contoso.Notify",
  displayName: "Notify",
  inputs: [
    {
      name: "Decision",
      referenceKey: "Decision",
      displayName: "Decision",
      typeName: "System.String",
      isWrapped: true,
      defaultSyntax: "Literal",
      tier: "essential"
    }
  ],
  outputs: [],
  ports: []
};

function catalogItem(activity: ActivityNode, activityDescriptor: StudioActivityDescriptor): ActivityCatalogItem {
  return {
    activityVersionId: activity.activityVersionId,
    activityTypeKey: activityDescriptor.typeName,
    version: "1.0.0",
    category: "Test",
    displayName: activityDescriptor.displayName ?? activityDescriptor.typeName,
    executionType: "Action",
    inputs: activityDescriptor.inputs,
    outputs: []
  };
}

export function DecisionInputsFixture() {
  const search = new URLSearchParams(window.location.search);
  const before = search.get("before") === "1";
  const unrelated = search.get("unrelated") === "1";
  const backend = useRef<BackendState>({
    unpublished: search.get("unpublished") === "1",
    fetchError: search.get("fetchError") === "1",
    latestAddsRegion: search.get("latestRegion") === "1",
    failOnce: search.get("failOnce") === "1"
  }).current;
  const [, setBackendEpoch] = useState(0);
  const [editorKey, setEditorKey] = useState(0);
  const bumpBackend = () => setBackendEpoch(value => value + 1);
  const api = useMemo(() => createApi(backend), [backend]);
  const activityDescriptor = unrelated ? notifyDescriptor : descriptor;
  const [activity, setActivity] = useState<ActivityNode>(() => unrelated
    ? {
      nodeId: "notify-browser-node",
      activityVersionId: "notify-browser-v1",
      inputs: [],
      outputs: [],
      Decision: {
        typeName: "System.String",
        expression: { type: "Literal", value: "ops@example.com" }
      }
    }
    : {
      nodeId: "evaluatedecision-browser-node",
      activityVersionId: "evaluatedecision-browser-v1",
      inputs: [],
      outputs: [],
      Decision: {
        typeName: "System.String",
        expression: { type: "Literal", value: backend.unpublished || backend.latestAddsRegion ? "approval@latest" : "approval@v1" }
      },
      Inputs: {
        typeName: "Elsa.Decisions.Core.DecisionValue",
        expression: {
          type: "Object",
          value: backend.failOnce
            ? { amount: { kind: "number", value: "950" }, customerCategory: { kind: "string", value: "Standard" } }
            : {}
        }
      },
      OperationBudget: {
        typeName: "System.Int32",
        expression: { type: "Literal", value: "1000000" }
      }
    });
  const [review, setReview] = useState<{ phase: string; executableStatus: string; validationErrors: string[] } | null>(null);
  const versionEditor: StudioActivityPropertyEditorContribution = useMemo(() => ({
    id: "decisions.version",
    requiresBackend: decisionsCapability,
    order: 50,
    supportsValidationReporting: true,
    collectDraftValidation: (next, context) => collectEvaluateDecisionValidation(api, next, context),
    supports: next => next.uiHint === "decision-version",
    component: props => <DecisionVersionEditor {...props} api={api} />
  }), [api]);
  const editors = useMemo(() => [...builtInPropertyEditors, versionEditor], [versionEditor]);
  const expressionEditors: StudioExpressionEditorContribution[] = useMemo(() => [
    ...(before ? [] : [createDecisionInputsEditorContribution(api)]),
    createObjectExpressionEditorContribution(() => editors)
  ], [api, before, editors]);
  const collectors = useMemo(
    () => contributionDraftValidationCollectors([versionEditor], []),
    [versionEditor]
  );
  const catalogByVersion = useMemo(
    () => new Map([[activity.activityVersionId, catalogItem(activity, activityDescriptor)]]),
    [activity.activityVersionId, activityDescriptor]
  );

  const collectIssues = useCallback(async (next: ActivityNode) => {
    return collectContributionDraftValidation(next, collectors, catalogByVersion);
  }, [catalogByVersion, collectors]);
  const { errors: localErrors } = useContributionDraftValidation(activity, [versionEditor], [], catalogByVersion);

  const openReview = async () => {
    const errors = await collectIssues(activity);
    const next = withLocalValidationErrors({
      phase: "review",
      executableStatus: "ready",
      validationErrors: [],
      validationWarnings: []
    } as never, errors);
    setReview({
      phase: next.phase,
      executableStatus: next.executableStatus,
      validationErrors: next.validationErrors
    });
  };

  const draft = { validationErrors: [] } as unknown as WorkflowDraft;

  return (
    <main className="wf-tokens" style={{ minHeight: "100vh", padding: 16, background: "var(--studio-app-bg)", color: "var(--studio-text)" }}>
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
        <h1 style={{ marginTop: 0, fontSize: 18 }}>{activityDescriptor.displayName}</h1>
        <ActivityPropertiesPanel
          key={editorKey}
          activity={activity}
          descriptor={activityDescriptor}
          editors={editors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus="ready"
          descriptorStatus="ready"
          visibleVariables={[]}
          scopeStatus="ready"
          onChange={setActivity}
        />
        <div className="wf-fixture-actions" style={{ display: "flex", flexWrap: "wrap", gap: 8, marginTop: 16 }}>
          <button type="button" onClick={() => { backend.unpublished = false; bumpBackend(); }}>Publish decision</button>
          <button type="button" onClick={() => { backend.latestAddsRegion = true; backend.unpublished = false; bumpBackend(); }}>Publish table with region</button>
          <button type="button" onClick={() => { backend.fetchError = false; backend.failOnce = false; bumpBackend(); }}>Recover lookup</button>
          <button type="button" onClick={() => void openReview()}>Review & publish</button>
          <button type="button" onClick={() => setEditorKey(key => key + 1)}>Remount inspector</button>
        </div>
        <output data-testid="authored-state" style={{ display: "none" }}>{JSON.stringify(activity)}</output>
        <output data-testid="backend-state" style={{ display: "none" }}>{JSON.stringify({
          unpublished: backend.unpublished,
          latestAddsRegion: backend.latestAddsRegion,
          failOnce: backend.failOnce
        })}</output>
      </aside>
      <ValidationPanel draft={draft} errors={localErrors} onRepair={() => undefined} />
      {review ? (
        <div role="dialog" aria-label="Review and publish">
          <h2>Review and publish</h2>
          <p data-testid="review-phase">{review.phase}</p>
          <p data-testid="review-status">{review.executableStatus}</p>
          {review.validationErrors.map(message => <p key={message}>{message}</p>)}
          <button type="button" disabled={review.phase === "validationBlocked" || review.executableStatus === "blocked"}>Publish</button>
          <button type="button" onClick={() => setReview(null)}>Cancel</button>
        </div>
      ) : null}
    </main>
  );
}
