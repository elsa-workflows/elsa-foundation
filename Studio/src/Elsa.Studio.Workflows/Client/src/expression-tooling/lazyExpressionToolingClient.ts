import type {
  StudioEndpointContext,
  StudioExpressionToolingClient
} from "@elsa-workflows/studio-sdk";
import type { ExpressionToolingCacheIdentity } from "./expressionToolingClient";

export function createLazyExpressionToolingClient(
  context: StudioEndpointContext,
  cacheIdentity: ExpressionToolingCacheIdentity
): StudioExpressionToolingClient {
  let client: StudioExpressionToolingClient | undefined;
  let loading: Promise<StudioExpressionToolingClient> | undefined;
  let pendingInvalidation: Parameters<StudioExpressionToolingClient["invalidateAuthorization"]>[0] | null | undefined;
  let disposed = false;
  const getClient = () => {
    loading ??= import("./expressionToolingClient").then(module => {
      client = module.createExpressionToolingClient(context, cacheIdentity);
      if (pendingInvalidation !== undefined)
        client.invalidateAuthorization(pendingInvalidation ?? undefined);
      if (disposed) client.dispose();
      return client;
    });
    return loading;
  };

  return {
    describe: async (...args) => (await getClient()).describe(...args),
    getCatalog: async (...args) => (await getClient()).getCatalog(...args),
    getValueShape: async (...args) => (await getClient()).getValueShape(...args),
    getAuthoringContext: async (...args) => (await getClient()).getAuthoringContext(...args),
    getCompletions: async (...args) => (await getClient()).getCompletions(...args),
    getHover: async (...args) => (await getClient()).getHover(...args),
    validate: async (...args) => (await getClient()).validate(...args),
    invalidateAuthorization: revisions => {
      pendingInvalidation = revisions ?? null;
      client?.invalidateAuthorization(revisions);
    },
    dispose: () => {
      disposed = true;
      client?.dispose();
    }
  };
}
