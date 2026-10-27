// Hierarchical query keys for the Studio shell surfaces, matching the Workflows module convention
// (see Elsa.Studio.Workflows/Client/src/api/workflows.ts): a single `as const` tree so partial keys
// invalidate everything nested beneath them.
export const shellKeys = {
  all: ["studio-shell"] as const,
  manifest: ["studio-shell", "manifest"] as const,
  hostHealth: ["studio-shell", "host-health"] as const
};
