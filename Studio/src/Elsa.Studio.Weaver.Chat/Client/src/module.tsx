import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

// Weaver is now a single, unified assistant surface owned by the host Studio shell (the Weaver dock,
// opened from the top-bar launcher). This module previously contributed a second, ask-only chat panel
// that duplicated the assistant and rendered raw provider payloads; it has been retired so there is one
// Weaver experience. The module is kept (registering no UI) so existing manifests load cleanly.
export function register(_api: ElsaStudioModuleApi) {
  // Intentionally contributes no navigation, route, panel, or AI surface.
}
