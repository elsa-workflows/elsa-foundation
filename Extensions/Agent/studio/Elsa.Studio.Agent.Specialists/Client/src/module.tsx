import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { HumanReviewPage } from "./HumanReviewPage";
import "./styles.css";

/**
 * Agents are user-defined; this module supplies the generic human review page that a workflow run links to.
 * It is reached from an exact pending occurrence, so it adds a route and no navigation entry or inbox.
 */
export function register(api: ElsaStudioModuleApi) {
  api.routes.add({
    id: "agent-review",
    requiresBackend: [{ id: "elsa.api.agent-reviews", contractVersion: "1" }],
    path: "/agents/reviews/:workflowExecutionId/:activityExecutionId",
    label: "Review",
    component: () => <HumanReviewPage context={api.backend} />
  });
}
