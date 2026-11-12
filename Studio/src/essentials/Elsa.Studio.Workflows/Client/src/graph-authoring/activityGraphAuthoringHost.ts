import type { StudioActivityDefinitionImplementationState } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView } from "../activityDefinitionTypes";
import type { AuthoringCheckpointCapture, AuthoringCheckpointCoordinator, AuthoringCheckpointIntent, AuthoringTransitionOutcome } from "./authoringCheckpoint";

/** Workflows-owned parent port. Optional native renderers receive a narrower session port. */
export interface ActivityGraphAuthoringHost {
  getResource(): AuthoringCheckpointCoordinator<ActivityDefinitionDraftView>;
  readLive(): AuthoringCheckpointCapture<StudioActivityDefinitionImplementationState>;
  canEdit(): boolean;
  probe(): boolean;
  invalidateScope(): void;
  notifyWorkChanged(): void;
  transition(intent: AuthoringCheckpointIntent, action: () => undefined): Promise<AuthoringTransitionOutcome>;
}

export const activityGraphAuthoringProtocol = { id: "elsa.workflows.activity-graph-authoring", version: 1 } as const;
