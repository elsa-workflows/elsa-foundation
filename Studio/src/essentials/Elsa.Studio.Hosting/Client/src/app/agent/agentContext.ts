import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentMode, StudioAgentSurface } from "../../sdk";

export async function collectAgentContext(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  mode: StudioAgentMode,
  sessionId?: string
): Promise<StudioAgentContextAttachment[]> {
  const providers = api.agent.contextProviders
    .list()
    .filter(provider => provider.surfaces.includes("*") || provider.surfaces.some(item => surface.route === item || surface.route.startsWith(`${item}/`)))
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500));

  const results = await Promise.all(providers.map(provider => provider.collect({ surface, mode, sessionId })));
  return dedupeAttachments(results.flat());
}

function dedupeAttachments(attachments: StudioAgentContextAttachment[]) {
  const seen = new Set<string>();
  const result: StudioAgentContextAttachment[] = [];

  for (const attachment of attachments) {
    if (seen.has(attachment.id)) {
      continue;
    }

    seen.add(attachment.id);
    result.push(attachment);
  }

  return result;
}
