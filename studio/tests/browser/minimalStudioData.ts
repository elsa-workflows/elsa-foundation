export const minimalWorkflowNames = ["Order fulfillment", "Customer onboarding", "Invoice approval", "Inventory sync", "Daily reporting", "Return processing"];
export const minimalWorkflowDescriptions = ["Process new customer orders", "Welcome and provision new accounts", "Review invoices before payment", "Synchronize stock across channels", "Prepare the daily operations summary", "Coordinate returns and refunds"];

export const minimalLogEntries = [
  "Request received", "Order validation completed", "Inventory reserved", "Confirmation sent", "Workflow run completed", "Run inspection opened", "Retry scheduled after temporary failure", "Request received"
].map((message, index) => ({
  sequence: index + 1, timestamp: `2026-10-31T10:42:${String(10 + index).padStart(2, "0")}.302Z`,
  level: index === 6 ? "Warning" : "Information", category: "Elsa.Workflows.Runtime", sourceId: index === 5 ? "studio" : "server", message,
  properties: { WorkflowExecutionId: "run_8f27d9", Activity: "Send confirmation", MessageId: "msg_71d9" }
}));
