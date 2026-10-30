import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentContextCollection, StudioAgentMode, StudioAgentSurface } from "../../sdk";

export async function collectAgentContext(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  mode: StudioAgentMode,
  sessionId?: string,
  explicit: StudioAgentContextAttachment[] = [],
  signal?: AbortSignal
): Promise<StudioAgentContextCollection> {
  const providers = api.agent.contextProviders.list()
    .filter(provider => provider.surfaces.includes("*") || provider.surfaces.some(item => surface.route === item || surface.route.startsWith(`${item}/`)))
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500));
  const providerIds = new Set<string>();
  const attachments = [...explicit];
  const diagnostics: StudioAgentContextCollection["diagnostics"] = [];
  for (const provider of providers) {
    signal?.throwIfAborted();
    if (providerIds.has(provider.id)) throw new Error(`Duplicate context contributor '${provider.id}'.`);
    providerIds.add(provider.id);
    // Expected limitations are typed outcomes. Unexpected faults and cancellation propagate.
    const collected = await provider.collect({ surface, mode, sessionId, signal });
    if (Array.isArray(collected)) attachments.push(...collected);
    else { attachments.push(...collected.attachments); diagnostics.push(...collected.diagnostics); }
  }
  const ids = new Set<string>();
  for (const attachment of attachments) {
    if (ids.has(attachment.id)) throw new Error(`Duplicate context attachment '${attachment.id}'.`);
    ids.add(attachment.id);
  }
  return { attachments, diagnostics };
}
