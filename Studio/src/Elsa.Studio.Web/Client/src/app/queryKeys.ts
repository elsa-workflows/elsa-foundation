// Hierarchical query keys for the Studio shell surfaces, matching the Workflows module convention
// (see Elsa.Studio.Workflows/Client/src/api/workflows.ts): a single `as const` tree so partial keys
// invalidate everything nested beneath them.
export const shellKeys = {
  all: ["studio-shell"] as const,
  manifest: ["studio-shell", "manifest"] as const,
  hostHealth: ["studio-shell", "host-health"] as const,
  // Keyed on the concrete probe target so the Studio-host, Server-host, and backend-liveness checks
  // cache independently and can be invalidated as a group via `shellKeys.hostHealth`.
  hostHealthProbe: (probe: "studio" | "server" | "backend") => ["studio-shell", "host-health", probe] as const
};
