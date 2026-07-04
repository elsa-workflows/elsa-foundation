import type React from "react";
import type { Edge } from "@xyflow/react";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDefinitionVersionDetails, WorkflowDraft, WorkflowInstanceDetails, WorkflowTestRunView } from "../workflowTypes";
import type { getChildSlots, ScopeFrame, WorkflowEdgeData } from "../workflowAdapter";

export type CreateWorkflowKind = "sequence" | "flowchart";

export interface CreateWorkflowDraft {
  name: string;
  description: string;
  rootKind: CreateWorkflowKind;
  rootActivityVersionId?: string | null;
}

export const createWorkflowRootOptions: { value: CreateWorkflowKind; label: string; hint: string }[] = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];

export interface ActivityPaletteGroup {
  category: string;
  activities: ActivityCatalogItem[];
}

export type CanvasView = "designer" | "code" | "properties";

export type WorkflowEdge = Edge<WorkflowEdgeData>;

export type WorkflowEditorOperation = "idle" | "saving" | "promoting" | "testRunPreparing" | "testRunStarting";

export interface WorkflowTestRunState {
  draftSignature: string;
  view: WorkflowTestRunView;
}

export interface ExecutableRunState {
  artifactId: string;
  workflowExecutionId: string | null;
}

export type WorkflowConnectSource = { nodeId: string; handleId: string | null };

export type ConnectMenuState =
  | { kind: "fromPort"; sourceNodeId: string; sourceHandleId: string | null; clientX: number; clientY: number }
  | { kind: "spliceEdge"; edgeId: string; clientX: number; clientY: number }
  | { kind: "fromEmpty"; clientX: number; clientY: number };

export type WorkflowGraphConnection = { source: string; target: string; sourcePort?: string; targetPort?: string };

export type WorkflowNodeAvailabilityLookup = (input: { activityVersionId?: string | null; activityTypeKey?: string | null }) => ActivityAvailabilityDiagnosticEntry | null;

export interface WorkflowMetadataSuggestion {
  name?: string;
  description?: string;
}

export type WorkflowSidePanel = "palette" | "inspector";

export interface WorkflowEditorPanelTab {
  id: string;
  title: string;
  order: number;
  icon: React.ReactNode;
  render(): React.ReactNode;
}

export interface WorkflowDesignerPanelContext {
  definition: WorkflowDefinitionDetails["definition"];
  draft: WorkflowDraft;
  selectedActivity: ActivityNode | null;
  selectedActivityDescriptor: StudioActivityDescriptor | null;
  selectedActivitySlots: ReturnType<typeof getChildSlots>;
  catalog: ActivityCatalogItem[];
  currentScopeOwner: ActivityNode | null;
  frames: ScopeFrame[];
}

export type InstanceInspectorTab = "timeline" | "issues" | "details";

export interface WorkflowInstanceInspectionData {
  details: WorkflowInstanceDetails;
  definitionVersion: WorkflowDefinitionVersionDetails | null;
  definitionVersionError: string;
  activityCatalog: ActivityCatalogItem[];
}

declare global {
  interface Window {
    __ELSA_STUDIO_WORKFLOW_CONTEXT__?: {
      workflowId: string;
      workflowDefinitionId?: string;
      workflowVersionId?: string | null;
      draftId?: string | null;
      revision?: string | null;
      selectedNodeId?: string | null;
      selectedActivityType?: string | null;
      summary?: string;
      activities?: Array<{ id: string; type: string; displayName?: string }>;
      connections?: WorkflowGraphConnection[];
      diagnostics?: Array<{ severity: string; message: string }>;
    };
  }
}
