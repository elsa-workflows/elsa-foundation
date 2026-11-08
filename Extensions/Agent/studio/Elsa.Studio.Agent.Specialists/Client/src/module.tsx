import { Bot } from "lucide-react";
import React from "react";
import type { ElsaStudioModuleApi, StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import { AgentsPage } from "./AgentsPage";
import { HumanReviewPage } from "./HumanReviewPage";
import { SpecialistEditor } from "./SpecialistEditor";
import { SpecialistPlayground } from "./SpecialistPlayground";
import { SpecialistInvocationPage, invocationPath } from "./SpecialistInvocationPage";
import { specialistProviderKey } from "./agentsApi";
import { continuitySchemaVersion, initialSpecialistManifest } from "./specialistManifest";
import "./styles.css";

const activityDesign = [{ id: "elsa.api.activity-design", contractVersion: "1" }];
const specialistAuthoring = [...activityDesign, { id: "elsa.api.agent-specialists", contractVersion: "1" }];
const invocationEvidence = [{ id: "elsa.api.agent-specialist-inspection", contractVersion: "1", relations: ["agent-specialist-invocation-evidence"] }];
// The playground runs drafts through the generic Test Run routes and reads the agent's evidence beside them.
const playground = [...activityDesign,
  { id: "elsa.api.publishing", contractVersion: "1", relations: ["activity-draft-test-run-dispatch", "activity-draft-test-run-cancel"] },
  { id: "elsa.api.agent-specialist-inspection", contractVersion: "1", relations: ["agent-specialist-test-run-evidence"] }];

function isInvocationView(data: unknown) {
  return typeof data === "object" && data !== null && (data as { invocation?: unknown }).invocation === true;
}

/** One form for both manifest schemas; schema "2" adds the explicit continuity choice. */
export const specialistEditorContributions: StudioActivityDefinitionImplementationEditorContribution[] = ["1", continuitySchemaVersion].map(schemaVersion => ({
  id: `elsa.specialist.schema-${schemaVersion}`,
  providerKey: specialistProviderKey,
  providerSchemaVersion: schemaVersion,
  requiresBackend: specialistAuthoring,
  createInitialImplementation: () => ({
    payload: { ...initialSpecialistManifest(), ...(schemaVersion === continuitySchemaVersion ? { allowContinuity: false } : {}) },
    layout: []
  }),
  component: SpecialistEditor
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
  for (const contribution of specialistEditorContributions) api.activityEditors.add(contribution);
  api.routes.add({
    id: "agent-playground",
    requiresBackend: playground,
    path: "/agents/drafts/:draftId/playground",
    label: "Playground",
    component: ({ navigate }) => <SpecialistPlayground context={api.backend} navigate={navigate} />
  });
  api.routes.add({
    id: "agent-invocation",
    requiresBackend: invocationEvidence,
    path: "/agents/invocations/:workflowExecutionId/:activityExecutionId",
    label: "Agent invocation",
    component: ({ navigate }) => <SpecialistInvocationPage context={api.backend} navigate={navigate} />
  });
  // Agents are user-defined, so their activity types are not known here: any selected occurrence whose runtime state view
  // names it an agent invocation links to that invocation's evidence page, which re-authorizes its own reads.
  api.workflowRunRelatedLinks?.add({
    id: "agent-invocation", order: 100, requiresBackend: invocationEvidence,
    activityTypes: [], anyActivityType: true, viewKind: "elsa.specialist.invocation", viewVersion: 1,
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
