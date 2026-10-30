import type { StudioAgentCapabilityContribution, StudioAgentPromptStarterContribution } from "@elsa-workflows/studio-sdk";

// Advertise only the candidate's demonstrated read/layout journeys; broader authoring follows in M3.
export const workflowAgentCapabilities: StudioAgentCapabilityContribution[] = [
  { id: "workflow.explain", displayName: "Explain workflow", description: "Explain the open workflow.", kind: "answer", risk: "read-only", surfaces: ["/workflows"] },
  { id: "workflow.propose-change", displayName: "Review workflow layout", description: "Review an activity position change.", kind: "proposal", risk: "review-required", surfaces: ["/workflows"] }
];
export const workflowPromptStarters: StudioAgentPromptStarterContribution[] = [
  { id: "workflow.explain.prompt", label: "Explain this workflow", prompt: "Explain the open workflow using its admitted graph.", surfaces: ["/workflows"], requiredCapabilities: ["workflow.explain"] },
  { id: "workflow.layout.prompt", label: "Review a layout improvement", prompt: "Propose a small position change for an existing activity. Keep save and publish explicit.", surfaces: ["/workflows"], requiredCapabilities: ["workflow.propose-change"] }
];
