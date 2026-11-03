import type { ElsaStudioModuleApi, StudioAgentContextAttachment, StudioAgentContextCollection, StudioAgentContextProviderContribution, StudioAgentMode, StudioAgentSurface } from "../../sdk";

const DEFAULT_CONTRIBUTOR_TIMEOUT_MS = 10_000;
const MAX_ATTACHMENTS = 32;
const MAX_SUMMARY_LENGTH = 4096;
const MAX_CONTENT_LENGTH = 32768;

export async function collectAgentContext(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  mode: StudioAgentMode,
  sessionId?: string,
  explicit: StudioAgentContextAttachment[] = [],
  signal?: AbortSignal,
  options: { contributorTimeoutMs?: number } = {}
): Promise<StudioAgentContextCollection> {
  const timeoutMs = options.contributorTimeoutMs ?? DEFAULT_CONTRIBUTOR_TIMEOUT_MS;
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new Error("Context contributor deadline must be positive and finite.");
  signal?.throwIfAborted();
  const providers = api.agent.contextProviders.list()
    .filter(provider => provider.surfaces.includes("*") || provider.surfaces.some(item => surface.route === item || surface.route.startsWith(`${item}/`)))
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500));
  const providerIds = new Set<string>();
  const candidateIds = new Set<string>();
  const attachments: StudioAgentContextAttachment[] = [];
  const diagnostics: StudioAgentContextCollection["diagnostics"] = [];
  append(explicit, "request", true);
  for (const provider of providers) {
    signal?.throwIfAborted();
    if (providerIds.has(provider.id)) throw new Error(`Duplicate context contributor '${provider.id}'.`);
    providerIds.add(provider.id);
    const outcome = await collectBounded(provider);
    signal?.throwIfAborted();
    if (!outcome) continue;
    const collected = outcome.collection;
    if (Array.isArray(collected)) append(collected, provider.id, provider.required === true);
    else {
      if (!collected || !Array.isArray(collected.attachments) || !Array.isArray(collected.diagnostics)) throw new Error(`Malformed context contribution '${provider.id}'.`);
      append(collected.attachments, provider.id, provider.required === true);
      diagnostics.push(...JSON.parse(JSON.stringify(collected.diagnostics)));
    }
  }
  return { attachments, diagnostics };

  function append(candidates: StudioAgentContextAttachment[], contributorId: string, required: boolean) {
    // JSON is the transport boundary. Copy it now so module-owned late mutations cannot change
    // the accepted snapshot; structural/serialization faults still fail composition explicitly.
    const snapshot: StudioAgentContextAttachment[] = JSON.parse(JSON.stringify(candidates));
    for (const item of snapshot) {
      if (!item || typeof item.id !== "string" || !item.id) throw new Error(`Malformed context attachment from '${contributorId}'.`);
      if (candidateIds.has(item.id)) throw new Error(`Duplicate context attachment '${item.id}'.`);
      candidateIds.add(item.id);
    }
    if (attachments.length + snapshot.length > MAX_ATTACHMENTS || snapshot.some(item => (item.summary?.length ?? 0) > MAX_SUMMARY_LENGTH || (JSON.stringify(item.content)?.length ?? 0) > MAX_CONTENT_LENGTH)) {
      diagnostics.push({ contributorId, code: "agent.context.budget_exceeded", message: "Context exceeds the supported candidate budget; contribute a bounded snapshot or an authorized detail tool.", required });
      return;
    }
    attachments.push(...snapshot);
  }

  async function collectBounded(provider: StudioAgentContextProviderContribution) {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let cancel: (() => void) | undefined;
    let deadline = false;
    try {
      const interrupted = new Promise<never>((_resolve, reject) => {
        cancel = () => { controller.abort(signal?.reason); reject(signal?.reason ?? new DOMException("Context collection was cancelled.", "AbortError")); };
        signal?.addEventListener("abort", cancel, { once: true });
        timer = setTimeout(() => { deadline = true; controller.abort(); reject(new Error("Context contributor deadline elapsed.")); }, timeoutMs);
      });
      // The race settles independently of cooperation. The losing promise is observed but never
      // appended, so a late resolution/rejection cannot mutate this collection or become unhandled.
      return { collection: await Promise.race([Promise.resolve().then(() => { signal?.throwIfAborted(); return provider.collect({ surface, mode, sessionId, signal: controller.signal }); }), interrupted]) };
    } catch {
      signal?.throwIfAborted();
      diagnostics.push({ contributorId: provider.id, code: deadline ? "agent.context.deadline" : "agent.context.contributor_failed", message: deadline ? "Context contributor did not finish within its deadline." : "Context contributor could not provide evidence.", required: provider.required === true });
      return null;
    } finally {
      if (timer !== undefined) clearTimeout(timer);
      if (cancel) signal?.removeEventListener("abort", cancel);
    }
  }
}
