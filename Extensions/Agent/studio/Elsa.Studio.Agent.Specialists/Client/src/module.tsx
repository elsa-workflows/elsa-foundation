import React from "react";
import type { ElsaStudioModuleApi, StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import { AgentsPage } from "./AgentsPage";
import { HumanReviewPage } from "./HumanReviewPage";
import { SpecialistEditor } from "./SpecialistEditor";
import { specialistProviderKey } from "./agentsApi";
import { continuitySchemaVersion, initialSpecialistManifest } from "./specialistManifest";
import "./styles.css";

const activityDesign = [{ id: "elsa.api.activity-design", contractVersion: "1" }];
const specialistAuthoring = [...activityDesign, { id: "elsa.api.agent-specialists", contractVersion: "1" }];

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
    nav: { title: "Agents", path: "/agents", order: 25, requiresBackend: activityDesign },
    routes: [
      { id: "agents", path: "/agents", label: "Agents", requiresBackend: activityDesign,
        component: ({ navigate }) => <AgentsPage context={api.backend} navigate={navigate} /> }
    ]
  });
  for (const contribution of specialistEditorContributions) api.activityEditors.add(contribution);
  // One exact wait addressed by URL; the occurrence link from run inspection is T034.
  api.routes.add({
    id: "agent-review",
    requiresBackend: [{ id: "elsa.api.agent-reviews", contractVersion: "1" }],
    path: "/agents/reviews/:workflowExecutionId/:activityExecutionId",
    label: "Review",
    component: () => <HumanReviewPage context={api.backend} />
  });
}
