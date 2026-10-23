import type { StudioAgentCapabilityContribution, StudioAgentPromptStarterContribution } from "../../sdk";

export const workflowAgentCapabilities: StudioAgentCapabilityContribution[] = [
  {
    id: "workflow.explain",
    displayName: "Explain workflow",
    description: "Explain the active workflow in business terms.",
    kind: "answer",
    risk: "read-only",
    surfaces: ["/workflows"]
  },
  {
    id: "workflow.troubleshoot",
    displayName: "Troubleshoot workflow",
    description: "Prioritize active workflow validation and runtime issues.",
    kind: "answer",
    risk: "read-only",
    surfaces: ["/workflows"]
  },
  {
    id: "workflow.propose-change",
    displayName: "Propose workflow change",
    description: "Prepare a reviewable draft workflow change proposal.",
    kind: "proposal",
    risk: "review-required",
    surfaces: ["/workflows"]
  }
];

export const workflowPromptStarters: StudioAgentPromptStarterContribution[] = [
  {
    id: "workflow.explain.prompt",
    label: "Explain this workflow",
    prompt: "What does this workflow do? Explain it in business terms and call out unclear branches.",
    surfaces: ["/workflows"],
    order: 10,
    requiredCapabilities: ["workflow.explain"]
  },
  {
    id: "workflow.troubleshoot.prompt",
    label: "Troubleshoot active workflow",
    prompt: "Review this workflow's validation and runtime diagnostics. What should I fix first?",
    surfaces: ["/workflows"],
    order: 20,
    requiredCapabilities: ["workflow.troubleshoot"]
  },
  {
    id: "workflow.proposal.prompt",
    label: "Propose stalled-approval handling",
    prompt: "How should I handle stalled approvals? Create a reviewable proposal if a workflow change is needed.",
    surfaces: ["/workflows"],
    order: 30,
    requiredCapabilities: ["workflow.propose-change"]
  }
];
