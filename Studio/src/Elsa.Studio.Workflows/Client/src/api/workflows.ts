// Private compatibility barrel for existing Workflows module imports. New code should import the
// owning domain client directly; no routes or fallback behavior live here.
export * from "./capabilities";
export * from "./workflowDesign";
export * from "./activityDesign";
export * from "./expressions";
export * from "./publishing";
export * from "./runtime";
