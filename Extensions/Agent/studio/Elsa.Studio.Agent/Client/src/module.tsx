import { Bot } from "lucide-react";
import React from "react";
import type { ElsaStudioModuleApi, StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import { AgentsPage } from "./AgentsPage";
import { HumanReviewPage } from "./HumanReviewPage";
import { AgentEditor } from "./AgentEditor";
import { AgentPlayground } from "./AgentPlayground";
import { AgentInvocationPage, invocationPath } from "./AgentInvocationPage";
import { agentProviderKey } from "./agentsApi";
import { continuitySchemaVersion, initialAgentManifest } from "./agentManifest";
import "./styles.css";

const activityDesign = [{ id: "elsa.api.activity-design", contractVersion: "1" }];
const agentAuthoring = [...activityDesign, { id: "elsa.api.agent", contractVersion: "1" }];
const invocationEvidence = [{ id: "elsa.api.agent-inspection", contractVersion: "1", relations: ["agent-invocation-evidence"] }];
// The playground runs drafts through the generic Test Run routes and reads the agent's evidence beside them.
const playground = [...activityDesign,
  { id: "elsa.api.publishing", contractVersion: "1", relations: ["activity-draft-test-run-dispatch", "activity-draft-test-run-cancel"] },
  { id: "elsa.api.agent-inspection", contractVersion: "1", relations: ["agent-test-run-evidence"] }];

function isInvocationView(data: unknown) {
  return typeof data === "object" && data !== null && (data as { invocation?: unknown }).invocation === true;
}

/** One form for both manifest schemas; schema "2" adds the explicit continuity choice. */
export const agentEditorContributions: StudioActivityDefinitionImplementationEditorContribution[] = ["1", continuitySchemaVersion].map(schemaVersion => ({
  id: `elsa.agent.schema-${schemaVersion}`,
  providerKey: agentProviderKey,
  providerSchemaVersion: schemaVersion,
  requiresBackend: agentAuthoring,
  createInitialImplementation: () => ({
    payload: { ...initialAgentManifest(), ...(schemaVersion === continuitySchemaVersion ? { allowContinuity: false } : {}) },
    layout: []
  }),
  focusDiagnosticLocation: ({ location, editorElement }) => {
    const fields: Record<string, string> = {
      "/instructions": "instructions", "/model": "model", "/model/providerKey": "model", "/model/connectionName": "model", "/model/modelId": "model",
      "/settings": "model-group", "/settings/temperature": "temperature", "/limits": "calls", "/limits/maximumModelCalls": "calls",
      "/limits/timeoutMilliseconds": "timeout", "/limits/maximumOutputTokens": "tokens"
    };
    const field = fields[location.jsonPointer ?? ""];
    let target = field ? editorElement.querySelector<HTMLElement>(`[data-agent-field="${field}"]`) : null;
    if (!target && location.jsonPointer === "/settings") target = editorElement.querySelector<HTMLElement>("[data-agent-field='model-group']");
    if (!target) return { kind: "unsupported", announcement: "Return to Designer and review the agent settings described by this issue." };
    const disclosure = target.closest<HTMLDetailsElement>("details");
    if (disclosure) disclosure.open = true;
    if (target.matches(":disabled")) {
      target = target.closest<HTMLElement>(".studio-field") ?? target.parentElement ?? target;
      target.tabIndex = -1;
    }
    target.focus();
    target.scrollIntoView?.({ block: "center" });
    return { kind: "focused", announcement: "Focused the agent setting that needs attention." };
  },
  component: AgentEditor
}));

/**
 * Agents are user-defined. This module lists them over the generic activity definitions API, contributes their
 * implementation form to the generic activity editor, and supplies the human review page a workflow run links to.
 */
export function register(api: ElsaStudioModuleApi) {
  api.featureAreas.add({
    id: "agents", title: "Agents", navGroup: "Workspace", order: 25,
    description: "Define agents with a model, instructions and typed inputs and results, and use them in workflows.",
    ownedPaths: ["/agents"], required: false, defaultEnabled: true,
    nav: { icon: Bot, title: "Agents", path: "/agents", placement: "build", order: 25, requiresBackend: activityDesign },
    routes: [
      { id: "agents", path: "/agents", label: "Agents", requiresBackend: activityDesign,
        component: ({ navigate }) => <AgentsPage context={api.backend} navigate={navigate} /> }
    ]
  });
  for (const contribution of agentEditorContributions) api.activityEditors.add(contribution);
  api.routes.add({
    id: "agent-playground",
    requiresBackend: playground,
    path: "/agents/drafts/:draftId/playground",
    label: "Playground",
    component: ({ navigate }) => <AgentPlayground context={api.backend} navigate={navigate} />
  });
  api.routes.add({
    id: "agent-invocation",
    requiresBackend: invocationEvidence,
    path: "/agents/invocations/:workflowExecutionId/:activityExecutionId",
    label: "Agent invocation",
    component: ({ navigate }) => <AgentInvocationPage context={api.backend} navigate={navigate} />
  });
  // Agents are user-defined, so their activity types are not known here: any selected occurrence whose runtime state view
  // names it an agent invocation links to that invocation's evidence page, which re-authorizes its own reads.
  api.workflowRunRelatedLinks?.add({
    id: "agent-invocation", order: 100, requiresBackend: invocationEvidence,
    activityTypes: [], anyActivityType: true, viewKind: "elsa.agent.invocation", viewVersion: 1,
    getLinks: context => isInvocationView(context.stateView.data)
      ? [{ id: "invocation", label: "Agent invocation", path: invocationPath(context) }]
      : []
  });
  // One exact wait addressed by URL.
  api.routes.add({
    id: "agent-review",
    requiresBackend: [{ id: "elsa.api.agent-reviews", contractVersion: "1" }],
    path: "/agents/reviews/:workflowExecutionId/:activityExecutionId",
    label: "Review",
    component: () => <HumanReviewPage context={api.backend} />
  });
}
