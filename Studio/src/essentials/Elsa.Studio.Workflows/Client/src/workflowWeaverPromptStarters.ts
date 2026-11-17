import type { StudioWeaverCapabilityContribution, StudioWeaverPromptStarterContribution } from "@elsa-workflows/studio-sdk";

// The demonstrated journeys: explanation (spec 1193 slice D), the layout change and one reviewed Operation Batch (slice C).
export const workflowWeaverCapabilities: StudioWeaverCapabilityContribution[] = [
  { id: "workflow.explain", displayName: "Explain workflow", description: "Explain the open workflow.", kind: "answer", risk: "read-only", surfaces: ["/workflows"] },
  { id: "workflow.propose-change", displayName: "Review workflow change", description: "Review an activity position change or one Operation Batch for the unsaved draft.", kind: "proposal", risk: "review-required", surfaces: ["/workflows"] }
];
export const workflowPromptStarters: StudioWeaverPromptStarterContribution[] = [
  { id: "workflow.explain.prompt", label: "Explain this workflow", prompt: "Explain the open workflow from its current working state and the designer's validation, and name the revision you read.", surfaces: ["/workflows"], requiredCapabilities: ["workflow.explain"] },
  { id: "workflow.layout.prompt", label: "Review a layout improvement", prompt: "Propose a small position change for an existing activity. Keep save and publish explicit.", surfaces: ["/workflows"], requiredCapabilities: ["workflow.propose-change"] },
  { id: "workflow.batch.prompt", label: "Change this draft", prompt: "Propose one reviewed change to the open draft using installed activities only. Ask me when an activity, input or target is unclear. Keep save and publish explicit.", surfaces: ["/workflows"], requiredCapabilities: ["workflow.propose-change"] }
];
