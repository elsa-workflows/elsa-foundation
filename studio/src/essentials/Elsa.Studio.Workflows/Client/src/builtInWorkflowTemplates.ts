// Code-owned starter recipes. Keep discovery independent of template API/designer helpers.
export const builtInWorkflowTemplates = [
  { id: "blank", name: "Blank workflow", description: "Choose a root activity and start with an empty graph." },
  { id: "http", name: "HTTP request and response", description: "An HTTP endpoint connected to an HTTP response." }
] as const;
